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
        store.Save(new(){Id="fixture",Symbol="MES",Date="2026-10-06",Time="10:00",AccountingGrossPnl=100,Fees=5,Risk=50,Notes="Private note",Setup="Pullback"});
        var service=new AnalysisService(store,new FixtureProvider(),Path.Combine(directory,"analysis"));
        var preview=service.Preview(new("journal",new(Symbol:"MES")));Check(!preview.Snapshot.GetRawText().Contains("Private note")&&preview.Snapshot.GetProperty("summary").GetProperty("net").GetDecimal()==95,"journal snapshot uses filtered accounting and excludes notes by default");
        Check(service.History().Length==0,"preview creates no analysis history or provider calls");
        var run=await service.Analyze(preview.Token,CancellationToken.None);Check(service.History().Single().Id==run.Id&&store.List().Single().NetPnl==95,"analysis history preserves input and result without changing trades");
        var rejected=false;try{await service.Analyze(preview.Token,CancellationToken.None);}catch(FormatException){rejected=true;}Check(rejected,"consumed analysis preview cannot be sent twice");
        Check(service.Preview(new("journal",IncludeNotes:true)).Snapshot.GetRawText().Contains("Private note"),"notes are only included when requested");
        rejected=false;try{service.Preview(new("journal",new(Symbol:"UNKNOWN")));}catch(FormatException){rejected=true;}Check(rejected,"empty trade ranges do not request an analysis");
        rejected=false;try{CodexAnalysisProvider.Validate("{}");}catch(FormatException){rejected=true;}Check(rejected,"invalid AI results are rejected");
        Check(JsonSerializer.Serialize(AnalysisService.SampleMarket()).Contains("SYNTHETIC"),"sample market provenance is explicit");
        if(live){var provider=new CodexAnalysisProvider();Console.WriteLine(JsonSerializer.Serialize(await provider.Status()));var liveService=new AnalysisService(store,provider,Path.Combine(directory,"live"));var sample=liveService.Preview(new("sample-market"));var response=await liveService.Analyze(sample.Token,CancellationToken.None);Check(response.Result.Limitations.Length>0,"actual Codex ChatGPT analysis returns validated structured output");Console.WriteLine("Synthetic live test saved in "+directory);}
    }
    private sealed class FixtureProvider:IAnalysisProvider
    {
        public Task<AnalysisResult> Analyze(JsonElement snapshot,string directory,CancellationToken token)=>Task.FromResult(new AnalysisResult("Fixture review","Retrospective summary",["Net 95","Fees 5"],["One trade","No market path"],["Record consistent risk"],"Retrospective review only"));
    }
}
