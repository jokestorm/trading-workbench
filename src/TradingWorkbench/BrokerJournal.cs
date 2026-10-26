using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingWorkbench;

public record BrokerJournalResult(int Imported,int Matched,int Duplicates,string[] NeedsReconciliation,DateTimeOffset? SyncedAt,string Message);
public record BrokerAccountJournal(ImportedTrade[] Trades,OpenPosition[] OpenPositions);
public static class BrokerJournalAdapter
{
    private static readonly TimeZoneInfo Eastern=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    public static ImportedTrade[] Build(string brokerAccount,string account,IEnumerable<BrokerFill> source)=>BuildAccount(brokerAccount,account,source).Trades;
    public static BrokerAccountJournal BuildAccount(string brokerAccount,string account,IEnumerable<BrokerFill> source)
    {
        var fills=source.OrderBy(f=>f.Time).ThenBy(f=>f.Order,StringComparer.Ordinal).ThenBy(f=>f.Effect=="CLOSING"?0:1).ThenBy(f=>f.Id,StringComparer.Ordinal).ToArray();
        if(fills.Length==0)return new([],[]);
        if(fills.Any(f=>f.Fees is null or <0))throw new FormatException("Futures fees are missing or unrecognized. No journal trades were written.");
        foreach(var group in fills.GroupBy(f=>f.Symbol))
        {
            if(group.First().Effect!="OPENING")throw new FormatException($"{group.Key} history begins inside an existing position. Include earlier history before syncing its journal.");
            if(group.GroupBy(f=>f.Time).Any(bucket=>bucket.Select(f=>f.Order).Distinct().Count()>1&&bucket.Select(f=>Math.Sign(f.Amount)).Distinct().Count()>1))throw new FormatException($"{group.Key} has simultaneous opposing orders with ambiguous sequencing.");
        }
        var report=BrokerPositionCalculator.Calculate(new(DateTimeOffset.MinValue,DateTimeOffset.UtcNow,[new(brokerAccount,account,fills)]));
        if(report.Any(p=>p.State=="UNKNOWN"))throw new FormatException("Futures fills do not establish unambiguous positions. No journal trades were written.");
        string Number(decimal value)=>value.ToString("G29",CultureInfo.InvariantCulture);
        var csv=new StringBuilder("symbol,side,quantity,price,fees,timestamp,executionId,multiplier\n");
        foreach(var fill in fills)
        {
            var symbol=fill.Symbol.TrimStart('/');
            var instrument=FuturesProducts.Find(symbol)??throw new FormatException($"No contract specifications are available for {symbol}.");
            var time=TimeZoneInfo.ConvertTime(fill.Time,Eastern).ToString("yyyy-MM-ddTHH:mm:ss",CultureInfo.InvariantCulture);
            var identity="api-"+CsvImporter.Fingerprint(brokerAccount+"|"+fill.Id);
            csv.AppendLine(string.Join(',',symbol,fill.Amount>0?"BUY":"SELL",Number(Math.Abs(fill.Amount)),Number(fill.Price),Number(fill.Fees!.Value),time,identity,Number(instrument.PointValue)));
        }
        // Use the same flat-to-flat accounting and proportional reversal fees as CSV imports.
        var built=CsvImporter.Build(new("Schwab API",csv.ToString(),new(){{"symbol",0},{"side",1},{"quantity",2},{"price",3},{"fees",4},{"timestamp",5},{"executionId",6},{"multiplier",7}},account,"Futures",1),allowOpenEnd:true,allowIncompleteTail:true);
        if(built.Errors.Count>0)throw new FormatException(built.Errors[0].Message);
        return new(built.Trades.Select(t=>t with{Trade=t.Trade with{Id="api-"+t.Trade.Id[4..],Source="Schwab API"}}).ToArray(),built.OpenPositions.ToArray());
    }
    internal static bool Equivalent(TradeData a,TradeData b)
    {
        bool Near(string x,string y)=>DateTime.TryParse(x,CultureInfo.InvariantCulture,DateTimeStyles.None,out var first)&&DateTime.TryParse(y,CultureInfo.InvariantCulture,DateTimeStyles.None,out var second)&&Math.Abs((first-second).TotalSeconds)<=1;
        return a.Account==b.Account&&a.Symbol.TrimStart('/')==b.Symbol.TrimStart('/')&&a.Direction==b.Direction&&a.Quantity==b.Quantity&&a.Multiplier==b.Multiplier&&a.Entry==b.Entry&&a.Exit==b.Exit&&decimal.Round(a.GrossPnl,2)==decimal.Round(b.GrossPnl,2)&&decimal.Round(a.Fees,2)==decimal.Round(b.Fees,2)&&Near(a.OpenedAt,b.OpenedAt)&&Near(a.Date+"T"+a.Time,b.Date+"T"+b.Time);
    }
    internal static bool Overlaps(TradeData a,TradeData b)=>a.Account==b.Account&&a.Symbol.TrimStart('/')==b.Symbol.TrimStart('/')&&((string.CompareOrdinal(a.OpenedAt,b.Date+"T"+b.Time)<0&&string.CompareOrdinal(b.OpenedAt,a.Date+"T"+a.Time)<0)||(a.OpenedAt==b.OpenedAt&&a.Date==b.Date&&a.Time==b.Time));
}

public partial class TradeStore
{
    public void EnsureBrokerJournal()
    {
        using var db=Open();using var command=db.CreateCommand();command.CommandText="""
            CREATE TABLE IF NOT EXISTS broker_journal_fills(account_id TEXT NOT NULL,id TEXT NOT NULL,document TEXT NOT NULL,PRIMARY KEY(account_id,id));
            CREATE TABLE IF NOT EXISTS broker_journal_state(id TEXT PRIMARY KEY,document TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS broker_journal_links(id TEXT PRIMARY KEY,trade_id TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS broker_journal_verified(account_id TEXT NOT NULL,symbol TEXT NOT NULL,PRIMARY KEY(account_id,symbol));
            """;command.ExecuteNonQuery();
    }
    public BrokerFill[] ArchiveBrokerHistory(BrokerHistory history)
    {
        if(history.Accounts.Length!=1)throw new FormatException("Journal sync currently requires exactly one linked account. Multiple accounts need explicit journal mapping.");
        var account=history.Accounts[0];using var db=Open();using var transaction=db.BeginTransaction();
        using var mapping=db.CreateCommand();mapping.Transaction=transaction;mapping.CommandText="SELECT document FROM broker_journal_state WHERE id='account'";
        var previous=mapping.ExecuteScalar() as string;if(previous is not null&&previous!=account.Id)throw new FormatException("The linked broker account changed. Reconcile its journal mapping before syncing.");
        using var select=db.CreateCommand();select.Transaction=transaction;select.CommandText="SELECT id,document FROM broker_journal_fills WHERE account_id=$account";select.Parameters.AddWithValue("$account",account.Id);
        var archived=new Dictionary<string,BrokerFill>();using(var reader=select.ExecuteReader()){while(reader.Read())archived.Add(reader.GetString(0),JsonSerializer.Deserialize<BrokerFill>(reader.GetString(1),Json)!);}
        var incoming=account.Fills.ToDictionary(f=>f.Id);
        if(archived.Values.Any(f=>f.Time>=history.From&&f.Time<=history.Through&&!incoming.ContainsKey(f.Id)))throw new FormatException("Previously archived futures fills disappeared from the overlapping API history. Reconcile corrections before syncing.");
        foreach(var fill in account.Fills)
        {
            if(fill.Time>history.Through.AddSeconds(5)||fill.Time<history.From.AddSeconds(-1))throw new FormatException("A futures fill is outside the requested history window.");
            if(archived.TryGetValue(fill.Id,out var existing)&&!fill.CanReplace(existing))throw new FormatException("A previously archived futures fill changed. Reconcile its correction before syncing.");
            using var insert=db.CreateCommand();insert.Transaction=transaction;insert.CommandText="INSERT INTO broker_journal_fills VALUES($account,$id,$document) ON CONFLICT(account_id,id) DO UPDATE SET document=excluded.document";insert.Parameters.AddWithValue("$account",account.Id);insert.Parameters.AddWithValue("$id",fill.Id);insert.Parameters.AddWithValue("$document",JsonSerializer.Serialize(fill,Json));insert.ExecuteNonQuery();archived[fill.Id]=fill;
        }
        using var save=db.CreateCommand();save.Transaction=transaction;save.CommandText="INSERT OR IGNORE INTO broker_journal_state VALUES('account',$account)";save.Parameters.AddWithValue("$account",account.Id);save.ExecuteNonQuery();
        transaction.Commit();return archived.Values.OrderBy(f=>f.Time).ThenBy(f=>f.Id,StringComparer.Ordinal).ToArray();
    }
    public BrokerJournalResult CommitBrokerJournal(string brokerAccount,ImportedTrade[] candidates)
    {
        using var db=Open();using var transaction=db.BeginTransaction();
        var known=new List<TradeData>();using(var query=db.CreateCommand()){query.Transaction=transaction;query.CommandText="SELECT document FROM trades";using var reader=query.ExecuteReader();while(reader.Read())known.Add(JsonSerializer.Deserialize<TradeData>(reader.GetString(0),Json)!);}
        var verified=new HashSet<string>();using(var query=db.CreateCommand()){query.Transaction=transaction;query.CommandText="SELECT symbol FROM broker_journal_verified WHERE account_id=$account";query.Parameters.AddWithValue("$account",brokerAccount);using var reader=query.ExecuteReader();while(reader.Read())verified.Add(reader.GetString(0));}
        var matched=0;var duplicates=0;var planned=new List<ImportedTrade>();var links=new Dictionary<string,string>();
        foreach(var candidate in candidates)
        {
            using var linked=db.CreateCommand();linked.Transaction=transaction;linked.CommandText="SELECT trade_id FROM broker_journal_links WHERE id=$id";linked.Parameters.AddWithValue("$id",candidate.Trade.Id);
            if(linked.ExecuteScalar() is string){duplicates++;continue;}
            var matches=known.Where(t=>BrokerJournalAdapter.Equivalent(t,candidate.Trade)).ToArray();
            if(matches.Length>1)throw new FormatException("More than one saved trade matches an API trade. No journal trades were written.");
            if(matches.Length==1)
            {
                links.Add(candidate.Trade.Id,matches[0].Id);duplicates++;
                if(matches[0].Source=="CSV"){verified.Add(candidate.Trade.Symbol);matched++;}
            }
            else planned.Add(candidate);
        }
        var blocked=planned.Where(t=>!verified.Contains(t.Trade.Symbol)).Select(t=>t.Trade.Symbol).Distinct().ToArray();
        var additions=planned.Where(t=>verified.Contains(t.Trade.Symbol)).ToArray();
        foreach(var candidate in additions)
        {
            if(known.Any(t=>BrokerJournalAdapter.Overlaps(t,candidate.Trade)))throw new FormatException($"{candidate.Trade.Symbol} overlaps a saved trade with different fills or fees. No journal trades were written.");
            using var insert=db.CreateCommand();insert.Transaction=transaction;insert.CommandText="INSERT INTO trades VALUES($id,$document)";insert.Parameters.AddWithValue("$id",candidate.Trade.Id);insert.Parameters.AddWithValue("$document",JsonSerializer.Serialize(candidate.Trade,Json));insert.ExecuteNonQuery();
            for(var index=0;index<candidate.Executions.Length;index++)
            {
                var execution=candidate.Executions[index];using var fill=db.CreateCommand();fill.Transaction=transaction;fill.CommandText="INSERT INTO fills VALUES($id,$trade,$role,$document)";fill.Parameters.AddWithValue("$id",candidate.Trade.Id+"-"+index);fill.Parameters.AddWithValue("$trade",candidate.Trade.Id);fill.Parameters.AddWithValue("$role",execution.Side==(candidate.Trade.Direction=="LONG"?"BUY":"SELL")?"ENTRY":"EXIT");fill.Parameters.AddWithValue("$document",JsonSerializer.Serialize(execution,Json));fill.ExecuteNonQuery();
            }
            known.Add(candidate.Trade);links.Add(candidate.Trade.Id,candidate.Trade.Id);
        }
        foreach(var link in links){using var insert=db.CreateCommand();insert.Transaction=transaction;insert.CommandText="INSERT INTO broker_journal_links VALUES($id,$trade)";insert.Parameters.AddWithValue("$id",link.Key);insert.Parameters.AddWithValue("$trade",link.Value);insert.ExecuteNonQuery();}
        foreach(var symbol in verified){using var insert=db.CreateCommand();insert.Transaction=transaction;insert.CommandText="INSERT OR IGNORE INTO broker_journal_verified VALUES($account,$symbol)";insert.Parameters.AddWithValue("$account",brokerAccount);insert.Parameters.AddWithValue("$symbol",symbol);insert.ExecuteNonQuery();}
        transaction.Commit();return new(additions.Length,matched,duplicates,blocked,DateTimeOffset.UtcNow,blocked.Length==0?"Schwab journal synced.":"Some contracts need an exact CSV trade match before API-only trades can be imported.");
    }
}

public sealed class BrokerJournalService
{
    private readonly TradeStore store;private readonly Func<CancellationToken,Task<BrokerHistory>> load;private readonly SemaphoreSlim gate=new(1,1);
    public BrokerJournalService(TradeStore store,Func<CancellationToken,Task<BrokerHistory>> load){this.store=store;this.load=load;store.EnsureBrokerJournal();}
    public async Task<BrokerJournalResult> Sync(CancellationToken token)
    {
        if(!await gate.WaitAsync(0,token))throw new FormatException("A journal sync is already running.");
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var history=await load(timeout.Token);timeout.Token.ThrowIfCancellationRequested();
            if(history.Accounts.Length!=1)throw new FormatException("Journal sync currently requires exactly one linked account.");
            var fills=store.ArchiveBrokerHistory(history);
            var candidates=BrokerJournalAdapter.Build(history.Accounts[0].Id,"Schwab futures",fills);
            timeout.Token.ThrowIfCancellationRequested();return store.CommitBrokerJournal(history.Accounts[0].Id,candidates);
        }
        finally{gate.Release();}
    }
    public static void Map(WebApplication app,BrokerJournalService service)=>app.MapPost("/api/schwab/journal/sync",async(CancellationToken token)=>
    {
        try{return Results.Ok(await service.Sync(token));}
        catch(FormatException error){return Results.BadRequest(new{message=error.Message});}
        catch(Exception error) when(error is HttpRequestException or OperationCanceledException){return Results.Json(new{message="Schwab journal sync could not finish. Saved trades were not changed."},statusCode:502);}
    });
}
