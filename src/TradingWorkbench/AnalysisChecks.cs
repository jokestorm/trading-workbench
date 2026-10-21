using System.Text.Json;
namespace TradingWorkbench;
public static class AnalysisChecks
{
    public static async Task Run(bool live=false)
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
        var now=DateTimeOffset.UtcNow;
        JsonElement DecisionSnapshot(string state="FLAT",int age=30,bool? realtime=true)=>JsonSerializer.SerializeToElement(new{position=new{state},bars=new[]{1},asOf=now.AddSeconds(-age),latestQuote=new{realtime,quoteTime=now.AddSeconds(-age)},tickSize=.25m},CodexAnalysisProvider.Json);
        var recommendation=new AnalysisResult("BUY","Observed setup",["Price support"],["Limited window"],["Watch entry"],"Below support",new("BUY","Long setup",100,99,102));
        Check(DecisionRules.Apply(recommendation,DecisionSnapshot(),now).Decision!.Action=="BUY","fresh flat-position buy with valid levels is retained");
        Check(DecisionRules.Apply(recommendation,DecisionSnapshot("UNKNOWN"),now).Decision!.Action=="WAIT","unknown position cannot become an opening order recommendation");
        Check(DecisionRules.Apply(recommendation,DecisionSnapshot("LONG"),now).Decision!.Action=="WAIT","buy recommendation cannot silently add to an existing long");
        Check(DecisionRules.Apply(recommendation with{Decision=new("SELL_CLOSE","Exit long",null,null,null)},DecisionSnapshot("LONG"),now).Decision!.Action=="SELL_CLOSE"&&DecisionRules.Apply(recommendation with{Decision=new("BUY_COVER","Exit short",null,null,null)},DecisionSnapshot("SHORT"),now).Decision!.Action=="BUY_COVER","closing recommendations match long and short positions");
        Check(DecisionRules.Apply(recommendation,DecisionSnapshot(age:180),now).Decision!.Guarded&&DecisionRules.Apply(recommendation,DecisionSnapshot(realtime:false),now).Decision!.Action=="WAIT"&&DecisionRules.Apply(recommendation,DecisionSnapshot(realtime:null),now).Decision!.Action=="WAIT","stale, delayed and unverified realtime data do not yield current buy/sell actions");
        Check(DecisionRules.Apply(recommendation with{Decision=new("BUY","Bad stop",100,101,102)},DecisionSnapshot(),now).Decision!.Action=="WAIT"&&DecisionRules.Apply(recommendation with{Decision=new("BUY","Off tick",100.1m,99,102)},DecisionSnapshot(),now).Decision!.Action=="WAIT","invalid level direction and off-tick futures prices are rejected");
        var invalidPosition=false;try{DecisionRules.Position(new("LONG",0,100));}catch(FormatException){invalidPosition=true;}Check(invalidPosition&&DecisionRules.Position(new("FLAT",2,100)).Quantity is null,"manual position validation rejects invalid holdings and clears flat inventory");
        var directory=Path.Combine(Path.GetTempPath(),"analysis-check-"+Guid.NewGuid().ToString("N"));var store=new TradeStore(directory);
        store.Save(new(){Id="fixture",Symbol="MES",Date="2026-10-06",Time="10:00",AccountingGrossPnl=100,Fees=5,Risk=50,Notes="Private note"});
        var service=new AnalysisService(store,new FixtureProvider(),Path.Combine(directory,"analysis"));
        var preview=service.Preview(new("journal",new(Symbol:"MES")));Check(!preview.Snapshot.GetRawText().Contains("Private note")&&preview.Snapshot.GetProperty("summary").GetProperty("net").GetDecimal()==95,"journal snapshot uses filtered accounting and excludes notes by default");
        Check(!preview.Snapshot.GetRawText().Contains("setup",StringComparison.OrdinalIgnoreCase),"journal analysis contains no setup fields or setup breakdowns");
        Check(service.History().Length==0,"preview creates no analysis history or provider calls");
        var run=await service.Analyze(preview.Token,CancellationToken.None);Check(service.History().Single().Id==run.Id&&store.List().Single().NetPnl==95,"analysis history preserves input and result without changing trades");
        var rejected=false;try{await service.Analyze(preview.Token,CancellationToken.None);}catch(FormatException){rejected=true;}Check(rejected,"consumed analysis preview cannot be sent twice");
        Check(service.Preview(new("journal",IncludeNotes:true)).Snapshot.GetRawText().Contains("Private note"),"notes are only included when requested");
        rejected=false;try{service.Preview(new("journal",new(Symbol:"UNKNOWN")));}catch(FormatException){rejected=true;}Check(rejected,"empty trade ranges do not request an analysis");
        rejected=false;try{CodexAnalysisProvider.Validate("{}");}catch(FormatException){rejected=true;}Check(rejected,"invalid AI results are rejected");
        var signal=recommendation with{Decision=recommendation.Decision! with{Trend="UP"}};
        Check(CodexAnalysisProvider.Validate(JsonSerializer.Serialize(signal,CodexAnalysisProvider.Json)).Decision?.Trend=="UP","market signal retains a validated trend direction");
        rejected=false;try{CodexAnalysisProvider.Validate(JsonSerializer.Serialize(signal with{Decision=signal.Decision! with{Trend="GUARANTEED"}},CodexAnalysisProvider.Json));}catch(FormatException){rejected=true;}Check(rejected,"unknown trend labels cannot become market signals");
        Check(JsonSerializer.Serialize(AnalysisService.SampleMarket()).Contains("SYNTHETIC"),"sample market provenance is explicit");
        foreach(var kind in new[]{"journal","market","synthetic"})
        {
            var logs=new AnalysisWorkflowChecks.CapturedLog<CodexAnalysisProvider>();
            var snapshot=kind=="market"?DecisionSnapshot():JsonSerializer.SerializeToElement(new{kind});
            var invocation=new CodexAnalysisProvider("fixture-codex",runProcess:async(start,input,token)=>
            {
                var arguments=start.ArgumentList.ToArray();
                Check(arguments.Contains(kind=="market"?"web_search=\"live\"":"web_search=\"disabled\"")&&arguments.Contains("features.shell_tool=false")&&arguments.Contains("--ignore-user-config")&&arguments.Contains("--ignore-rules"),kind+" analysis enables only its intended research access while preserving CLI isolation");
                Check(input!.Contains(kind=="journal"?"Review only the selected closed trades":"Act as an aggressive, decisive intraday trade caller"),kind+" receives its own analysis prompt");
                var path=arguments[Array.IndexOf(arguments,"--output-last-message")+1];
                await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new AnalysisResult("Fixture","Review",["Data"],["Sample"],["Review"],"Scope"),CodexAnalysisProvider.Json),token);
                return (0,"model: fixture");
            },logger:logs);
            var runDirectory=Path.Combine(directory,"prompt-"+kind);Directory.CreateDirectory(runDirectory);
            await invocation.Analyze(snapshot,runDirectory,CancellationToken.None);
            Check(logs.Messages.Any(line=>line.Contains("Codex starting"))&&logs.Messages.Any(line=>line.Contains("exitCode=0")&&line.Contains("resultPresent=True"))&&logs.Messages.All(line=>!line.Contains("SNAPSHOT:")&&!line.Contains("Fixture review")),kind+" Codex diagnostics report process timing and exit without input or result contents");
        }
        var failureLog=new AnalysisWorkflowChecks.CapturedLog<CodexAnalysisProvider>();
        var brokenCodex=new CodexAnalysisProvider("fixture-codex",runProcess:(_,_,_)=>Task.FromResult((1,"401 unauthorized PRIVATE-TOKEN")),logger:failureLog);
        rejected=false;try{await brokenCodex.Analyze(JsonSerializer.SerializeToElement(new{kind="journal"}),directory,CancellationToken.None);}catch(FormatException){rejected=true;}
        Check(rejected&&failureLog.Messages.Any(line=>line.Contains("failureCategory=authentication"))&&failureLog.Messages.All(line=>!line.Contains("PRIVATE-TOKEN")),"Codex failures log a safe category without raw subprocess output");
        Check(run.PromptVersion=="journal-review-v2"&&AnalysisPrompts.For(DecisionSnapshot()).Version=="market-signal-v8"&&AnalysisPrompts.For(DecisionSnapshot()).TimeLimit==TimeSpan.FromSeconds(90),"saved results identify their separate prompt version and market runs have a 90-second execution limit");
        if(live){var provider=new CodexAnalysisProvider();Console.WriteLine(JsonSerializer.Serialize(await provider.Status()));var liveService=new AnalysisService(store,provider,Path.Combine(directory,"live"));var sample=liveService.Preview(new("sample-market"));var response=await liveService.Analyze(sample.Token,CancellationToken.None);Check(response.Result.Limitations.Length>0,"actual Codex ChatGPT analysis returns validated structured output");Console.WriteLine("Synthetic live test saved in "+directory);}
    }
    private sealed class FixtureProvider:IAnalysisProvider
    {
        public Task<AnalysisResult> Analyze(JsonElement snapshot,string directory,CancellationToken token,AnalysisPrompt? prompt=null)=>Task.FromResult(new AnalysisResult("Fixture review","Retrospective summary",["Net 95","Fees 5"],["One trade","No market path"],["Record consistent risk"],"Retrospective review only"));
    }
}
