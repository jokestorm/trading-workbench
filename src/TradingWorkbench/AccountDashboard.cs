using System.Text;
using System.Text.Json;

namespace TradingWorkbench;

public record AccountTradeAnnotation(string Id,string Notes,decimal? Risk);
public record AccountDashboardState(string Status,string Message,string Revision,DateTimeOffset? SyncedAt,DateTimeOffset? HistoryFrom,DateTimeOffset? HistoryThrough,int FillCount,ImportedTrade[] ClosedTrades,OpenPosition[] OpenPositions)
{
    public string Source=>"Schwab transactions";
    public TradeData[] Trades=>ClosedTrades.Select(t=>t.Trade).ToArray();
}

public partial class TradeStore
{
    public Dictionary<string,AccountTradeAnnotation> AccountAnnotations()
    {
        using var db=Open();using var create=db.CreateCommand();create.CommandText="CREATE TABLE IF NOT EXISTS broker_account_annotations(id TEXT PRIMARY KEY,document TEXT NOT NULL)";create.ExecuteNonQuery();
        using var select=db.CreateCommand();select.CommandText="SELECT id,document FROM broker_account_annotations";using var reader=select.ExecuteReader();var result=new Dictionary<string,AccountTradeAnnotation>();
        while(reader.Read())result.Add(reader.GetString(0),JsonSerializer.Deserialize<AccountTradeAnnotation>(reader.GetString(1),Json)!);return result;
    }
    public void SaveAccountAnnotation(AccountTradeAnnotation annotation)
    {
        using var db=Open();using var save=db.CreateCommand();save.CommandText="INSERT INTO broker_account_annotations VALUES($id,$document) ON CONFLICT(id) DO UPDATE SET document=excluded.document";
        save.Parameters.AddWithValue("$id",annotation.Id);save.Parameters.AddWithValue("$document",JsonSerializer.Serialize(annotation,Json));save.ExecuteNonQuery();
    }
}

// Dashboard accounting is a projection of archived broker transactions, never CSV rows.
public sealed class AccountDashboard
{
    private readonly TradeStore store;private readonly string path;
    private readonly Func<CancellationToken,Task<BrokerTransactionReport>> refresh;
    private readonly object sync=new();private readonly SemaphoreSlim gate=new(1,1);
    private readonly ILogger<AccountDashboard> log;private AccountDashboardState state;
    public AccountDashboard(TradeStore store,string directory,Func<CancellationToken,Task<BrokerTransactionReport>> refresh,ILogger<AccountDashboard>? logger=null)
    {
        this.store=store;this.refresh=refresh;store.EnsureBrokerJournal();store.AccountAnnotations();Directory.CreateDirectory(directory);path=Path.Combine(directory,"current.json");
        log=logger??Microsoft.Extensions.Logging.Abstractions.NullLogger<AccountDashboard>.Instance;
        state=File.Exists(path)?JsonSerializer.Deserialize<AccountDashboardState>(File.ReadAllText(path),CodexAnalysisProvider.Json)!:new("waiting","Connecting to Schwab transactions…","initial",null,null,null,0,[],[]);
    }
    public AccountDashboardState Current(){lock(sync)return state;}
    public TradeData[] Trades()=>Current().Trades;
    private void Save(AccountDashboardState next)
    {
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(next,CodexAnalysisProvider.Json));
        for(var attempt=0;;attempt++)
        {
            try{File.Move(path+".tmp",path,true);break;}
            catch(Exception e) when(attempt<4&&e is IOException or UnauthorizedAccessException){Thread.Sleep(20*(attempt+1));}
        }
        state=next;
    }
    public async Task<AccountDashboardState> Sync(CancellationToken token)
    {
        if(!await gate.WaitAsync(0,token))return Current();
        try
        {
            var report=await refresh(token);token.ThrowIfCancellationRequested();
            lock(sync)
            {
                if(report.Status!="ready"||report.History is null)
                {
                    Save(state with{Status="failed",Message=report.Message});return state;
                }
                if(state.Status=="ready"&&state.SyncedAt==report.LastSynced)return state;
                try
                {
                    var history=report.History;
                    if(history.Accounts.Length!=1)throw new FormatException("Select one linked account before calculating the account dashboard.");
                    var account=history.Accounts[0];var archived=store.ArchiveBrokerHistory(history);
                    var journal=BrokerJournalAdapter.BuildAccount(account.Id,"Schwab futures",archived);
                    var annotations=store.AccountAnnotations();var saved=store.List();
                    var trades=journal.Trades.Select(item=>
                    {
                        var trade=item.Trade;
                        if(!annotations.TryGetValue(trade.Id,out var annotation))
                        {
                            var matches=saved.Where(t=>BrokerJournalAdapter.Equivalent(t,trade)).ToArray();
                            if(matches.Length==1&&(matches[0].Notes.Length>0||matches[0].Risk.HasValue))
                            {
                                annotation=new(trade.Id,matches[0].Notes,matches[0].Risk);store.SaveAccountAnnotation(annotation);
                            }
                        }
                        return annotation is null?item:item with{Trade=trade with{Notes=annotation.Notes,Risk=annotation.Risk}};
                    }).ToArray();
                    var content=JsonSerializer.Serialize(new{trades,journal.OpenPositions},CodexAnalysisProvider.Json);
                    var revision=CsvImporter.Fingerprint(content);
                    var from=state.HistoryFrom.HasValue&&state.HistoryFrom<history.From?state.HistoryFrom:history.From;
                    Save(new("ready","Account dashboard updated from Schwab transaction history.",revision,report.LastSynced,from,history.Through,archived.Length,trades,journal.OpenPositions));
                }
                catch(FormatException e){Save(state with{Status="failed",Message=e.Message});}
                return state;
            }
        }
        finally{gate.Release();}
    }
    public TradeData Annotate(string id,TradeData input)
    {
        lock(sync)
        {
            var existing=state.ClosedTrades.SingleOrDefault(t=>t.Trade.Id==id)??throw new FormatException("That transaction-derived trade was not found.");
            var updated=existing.Trade with{Notes=input.Notes??"",Risk=input.Risk};var errors=updated.Validate();
            if(errors.Count>0)throw new FormatException(string.Join(" ",errors.Values.SelectMany(v=>v)));
            store.SaveAccountAnnotation(new(id,updated.Notes,updated.Risk));
            var closed=state.ClosedTrades.Select(t=>t.Trade.Id==id?t with{Trade=updated}:t).ToArray();
            Save(state with{ClosedTrades=closed,Revision=Guid.NewGuid().ToString("N")});return updated;
        }
    }
    public void RequireData()
    {
        if(Current().SyncedAt is null)throw new FormatException("Schwab transaction history has not been loaded yet. Sync the account dashboard first.");
    }
    public async Task Run(CancellationToken token)
    {
        while(!token.IsCancellationRequested)
        {
            try{await Sync(token);await Task.Delay(TimeSpan.FromSeconds(30),token);}
            catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
            catch(Exception e){log.LogWarning("Account dashboard sync failed errorType={ErrorType}",e.GetType().Name);try{await Task.Delay(TimeSpan.FromSeconds(30),token);}catch(OperationCanceledException){break;}}
        }
    }
    public static void Map(WebApplication app,AccountDashboard dashboard)
    {
        app.MapGet("/api/account/dashboard",()=>dashboard.Current());
        app.MapPost("/api/account/sync",async(CancellationToken token)=>await dashboard.Sync(token));
        app.MapGet("/api/account/analytics",(HttpRequest request)=>
        {
            try{dashboard.RequireData();return Results.Ok(Analytics.Calculate(TradeFilter.Read(request.Query).Apply(dashboard.Trades())));}
            catch(FormatException e){return Results.BadRequest(new{message=e.Message});}
        });
        app.MapGet("/api/account/trades/export",(HttpRequest request)=>
        {
            try{dashboard.RequireData();return Results.File(Encoding.UTF8.GetBytes("\uFEFF"+Analytics.Export(TradeFilter.Read(request.Query).Apply(dashboard.Trades()))),"text/csv; charset=utf-8","account-transactions-filtered.csv");}
            catch(FormatException e){return Results.BadRequest(new{message=e.Message});}
        });
        app.MapGet("/api/account/trades/{id}/fills",(string id)=>
        {
            var trade=dashboard.Current().ClosedTrades.SingleOrDefault(t=>t.Trade.Id==id);
            return trade is null?Results.NotFound():Results.Ok(trade.Executions.Select(f=>new{role=f.Side==(trade.Trade.Direction=="LONG"?"BUY":"SELL")?"ENTRY":"EXIT",execution=f}));
        });
        app.MapPut("/api/account/trades/{id}",(string id,TradeData input)=>
        {
            try{return Results.Ok(dashboard.Annotate(id,input));}catch(FormatException e){return Results.BadRequest(new{message=e.Message});}
        });
    }
}
