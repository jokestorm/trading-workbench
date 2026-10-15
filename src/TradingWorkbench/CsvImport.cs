using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TradingWorkbench;

public record ImportRequest(string FileName, string Csv, Dictionary<string, int> Columns, string Account, string AssetType, decimal Multiplier, string SourceTimeZone="Eastern");
public record Execution(string Id, string SourceFingerprint, string Account, string Symbol, string AssetType, string Side, decimal Quantity, decimal Price, decimal Fees, decimal Multiplier, string Timestamp, int Row, string ExternalId);
public record ImportIssue(int Row, string Message);
public record ImportedTrade(TradeData Trade, Execution[] Executions);
public record OpenPosition(string Account,string Symbol,decimal Quantity,decimal AverageEntry,decimal Multiplier,decimal EntryFees,Execution[] Executions);
public record ImportPreview(string Token, string FileName, int RowCount, ImportedTrade[] Trades, ImportIssue[] Errors, string[] Warnings, int Duplicates, decimal NetPnl,OpenPosition[] OpenPositions,StatementReconciliation? Reconciliation);
public record CommitRequest(string Token);

public static class CsvReader
{
    public static List<string[]> Parse(string csv)
    {
        var result = new List<string[]>(); var row = new List<string>(); var cell = new StringBuilder();
        var quoted = false; var afterQuote = false; csv = csv.TrimStart('\uFEFF');
        void Cell() { row.Add(cell.ToString()); cell.Clear(); afterQuote = false; }
        void Row() { Cell(); if (row.Any(c => !string.IsNullOrWhiteSpace(c))) result.Add(row.ToArray()); row.Clear(); }
        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (quoted)
            {
                if(c == '"') { if(i+1 < csv.Length && csv[i+1]=='"') { cell.Append('"'); i++; } else { quoted=false; afterQuote=true; } }
                else cell.Append(c);
            }
            else if(c=='"') { if(cell.ToString()=="=")cell.Clear(); if(cell.Length>0 || afterQuote) throw new FormatException("Unexpected quote in CSV."); quoted=true; }
            else if(c==',') Cell();
            else if(c is '\r' or '\n') { if(c=='\r' && i+1<csv.Length && csv[i+1]=='\n')i++; Row(); }
            else { if(afterQuote && !char.IsWhiteSpace(c))throw new FormatException("Unexpected text after a quoted value."); if(!afterQuote)cell.Append(c); }
        }
        if(quoted)throw new FormatException("CSV contains an unclosed quoted value.");
        if(cell.Length>0 || row.Count>0 || afterQuote)Row();
        return result;
    }
}

// This adapter expects executions, not order requests, balance changes or tax lots.
public static class CsvImporter
{
    public static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static (List<ImportedTrade> Trades, List<ImportIssue> Errors, List<string> Warnings, int Rows,List<OpenPosition> OpenPositions) Build(ImportRequest request,bool allowOpenEnd=false)
    {
        var rows = CsvReader.Parse(request.Csv);
        var errors = new List<ImportIssue>(); var warnings = new List<string>(); var fills = new List<Execution>();
        if(rows.Count < 2)throw new FormatException("The CSV needs a header row and at least one execution.");
        if(rows.Count > 5001)throw new FormatException("Import at most 5,000 executions at a time.");
        if(string.IsNullOrWhiteSpace(request.Account) || request.Account.Length>80)throw new FormatException("Enter an account label of up to 80 characters.");
        foreach(var required in new[]{"symbol","side","quantity","price","timestamp"})
            if(!request.Columns.TryGetValue(required,out var column) || column<0 || column>=rows[0].Length)throw new FormatException($"Map the {required} column.");
        if(!request.Columns.TryGetValue("fees",out var feeColumn) || feeColumn<0)warnings.Add("No fee column mapped: fees are assumed to be zero. Confirm this before importing.");
        if(!request.Columns.TryGetValue("executionId",out var idColumn) || idColumn<0)warnings.Add("No execution ID mapped: duplicate detection uses the full execution values. Identical executions need distinct IDs.");
        string Read(string[] row, string field) => request.Columns.TryGetValue(field,out var index) && index>=0 && index<row.Length ? row[index].Trim() : "";
        decimal Number(string value, string name)
        {
            var text=value.Trim().Replace("$","");
            if(!decimal.TryParse(text,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint|NumberStyles.AllowThousands,CultureInfo.InvariantCulture,out var number))throw new FormatException($"Invalid {name}: {value}.");
            return number;
        }
        var seen = new HashSet<string>();
        for(var i=1;i<rows.Count;i++)
        {
            try
            {
                var row=rows[i]; if(row.Length!=rows[0].Length)throw new FormatException("Column count differs from the header.");
                var symbol=Read(row,"symbol").ToUpperInvariant(); var side=Read(row,"side").ToUpperInvariant();
                side=side switch {"BUY" or "BOT" or "BOUGHT"=>"BUY", "SELL" or "SOLD"=>"SELL", _=>throw new FormatException("Side must be BUY/SELL, BOT/SOLD or BOUGHT/SOLD. Non-trade transactions are unsupported.")};
                var quantity=Number(Read(row,"quantity"),"quantity"); var price=Number(Read(row,"price"),"price");
                var feeText=Read(row,"fees"); var fees=feeText.Length==0?0m:Number(feeText,"fees");
                var assetType=Read(row,"assetType"); if(assetType.Length==0)assetType=request.AssetType; assetType=assetType.ToUpperInvariant() switch {"EQUITY" or "STOCK"=>"Equity","FUTURES" or "FUTURE"=>"Futures",_=>throw new FormatException("Asset type must be Equity or Futures.")};
                var multiplierText=Read(row,"multiplier"); var multiplier=multiplierText.Length==0?request.Multiplier:Number(multiplierText,"point value");
                var known=InstrumentCatalog.All.FirstOrDefault(p=>p.AssetType=="Futures" && Regex.IsMatch(symbol,$"^/?{p.Root}[FGHJKMNQUVXZ][0-9]{{1,4}}$"));
                if(known is not null && (assetType!="Futures" || multiplier!=known.PointValue))throw new FormatException($"{symbol} must use Futures with the configured {known.Root} point value of {known.PointValue}. Map per-row type/point value or choose the correct default.");
                if(assetType=="Futures" && InstrumentCatalog.All.Any(p=>p.AssetType=="Futures"&&symbol.TrimStart('/')==p.Root))throw new FormatException("Futures executions need a dated contract symbol, not just the root.");
                var formats=new[]{"yyyy-MM-ddTHH:mm:ss","yyyy-MM-ddTHH:mm","yyyy-MM-dd HH:mm:ss","yyyy-MM-dd HH:mm","M/d/yyyy H:mm:ss","M/d/yyyy H:mm"};
                if(!DateTime.TryParseExact(Read(row,"timestamp"),formats,CultureInfo.InvariantCulture,DateTimeStyles.None,out var timestamp))throw new FormatException("Execution time must include date and time in Eastern Time, for example 2026-10-06 09:30:00. Date-only transactions cannot establish trade timing.");
                var time=timestamp.ToString("yyyy-MM-ddTHH:mm:ss",CultureInfo.InvariantCulture);
                var validation=new TradeData{Symbol=symbol,AssetType=assetType,Quantity=quantity,Entry=price,Exit=price,Multiplier=multiplier,Fees=fees,OpenedAt=time,Date=time[..10],Time=time[11..]}.Validate();
                if(validation.Count>0)throw new FormatException(string.Join(" ",validation.Values.SelectMany(v=>v)));
                var account=request.Account.Trim();
                var identity=Read(row,"executionId");
                string Stable(decimal value)=>value.ToString("G29",CultureInfo.InvariantCulture);
                var canonical=JsonSerializer.Serialize(new{account,symbol,side,quantity=Stable(quantity),price=Stable(price),fees=Stable(fees),multiplier=Stable(multiplier),assetType,time});
                var id=Fingerprint(identity.Length>0?$"{account}|execution|{identity}":canonical);
                if(!seen.Add(id))throw new FormatException("Repeated execution identity in this CSV. Remove duplicate rows or supply distinct execution IDs for identical fills.");
                fills.Add(new(id,Fingerprint(canonical),account,symbol,assetType,side,quantity,price,fees,multiplier,time,i+1,identity));
            }
            catch(FormatException error){errors.Add(new(i+1,error.Message));}
        }
        var trades=new List<ImportedTrade>();
        var openPositions=new List<OpenPosition>();
        foreach(var group in fills.GroupBy(f=>new{f.Account,f.Symbol}))
        {
            decimal position=0; var portions=new List<Execution>(); string direction="";
            foreach(var fill in group.OrderBy(f=>f.Timestamp).ThenBy(f=>f.Row))
            {
                if(portions.Count>0 && (fill.Multiplier!=portions[0].Multiplier || fill.AssetType!=portions[0].AssetType)){errors.Add(new(fill.Row,"Point value or asset type changed while a position was open."));break;}
                var sign=fill.Side=="BUY"?1m:-1m;
                var remaining=fill.Quantity; var remainingFees=fill.Fees;
                while(remaining>0)
                {
                    if(position==0)direction=sign>0?"LONG":"SHORT";
                    var closing=position!=0 && Math.Sign(position)!=sign;
                    var quantity=closing?Math.Min(Math.Abs(position),remaining):remaining;
                    var fees=quantity==remaining?remainingFees:fill.Fees*quantity/fill.Quantity;
                    portions.Add(fill with{Quantity=quantity,Fees=fees});
                    remaining-=quantity;remainingFees-=fees;position+=sign*quantity;
                    if(position!=0)continue;
                    var entries=portions.Where(f=>f.Side==(direction=="LONG"?"BUY":"SELL")).ToArray();
                    var exits=portions.Where(f=>f.Side!=(direction=="LONG"?"BUY":"SELL")).ToArray();
                    var total=entries.Sum(f=>f.Quantity);
                    var gross=(exits.Sum(f=>f.Quantity*f.Price)-entries.Sum(f=>f.Quantity*f.Price))*(direction=="LONG"?1m:-1m)*fill.Multiplier;
                    var end=portions[^1].Timestamp;
                    var trade=new TradeData{Id="csv-"+Fingerprint(string.Join("|",portions.Select(f=>$"{f.Id}:{f.Quantity.ToString("G29",CultureInfo.InvariantCulture)}:{f.Side}"))),Account=fill.Account,Source="CSV",Symbol=fill.Symbol,AssetType=fill.AssetType,Direction=direction,Quantity=total,Entry=entries.Sum(f=>f.Quantity*f.Price)/total,Exit=exits.Sum(f=>f.Quantity*f.Price)/total,Fees=portions.Sum(f=>f.Fees),Multiplier=fill.Multiplier,OpenedAt=portions[0].Timestamp,Date=end[..10],Time=end[11..],AccountingGrossPnl=gross};
                    trades.Add(new(trade,portions.ToArray()));portions.Clear();
                }
            }
            if(position!=0)
            {
                if(allowOpenEnd && portions.All(f=>f.Side==(position>0?"BUY":"SELL")))openPositions.Add(new(group.Key.Account,group.Key.Symbol,position,portions.Sum(f=>f.Quantity*f.Price)/Math.Abs(position),portions[0].Multiplier,portions.Sum(f=>f.Fees),portions.ToArray()));
                else errors.Add(new(portions[0].Row,$"{group.Key.Symbol} has an unclosed or partly realized position of {position}. Include the complete flat-to-flat history."));
            }
        }
        warnings.Add("The file must begin with no existing position for each symbol. Verify this; sell-first rows are interpreted as short entries.");
        if(fills.GroupBy(f=>new{f.Symbol,f.Timestamp}).Any(g=>g.Select(f=>f.Side).Distinct().Count()>1))warnings.Add("Some buy and sell executions share a timestamp. CSV row order breaks these ties; verify the resulting grouping.");
        return(trades,errors,warnings,rows.Count-1,openPositions);
    }
}

public class ImportService(TradeStore store)
{
    private readonly Dictionary<string,(ImportRequest Request,DateTime Expires)> previews=new();
    private readonly object gate=new();
    public ImportPreview Preview(ImportRequest request)
    {
        lock(gate)
        {
            foreach(var expired in previews.Where(p=>p.Value.Expires<DateTime.UtcNow).Select(p=>p.Key).ToArray())previews.Remove(expired);
            if(previews.Count>=10)previews.Remove(previews.Keys.First());
            var token=Guid.NewGuid().ToString("N");var result=Evaluate(request,token);
            previews[token]=(request,DateTime.UtcNow.AddMinutes(20));return result;
        }
    }
    private ImportPreview Evaluate(ImportRequest request,string token)
    {
        if(request.Csv is null || request.Columns is null || request.FileName is null)throw new FormatException("CSV, filename and column mapping are required.");
        if(request.Csv.Length>2_000_000)throw new FormatException("CSV must be smaller than 2 MB.");
        var extended=false;
        var statement=ThinkorswimStatement.IsStatement(request.Csv)?ThinkorswimStatement.Normalize(StatementHistory.Extend(request,store.StatementHistory(request.Account),out extended)):null;
        var built=CsvImporter.Build(statement?.Normalized??request,statement is not null);var newTrades=new List<ImportedTrade>();var duplicates=0;
        var reconciliation=statement?.Reconcile(built.Trades,built.OpenPositions);
        if(statement?.CashSettlementCloses>0)built.Warnings.Add($"Includes {statement.CashSettlementCloses} cash-settled expiration close(s). Exit prices are derived from the broker cash adjustment and preceding official settlement mark, not an exchange execution.");
        foreach(var change in statement?.CashBalanceChanges??[])built.Warnings.Add($"Review unitemized cash adjustment on {change.Date}: {change.Amount.ToString("+0.00;-0.00;0.00",CultureInfo.InvariantCulture)} USD between reported daily balances. Its cause is unverified; it is separate from reported sweeps, fees and trade P/L.");
        if(statement?.ClosingOrderContinuations>0)built.Warnings.Add("Split fills of a verified closing order cross through flat into a reversal. Ledger quantities determine the resulting positions; the order-level TO CLOSE label is retained only as source context.");
        if(statement?.NearTimeMatches>0)built.Warnings.Add($"Matched {statement.NearTimeMatches} ledger posting(s) to Trade History within one second using identical contract, side, quantity and price. Reporting uses the matched execution time; ambiguous matches remain blocked.");
        if(extended)built.Warnings.Add("Saved statement history restores opening executions omitted by this rolling statement. Reconciliation totals cover the combined history; only new closed trades are imported.");
        if(reconciliation is not null)
        {
            var observed=built.Trades.SelectMany(t=>t.Executions).Concat(built.OpenPositions.SelectMany(p=>p.Executions)).DistinctBy(f=>f.Id).ToDictionary(f=>f.Id,f=>f.SourceFingerprint);
            foreach(var pending in store.Positions().Where(p=>p.Account==request.Account.Trim()&&reconciliation.Symbols.Contains(p.Symbol)))
                if(pending.Executions.Any(f=>!observed.TryGetValue(f.Id,out var fingerprint)||fingerprint!=f.SourceFingerprint))built.Errors.Add(new(0,"The statement omits or changes opening executions of a saved open position. Include consistent overlapping complete history before updating it."));
        }
        if(statement is not null){built.Warnings.RemoveAll(w=>w.StartsWith("The file must begin"));built.Warnings.Add("Statement futures executions and both fee columns are used. Cash sweeps, settlements, order requests and YTD totals are excluded from closed-trade P/L.");built.Warnings.Add($"Source execution timezone: {request.SourceTimeZone}. Reporting times are converted to Eastern; the statement's trade date is not its execution date.");}
        if(reconciliation is not null && !reconciliation.Reconciled)built.Errors.Add(new(0,"Statement cash/fees/position reconciliation failed. Review the statement before importing."));
        if(store.List().Any(t=>t.Source=="Manual" && built.Trades.Any(c=>c.Trade.Account==t.Account&&c.Trade.Symbol==t.Symbol&&c.Trade.Date==t.Date)))built.Warnings.Add("Manual trades exist for the same account, symbol and closing date. They are not matched to CSV executions; review potential double counting before importing.");
        foreach(var candidate in built.Trades)
        {
            var status=store.ImportStatus(candidate);
            if(status=="duplicate"){duplicates++;continue;}
            if(status!="new"){built.Errors.Add(new(candidate.Executions[0].Row,status));continue;}
            newTrades.Add(candidate);
        }
        return new(token,Path.GetFileName(request.FileName),built.Rows,newTrades.ToArray(),built.Errors.ToArray(),built.Warnings.ToArray(),duplicates,newTrades.Sum(t=>t.Trade.NetPnl),built.OpenPositions.ToArray(),reconciliation);
    }
    public object Commit(string token)
    {
        lock(gate)
        {
            if(string.IsNullOrWhiteSpace(token) || !previews.TryGetValue(token,out var pending) || pending.Expires<DateTime.UtcNow)throw new FormatException("Preview expired. Preview the CSV again.");
            var preview=Evaluate(pending.Request,token);
            if(preview.Errors.Length>0)throw new FormatException("Resolve all import errors before saving. No records were written.");
            store.CommitImport(preview,pending.Request);
            previews.Remove(token);return new{imported=preview.Trades.Length,duplicates=preview.Duplicates,openPositions=preview.OpenPositions.Length};
        }
    }
}
