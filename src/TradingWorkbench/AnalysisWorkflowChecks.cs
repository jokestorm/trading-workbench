using System.Text.Json;
using System.Diagnostics;

namespace TradingWorkbench;

public static class AnalysisWorkflowChecks
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
    private sealed class ControlledProvider:IAnalysisProvider
    {
        public TaskCompletionSource<JsonElement> Started {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<AnalysisResult> Result {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<AnalysisResult> Analyze(JsonElement snapshot,string directory,CancellationToken token)
        {
            Started.TrySetResult(snapshot);return await Result.Task.WaitAsync(token);
        }
    }
    private static readonly AnalysisResult Answer=new("Fixture review","Saved review",["Observed data"],["Limited sample"],["Review"],"Retrospective only");
    private static async Task<AnalysisJob> Finished(AnalysisWorkflow workflow)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while(workflow.Current() is {Active:true})await Task.Delay(10,timeout.Token);
        return workflow.Current()!;
    }
    public static async Task Run()
    {
        var directory=Path.Combine(Path.GetTempPath(),"workflow-check-"+Guid.NewGuid().ToString("N"));
        var store=new TradeStore(directory);
        store.Save(new(){Id="fixture",Symbol="MES",Date="2026-10-06",Time="10:00",AccountingGrossPnl=100,Fees=5});
        var provider=new ControlledProvider();var analysis=new AnalysisService(store,provider,Path.Combine(directory,"analysis"));
        await using var workflow=new AnalysisWorkflow(analysis,Path.Combine(directory,"jobs"),(_,_)=>throw new Exception("Journal must not refresh market data"));
        var job=workflow.Start(new("journal"));
        var snapshot=await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(workflow.Start(new("journal")).Id==job.Id,"repeated Analyze clicks show the same active run");
        store.Save(new(){Id="later",Symbol="MES",Date="2026-10-06",Time="11:00",AccountingGrossPnl=200});
        provider.Result.SetResult(Answer);
        var finished=await Finished(workflow);
        Check(finished.State=="completed"&&analysis.Find(job.Id).Snapshot.GetProperty("summary").GetProperty("net").GetDecimal()==95,"background journal run saves its original frozen selection without a waiting browser");
        Check(snapshot.GetProperty("trades").GetArrayLength()==1,"journal preparation freezes selected trades at start");
        await using var reopened=new AnalysisWorkflow(analysis,Path.Combine(directory,"jobs"),(_,_)=>throw new Exception());
        Check(reopened.Current()?.State=="completed"&&reopened.Current()?.Id==job.Id,"reopening preserves completed run status and history");

        var market=new MarketStore(directory);var marketProvider=new ControlledProvider();
        var marketAnalysis=new AnalysisService(store,marketProvider,Path.Combine(directory,"market-analysis"),market);
        var refreshed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRefresh=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var marketWorkflow=new AnalysisWorkflow(marketAnalysis,Path.Combine(directory,"market-jobs"),async(request,token)=>
        {
            refreshed.SetResult();await allowRefresh.Task.WaitAsync(token);
            var now=DateTimeOffset.UtcNow;
            market.SaveBars(Enumerable.Range(0,6).Select(i=>new PriceBar(request.Symbol!,now.AddMinutes(i-6),100,101,99,100,10)).ToArray());
            var quote=new MarketQuote(request.Symbol!,"FUTURE",100,null,null,null,null,now,true,now);
            market.SaveQuote(quote);return quote;
        });
        var marketJob=marketWorkflow.Start(new("saved-market",Symbol:"/MESZ26",Days:1,Position:new("FLAT")));
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(marketWorkflow.Start(new("journal")).Id==marketJob.Id,"market and journal share one active run including the refresh phase");
        allowRefresh.SetResult();var marketSnapshot=await marketProvider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(marketSnapshot.GetProperty("symbol").GetString()=="/MESZ26"&&marketSnapshot.GetProperty("position").GetProperty("state").GetString()=="FLAT"&&marketSnapshot.GetProperty("bars").GetArrayLength()==6,"workflow refreshes observed prices before freezing the requested contract and position");
        market.SaveQuote(new("/MESZ26","FUTURE",90,null,null,null,null,DateTimeOffset.UtcNow,false,DateTimeOffset.UtcNow));
        marketProvider.Result.SetResult(Answer with{Decision=new("BUY","Fixture levels",100,99,102)});
        Check((await Finished(marketWorkflow)).State=="completed"&&marketAnalysis.Find(marketJob.Id).Result.Decision?.Action=="BUY","refreshed market run saves a validated recommendation through the workflow interface");
        Check(marketAnalysis.Find(marketJob.Id).Snapshot.GetProperty("latestQuote").GetProperty("last").GetDecimal()==100,"later market updates cannot replace a run's frozen quote");

        await using var stale=new AnalysisWorkflow(marketAnalysis,Path.Combine(directory,"stale-jobs"),(request,_)=>Task.FromResult(new MarketQuote(request.Symbol!,"FUTURE",100,null,null,null,null,DateTimeOffset.UtcNow.AddMinutes(-3),true,DateTimeOffset.UtcNow)));
        var staleJob=stale.Start(new("saved-market",Symbol:"/MESZ26",Position:new("FLAT")));
        Check((await Finished(stale)).State=="completed"&&marketAnalysis.Find(staleJob.Id).Result.Decision?.Action=="WAIT","workflow applies freshness guards before saving a market recommendation");

        await using var failing=new AnalysisWorkflow(marketAnalysis,Path.Combine(directory,"failed-jobs"),(_,_)=>throw new FormatException("Fixture refresh failure"));
        failing.Start(new("saved-market",Symbol:"/MESZ26"));
        Check((await Finished(failing)).State=="failed"&&marketAnalysis.History().Length==2,"failed refresh does not analyze cached observations or save a result");

        var canceledProvider=new ControlledProvider();var canceledAnalysis=new AnalysisService(store,canceledProvider,Path.Combine(directory,"canceled-analysis"));
        await using var canceled=new AnalysisWorkflow(canceledAnalysis,Path.Combine(directory,"canceled-jobs"),(_,_)=>throw new Exception());
        var cancelJob=canceled.Start(new("journal"));await canceledProvider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        canceled.Cancel(cancelJob.Id);await Finished(canceled);
        canceledProvider.Result.TrySetResult(Answer);
        Check(canceled.Current()?.State=="canceled"&&canceledAnalysis.History().Length==0,"explicit cancellation prevents a late provider result from being saved");
        var retry=canceled.Start(new("journal"));Check(retry.Id!=cancelJob.Id,"a canceled run allows a new explicit request");await Finished(canceled);

        var crashDirectory=Path.Combine(directory,"crash");
        using var process=new Process{StartInfo=new("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        process.StartInfo.ArgumentList.Add(typeof(AnalysisWorkflowChecks).Assembly.Location);process.StartInfo.ArgumentList.Add("--workflow-crash");process.StartInfo.ArgumentList.Add(crashDirectory);
        process.Start();
        try{Check(await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20))=="READY","crash fixture admits an actual background run");}
        finally{if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();}
        var restoredAnalysis=new AnalysisService(new TradeStore(crashDirectory),new ControlledProvider(),Path.Combine(crashDirectory,"analysis"));
        await using var restored=new AnalysisWorkflow(restoredAnalysis,Path.Combine(crashDirectory,"jobs"),(_,_)=>throw new Exception("Must not resume"));
        Check(restored.Current()?.State=="interrupted"&&restoredAnalysis.History().Length==0,"process restart marks unfinished work interrupted without another model call");
    }
    public static async Task Crash(string directory)
    {
        var store=new TradeStore(directory);store.Save(new(){Id="fixture",Symbol="MES",Date="2026-10-06",AccountingGrossPnl=100});
        var provider=new ControlledProvider();var analysis=new AnalysisService(store,provider,Path.Combine(directory,"analysis"));
        await using var workflow=new AnalysisWorkflow(analysis,Path.Combine(directory,"jobs"),(_,_)=>throw new Exception());
        workflow.Start(new("journal"));await provider.Started.Task;Console.WriteLine("READY");await Task.Delay(Timeout.Infinite);
    }
}
