using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TradingWorkbench;

public record CashBalanceChange(string Date,string Time,decimal Before,decimal After)
{
    public decimal Amount=>After-Before;
}
public record StatementReconciliation(int Executions,int ClosedTrades,decimal ClosedGross,decimal ClosedFees,decimal ClosedNet,decimal AllFees,decimal SettlementAmounts,decimal TradeAmounts,decimal PeriodLedgerNet,decimal OpenSettlementPnl,decimal OpenEntryFees,decimal OpeningFuturesCash,decimal ClosingFuturesCash,decimal Sweeps,bool Reconciled,string[] Symbols)
{
    public CashBalanceChange[] CashBalanceChanges {get;init;}=[];
}
public record NormalizedStatement(ImportRequest Normalized,List<string[]> Ledger,List<string[]> Holdings,int Executions)
{
    public int CashSettlementCloses {get;init;}
    public int ClosingOrderContinuations {get;init;}
    public int NearTimeMatches {get;init;}
    public CashBalanceChange[] CashBalanceChanges {get;init;}=[];
    public StatementReconciliation Reconcile(List<ImportedTrade> trades,List<OpenPosition> open)
    {
        decimal Number(string s)=>ThinkorswimStatement.Money(s);
        var initial=Number(Ledger[0][9]);var ending=Number(Ledger[^1][9]);
        var allFees=-Ledger.Where(r=>r[3]=="TRD").Sum(r=>Number(r[6])+Number(r[7]));
        var settlements=Ledger.Where(r=>r[3]=="ADJ").Sum(r=>Number(r[8]));var amounts=Ledger.Where(r=>r[3]=="TRD").Sum(r=>Number(r[8]));var sweeps=Ledger.Where(r=>r[3]=="FSWP").Sum(r=>Number(r[8]));
        var openMtm=0m;var valid=true;
        foreach(var p in open)
        {
            var last=Ledger.LastOrDefault(r=>r[3]=="ADJ"&&r[5].StartsWith($"/{p.Symbol}:"));
            var match=last is null?null:Regex.Match(last[5],@"mark to market at ([0-9.]+) official settlement price");
            if(match is null||!match.Success){valid=false;continue;}
            var settlement=Number(match.Groups[1].Value);openMtm+=(settlement-p.AverageEntry)*p.Quantity*p.Multiplier;
            var holding=Holdings.FirstOrDefault(r=>r.Length>=5&&r[0].TrimStart('/')==p.Symbol);
            if(holding is null||Number(holding[4])!=p.Quantity)valid=false;
        }
        foreach(var holding in Holdings.Where(r=>r.Length>=5&&r[0].StartsWith('/')))
            if(!open.Any(p=>p.Symbol==holding[0].TrimStart('/')&&p.Quantity==Number(holding[4])))valid=false;
        var gross=trades.Sum(t=>t.Trade.GrossPnl);var closedFees=trades.Sum(t=>t.Trade.Fees);var net=trades.Sum(t=>t.Trade.NetPnl);var entryFees=open.Sum(p=>p.EntryFees);var period=amounts+settlements-allFees;
        valid &= decimal.Round(gross+openMtm,2)==decimal.Round(amounts+settlements,2) && closedFees+entryFees==allFees && decimal.Round(net+openMtm-entryFees,2)==period && initial+period+sweeps+CashBalanceChanges.Sum(c=>c.Amount)==ending;
        return new(Executions,trades.Count,gross,closedFees,net,allFees,settlements,amounts,period,openMtm,entryFees,initial,ending,sweeps,valid,trades.Select(t=>t.Trade.Symbol).Concat(open.Select(p=>p.Symbol)).Distinct().ToArray()){CashBalanceChanges=CashBalanceChanges};
    }
}

public static class ThinkorswimStatement
{
    public static bool IsStatement(string csv)=>csv.TrimStart('\uFEFF').StartsWith("Account Statement for",StringComparison.OrdinalIgnoreCase)&&csv.Contains("Futures Statements");
    public static decimal Money(string text)
    {
        if(string.IsNullOrWhiteSpace(text)||text=="--")return 0;
        var value=text.Replace("$","").Replace(",","").Trim();if(value.StartsWith('(')&&value.EndsWith(')'))value="-"+value[1..^1];
        if(!decimal.TryParse(value,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var number))throw new FormatException("Unrecognized statement monetary value.");return number;
    }
    public static NormalizedStatement Normalize(ImportRequest request)
    {
        var sourceZone=request.SourceTimeZone switch {"Eastern"=>TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"),"Central"=>TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time"),_=>throw new FormatException("Choose the source execution timezone (Eastern or Central) before previewing this statement.")};
        var eastern=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var rows=CsvReader.Parse(request.Csv);
        List<string[]> Section(string name)
        {
            var start=rows.FindIndex(r=>r.Length==1&&r[0]==name);if(start<0)return name=="Futures"?[]:throw new FormatException($"Statement section missing: {name}.");
            var end=rows.FindIndex(start+1,r=>r.Length==1);if(end<0)end=rows.Count;
            return rows.Skip(start+2).Take(end-start-2).ToList();
        }
        DateTime Time(string value)
        {
            if(!DateTime.TryParseExact(value,new[]{"M/d/yy HH:mm:ss","M/d/yy H:mm:ss"},CultureInfo.InvariantCulture,DateTimeStyles.None,out var time))throw new FormatException("Unrecognized execution date/time in statement.");
            return DateTime.SpecifyKind(time,DateTimeKind.Unspecified);
        }
        string Symbol(string value)=>value.TrimStart('/').Split(':')[0];
        string Key(DateTime time,string symbol,string side,decimal quantity,decimal price)=>$"{time:yyyy-MM-ddTHH:mm:ss}|{symbol}|{side}|{quantity:G29}|{price:G29}";
        var history=Section("Account Trade History");
        var effects=new Dictionary<string,Queue<string>>();
        foreach(var row in history)
        {
            if(row.Length!=13||row[2]!="FUTURE")throw new FormatException("This statement adapter currently supports futures executions only. Other trade-history rows need their own adapter.");
            var key=Key(Time(row[1]),Symbol(row[6]),row[3],Math.Abs(Money(row[4])),Money(row[10]));
            if(!effects.ContainsKey(key))effects[key]=new();effects[key].Enqueue(row[5]);
        }
        var ledger=Section("Futures Statements").Where(r=>r.Length==10).ToList();
        if(ledger.Count==0||ledger[0][3]!="BAL")throw new FormatException("Statement must include an opening futures cash balance.");
        var canonical=new List<string[]>{new[]{"executionId","timestamp","symbol","side","quantity","price","fees","assetType","multiplier"}};
        var cashChanges=new List<CashBalanceChange>();var previousDate=Time($"{ledger[0][1]} {ledger[0][2]}").Date;
        var marks=new Dictionary<string,(decimal Price,decimal Quantity,DateTime Time)>();var settlementCloses=0;
        var closingOrders=new HashSet<string>();var continuations=0;var nearTimeMatches=0;
        var position=new Dictionary<string,decimal>();var occurrences=new Dictionary<string,int>();var cash=Money(ledger[0][9]);
        foreach(var row in ledger)
        {
            if(row[3] is not ("BAL" or "TRD" or "ADJ" or "FSWP"))throw new FormatException($"Unsupported futures ledger event: {row[3]}.");
            var fee=Money(row[6])+Money(row[7]);var amount=Money(row[8]);
            var date=Time($"{row[1]} {row[2]}").Date;
            if(row[3]=="BAL"&&cash!=Money(row[9]))
            {
                if(date<=previousDate||fee!=0||amount!=0)throw new FormatException("An inconsistent opening cash balance requires a separate daily balance row with no transaction amounts.");
                cashChanges.Add(new(date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),row[2],cash,Money(row[9])));
                cash=Money(row[9]);
            }
            previousDate=date;
            if(row[3]!="BAL")cash+=fee+amount;
            if(cash!=Money(row[9]))throw new FormatException("The futures running cash balance does not reconcile.");
            if(row[3]=="ADJ")
            {
                var mark=Regex.Match(row[5],@"^/([A-Z0-9]+):[A-Z]+ mark to market at ([0-9.]+) official settlement price$");
                if(mark.Success)marks[mark.Groups[1].Value]=(Money(mark.Groups[2].Value),position.GetValueOrDefault(mark.Groups[1].Value),Time($"{row[1]} {row[2]}"));
            }
            if(row[3]!="TRD")continue;
            var sourceTime=Time($"{row[1]} {row[2]}");
            var executionTime=sourceTime;
            if(sourceZone.IsInvalidTime(sourceTime)||sourceZone.IsAmbiguousTime(sourceTime))throw new FormatException("An execution lies in an ambiguous or invalid DST hour; use an export with an explicit offset.");
            string side,symbol,sourceId;decimal quantity,price;InstrumentDefinition definition;
            if(row[5].Trim().Equals("cash settle future",StringComparison.OrdinalIgnoreCase))
            {
                var active=position.Where(p=>p.Value!=0).ToArray();
                if(active.Length!=1)throw new FormatException("Cash settlement cannot be assigned to one verified open contract. Include complete history or a broker settlement confirmation.");
                var pending=active[0];symbol=pending.Key;
                var contract=Regex.Match(symbol,@"^MET([FGHJKMNQUVXZ])([0-9]{1,4})$");
                var year=contract.Success?contract.Groups[2].Value:"";
                if(!contract.Success||"FGHJKMNQUVXZ".IndexOf(contract.Groups[1].Value,StringComparison.Ordinal)+1!=sourceTime.Month
                    ||!(year.Length switch{1=>sourceTime.Year%10==int.Parse(year),2=>sourceTime.Year%100==int.Parse(year),4=>sourceTime.Year==int.Parse(year),_=>false}))
                    throw new FormatException("Cash settlement requires a verified Micro Ether contract in its expiration month. Other settlement formats need explicit broker details.");
                if(!marks.TryGetValue(symbol,out var basis)||basis.Quantity!=pending.Value||basis.Time>=sourceTime)
                    throw new FormatException("Cash settlement needs an official preceding mark for the unchanged open position. Include complete settlement history.");
                definition=InstrumentCatalog.All.Single(p=>p.Root=="MET");
                price=basis.Price+amount/(pending.Value*definition.PointValue);
                if(price<=0||price%.5m!=0)throw new FormatException("The cash settlement does not imply a valid Micro Ether settlement price. Review the broker settlement details.");
                side=pending.Value>0?"SELL":"BUY";quantity=Math.Abs(pending.Value);position[symbol]=0;
                sourceId=$"tos:cash-settlement:{row[4]}:{sourceTime:yyyy-MM-ddTHH:mm:ss}:{symbol}";settlementCloses++;
            }
            else
            {
                var match=Regex.Match(row[5],@"^(BOT|SOLD) ([+-][0-9]+) /([A-Z0-9]+):[A-Z]+ @([0-9.]+)$");
                if(!match.Success)throw new FormatException($"Unrecognized futures execution description on {row[1]} at {row[2]}.");
                side=match.Groups[1].Value=="BOT"?"BUY":"SELL";quantity=Math.Abs(Money(match.Groups[2].Value));symbol=match.Groups[3].Value;price=Money(match.Groups[4].Value);
                var key=Key(sourceTime,symbol,side,quantity,price);
                if(!effects.TryGetValue(key,out var matches)||matches.Count==0)
                {
                    // Ledger posting and Trade History execution clocks can
                    // differ by one second. Require exactly one otherwise
                    // identical unconsumed match, never an arbitrary nearest fill.
                    var nearby=new[]{sourceTime.AddSeconds(-1),sourceTime.AddSeconds(1)}
                        .Select(t=>(Time:t,Matches:effects.GetValueOrDefault(Key(t,symbol,side,quantity,price))))
                        .Where(p=>p.Matches?.Count>0).ToArray();
                    if(nearby.Length!=1)throw new FormatException($"A futures ledger execution for {symbol} on {row[1]} at {row[2]} has no unique matching Trade History fill.");
                    matches=nearby[0].Matches!;executionTime=nearby[0].Time;nearTimeMatches++;
                }
                var effect=matches.Dequeue();var prior=position.GetValueOrDefault(symbol);var sign=side=="BUY"?1m:-1m;
                var order=$"{symbol}|{row[4]}|{sourceTime:yyyy-MM-dd}|{side}";
                if(effect=="TO CLOSE")
                {
                    if(prior==0||Math.Sign(prior)==sign)
                    {
                        if(!closingOrders.Contains(order))throw new FormatException($"The statement begins with an unknown position or inconsistent closing fill for {symbol} on {row[1]} at {row[2]}. Include earlier history.");
                        continuations++;
                    }
                    else if(!string.IsNullOrWhiteSpace(row[4])&&row[4]!="--")closingOrders.Add(order);
                }
                if(effect is not ("TO OPEN" or "TO CLOSE"))throw new FormatException("Unrecognized position effect.");
                position[symbol]=prior+sign*quantity;
                definition=InstrumentCatalog.All.FirstOrDefault(p=>p.AssetType=="Futures"&&Regex.IsMatch(symbol,$"^{p.Root}[FGHJKMNQUVXZ][0-9]{{1,4}}$"))??throw new FormatException($"Configure the point value for {symbol} before importing.");
                sourceId=$"tos:{row[4]}:{key}";
            }
            // Any fill after a mark invalidates that mark as a complete cash
            // settlement basis; wait for a new official mark instead of guessing.
            marks.Remove(symbol);
            if(fee>0)throw new FormatException("Positive fee credits require separate reconciliation.");
            var occurrence=occurrences.GetValueOrDefault(sourceId)+1;occurrences[sourceId]=occurrence;
            var reporting=TimeZoneInfo.ConvertTime(executionTime,sourceZone,eastern);
            canonical.Add(new[]{$"{sourceId}:{occurrence}",reporting.ToString("yyyy-MM-ddTHH:mm:ss",CultureInfo.InvariantCulture),symbol,side,quantity.ToString("G29",CultureInfo.InvariantCulture),price.ToString("G29",CultureInfo.InvariantCulture),(-fee).ToString("G29",CultureInfo.InvariantCulture),"Futures",definition.PointValue.ToString("G29",CultureInfo.InvariantCulture)});
        }
        if(effects.Values.Any(q=>q.Count>0))throw new FormatException("Some Trade History fills have no matching futures ledger execution.");
        var columns=new Dictionary<string,int>{{"executionId",0},{"timestamp",1},{"symbol",2},{"side",3},{"quantity",4},{"price",5},{"fees",6},{"assetType",7},{"multiplier",8}};
        string Escape(string value)=>'"'+value.Replace("\"","\"\"")+'"';
        var normalized=request with{Csv=string.Join('\n',canonical.Select(row=>string.Join(',',row.Select(Escape)))),Columns=columns,AssetType="Futures",Multiplier=1};
        return new(normalized,ledger,Section("Futures"),canonical.Count-1){CashSettlementCloses=settlementCloses,CashBalanceChanges=cashChanges.ToArray(),ClosingOrderContinuations=continuations,NearTimeMatches=nearTimeMatches};
    }
}
