using System.Text.Json;

namespace TradingWorkbench;

public static class PromptSettingsChecks
{
    private sealed class CaptureProvider:IAnalysisProvider
    {
        public string? Text;
        public Task<AnalysisResult> Analyze(JsonElement snapshot,string directory,CancellationToken token,AnalysisPrompt? prompt=null)
        {
            Text=prompt!.Text;
            return Task.FromResult(new AnalysisResult("WAIT","Fixture",["Data"],["Fixture"],["Watch"],"Changed prices",new("WAIT","Fixture",null,null,null)));
        }
    }
    public static async Task Run()
    {
        void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS "+message);}
        var directory=Path.Combine(Path.GetTempPath(),"prompt-settings-"+Guid.NewGuid().ToString("N"));var version=1;
        PromptDefinition Default(string kind)=>new(kind,$"Default {kind} version {version}",$"{kind}-v{version}");
        var settings=new PromptSettings(Path.Combine(directory,"settings"),Default);
        var initial=settings.Get("journal");Check(settings.Reset("journal",initial.Revision).ActiveSource=="default","use latest default works before any custom prompt has been saved");
        var market=settings.Get("market");var saved=settings.Save("market",new("My custom instructions",market.DefaultVersion,market.Revision));
        var reopened=new PromptSettings(Path.Combine(directory,"settings"),Default);
        Check(reopened.Get("market").CustomText==saved.CustomText&&reopened.Get("journal").CustomText is null,"custom prompts persist across restart independently for market and journal");
        version=2;
        var updated=reopened.Get("market");
        Check(updated.UpdateAvailable&&updated.DefaultVersion=="market-v2"&&updated.CustomText=="My custom instructions"&&reopened.Get("journal").DefaultVersion=="journal-v2","app updates replace defaults without overwriting custom prompts");
        bool Rejected(Action action){try{action();return false;}catch(FormatException){return true;}}
        Check(Rejected(()=>settings.Save("market",new("stale edit",market.DefaultVersion,market.Revision))),"stale editors cannot overwrite newer saved prompt settings");
        Check(Rejected(()=>settings.Save("market",new(" ",updated.DefaultVersion,updated.Revision)))&&Rejected(()=>settings.Get("../outside")),"blank prompts and invalid prompt paths are rejected");
        var adopted=settings.Save("market",new(updated.CustomText!,updated.DefaultVersion,updated.Revision));
        Check(!adopted.UpdateAvailable&&adopted.BasedOnVersion=="market-v2","reviewing a new default records the chosen baseline for the custom prompt");

        var store=new TradeStore(directory);var prices=new MarketStore(directory);var provider=new CaptureProvider();
        var analysis=new AnalysisService(store,provider,Path.Combine(directory,"analysis"),prices,settings);
        var refreshing=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var workflow=new AnalysisWorkflow(analysis,Path.Combine(directory,"jobs"),async(request,token)=>{
            refreshing.SetResult();await release.Task.WaitAsync(token);var now=DateTimeOffset.UtcNow;
            prices.SaveBars(Enumerable.Range(0,6).Select(i=>new PriceBar(request.Symbol!,now.AddMinutes(i-6),100,101,99,100,10)).ToArray());
            return new(request.Symbol!,"FUTURE",100,null,null,null,null,now,true,now);
        });
        var job=workflow.Start(new("saved-market",Symbol:"/MESZ26",Days:1,Position:new("FLAT")));await refreshing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        settings.Save("market",new("Changed after Analyze",adopted.DefaultVersion,adopted.Revision));release.SetResult();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));while(workflow.Current()!.Active)await Task.Delay(10,timeout.Token);
        var run=analysis.Find(job.Id);
        Check(run.PromptText==provider.Text&&run.PromptText!.StartsWith("My custom instructions\nSNAPSHOT:\n")&&!run.PromptText.Contains("Changed after Analyze")&&run.PromptSource=="custom"&&run.PromptBaseVersion=="market-v2","analysis freezes the selected prompt at admission and saves the exact provider input");
        var current=settings.Get("market");var reset=settings.Reset("market",current.Revision);
        Check(reset.CustomText is null&&reset.ActiveSource=="default"&&settings.Resolve("market").Text==Default("market").Text,"use latest default removes the override and follows future app updates");
        version=3;Check(settings.Resolve("market").Version=="market-v3","default users automatically receive the next app prompt");
        var legacyJson=JsonSerializer.Serialize(run,CodexAnalysisProvider.Json);var node=System.Text.Json.Nodes.JsonNode.Parse(legacyJson)!.AsObject();node.Remove("promptText");node.Remove("promptSource");node.Remove("promptBaseVersion");
        Check(JsonSerializer.Deserialize<AnalysisRun>(node.ToJsonString(),CodexAnalysisProvider.Json)!.PromptText is null,"older analysis history remains readable without exact prompt metadata");
    }
}
