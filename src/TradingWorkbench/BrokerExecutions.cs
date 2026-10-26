using System.Text.Json;

namespace TradingWorkbench;

public record BrokerTransactionReport(string Status,string Message,DateTimeOffset? LastSynced=null,DateTimeOffset? LastAttempt=null,BrokerHistory? History=null);

public static class BrokerExecutions
{
    public static (AnalysisPosition Position,bool CrossedFlat) Apply(AnalysisPosition starting,IEnumerable<BrokerFill> source)
    {
        var position=DecisionRules.Position(starting);
        if(position.State=="UNKNOWN")throw new FormatException("Establish the starting position before following transaction fills.");
        var fills=source.OrderBy(f=>f.Time).ThenBy(f=>f.Order,StringComparer.Ordinal).ThenBy(f=>f.Effect=="CLOSING"?0:1).ThenBy(f=>f.Id,StringComparer.Ordinal).ToArray();
        if(fills.Length==0)return(position,false);
        if(fills.Select(f=>f.Symbol).Distinct().Count()!=1)throw new FormatException("Reconcile each contract separately.");
        var initial=position.State=="LONG"?position.Quantity!.Value:position.State=="SHORT"?-position.Quantity!.Value:0;
        var baseline=initial==0?Array.Empty<BrokerFill>():[new BrokerFill("baseline","baseline",fills[0].Symbol,fills[0].Time.AddTicks(-1),initial,position.AverageEntry!.Value,"OPENING")];
        var report=BrokerPositionCalculator.Calculate(new(fills[0].Time.AddTicks(-1),fills[^1].Time,[new("monitor","Selected account",baseline.Concat(fills).ToArray())])).Single();
        if(report.State=="UNKNOWN")throw new FormatException("Transaction fills do not reconcile with the recorded position. Confirm the current broker position.");
        decimal qty=initial;var crossed=false;
        foreach(var fill in fills){if(qty!=0&&Math.Sign(qty)!=Math.Sign(fill.Amount)&&Math.Abs(fill.Amount)>=Math.Abs(qty))crossed=true;qty+=fill.Amount;}
        return(new(report.State,report.Quantity,report.AverageEntry),crossed);
    }
}

// One canonical fill source: the existing Schwab TRADE transactions transport.
public sealed class BrokerExecutionService
{
    private readonly string path;private readonly Func<CancellationToken,Task<BrokerHistory>> load;
    private readonly object sync=new();private readonly SemaphoreSlim gate=new(1,1);
    private readonly ILogger<BrokerExecutionService> log;private BrokerTransactionReport report;
    public BrokerExecutionService(string directory,Func<CancellationToken,Task<BrokerHistory>> load,ILogger<BrokerExecutionService>? logger=null)
    {
        Directory.CreateDirectory(directory);path=Path.Combine(directory,"current.json");this.load=load;
        log=logger??Microsoft.Extensions.Logging.Abstractions.NullLogger<BrokerExecutionService>.Instance;
        report=File.Exists(path)?JsonSerializer.Deserialize<BrokerTransactionReport>(File.ReadAllText(path),CodexAnalysisProvider.Json)!:new("waiting","Transaction fills have not synced yet.");
    }
    public BrokerTransactionReport Current(){lock(sync)return report;}
    public async Task<BrokerTransactionReport> Refresh(CancellationToken token)
    {
        if(!await gate.WaitAsync(0,token))return Current();
        try
        {
            var before=Current();var now=DateTimeOffset.UtcNow;if(before.LastAttempt.HasValue&&now-before.LastAttempt<TimeSpan.FromSeconds(10))return before;
            BrokerTransactionReport next;
            try
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(20));
                var history=await load(timeout.Token).WaitAsync(timeout.Token);token.ThrowIfCancellationRequested();
                foreach(var account in history.Accounts)
                {
                    if(account.Fills.Any(f=>f.Time>history.Through.AddSeconds(5)))throw new FormatException("Transaction history contains a future-dated fill.");
                    var prior=before.History?.Accounts.FirstOrDefault(a=>a.Id==account.Id);
                    if(prior is not null&&prior.Fills.Any(f=>f.Time>=history.From&&f.Time<=before.History!.Through&&!account.Fills.Any(current=>current.Id==f.Id&&current.CanReplace(f))))
                        throw new FormatException("Previously seen transaction fills changed or disappeared. Reconcile the correction before following fills.");
                }
                next=new("ready","Following Schwab transaction fills.",DateTimeOffset.UtcNow,now,history);
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
            catch(Exception e) when(e is FormatException or HttpRequestException or OperationCanceledException){next=before with{Status="failed",LastAttempt=now,Message=e is FormatException?e.Message:"Transaction refresh failed. Previous fills remain historical."};}
            lock(sync){File.WriteAllText(path+".tmp",JsonSerializer.Serialize(next,CodexAnalysisProvider.Json));File.Move(path+".tmp",path,true);report=next;return report;}
        }
        finally{gate.Release();}
    }
    public async Task Run(Func<bool> enabled,CancellationToken token)
    {
        while(!token.IsCancellationRequested)
        {
            try{if(enabled())await Refresh(token);await Task.Delay(TimeSpan.FromSeconds(10),token);}
            catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
            catch(Exception e)
            {
                log.LogWarning("Transaction fill sync failed errorType={ErrorType}",e.GetType().Name);
                lock(sync)report=report with{Status="failed",Message="Transaction sync did not finish. Retrying automatically."};
                try{await Task.Delay(TimeSpan.FromSeconds(10),token);}catch(OperationCanceledException){break;}
            }
        }
    }
    public static void Map(WebApplication app,BrokerExecutionService service)
    {
        app.MapGet("/api/schwab/transactions",()=>service.Current());
        app.MapPost("/api/schwab/transactions/sync",async(CancellationToken token)=>Results.Ok(await service.Refresh(token)));
    }
}
