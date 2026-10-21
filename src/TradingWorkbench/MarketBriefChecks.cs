using System.Text.Json;
namespace TradingWorkbench;

public static class MarketBriefChecks
{
    public static async Task Run()
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
        DateTimeOffset At(string value)=>DateTimeOffset.Parse(value);
        var morning=At("2026-10-21T12:00:00Z"); // 8 AM EDT
        Check(MarketBriefService.Slot(morning)=="2026-10-21-08"&&MarketBriefService.Slot(morning.AddHours(7))=="2026-10-21-15"&&MarketBriefService.Slot(morning.AddHours(8)) is null&&MarketBriefService.Slot(morning.AddSeconds(-1)) is null,"market brief schedule includes 8 through 3 Eastern and excludes off-hours");
        Check(MarketBriefService.Slot(At("2026-12-21T13:00:00Z"))=="2026-12-21-08"&&MarketBriefService.Slot(At("2026-10-24T12:00:00Z")) is null,"market brief schedule follows Eastern daylight saving time and skips weekends");
        var directory=Path.Combine(Path.GetTempPath(),"brief-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var content=new BriefContent("Mixed",["Equities mixed.","Rates steady.","Energy softer."],"No major upcoming catalyst verified.");
            var calls=0;var fail=false;var busy=false;
            var service=new MarketBriefService(directory,(previous,now,path,token)=>{calls++;if(fail)throw new FormatException("PRIVATE fixture error");return Task.FromResult(content);},()=>busy);
            await service.Tick(morning,CancellationToken.None);await service.Tick(morning.AddMinutes(40),CancellationToken.None);
            Check(calls==1&&service.Current().LastChecked==morning&&service.Current().LastChanged==morning,"market brief admits only one check per current hour without repeated inference");
            await service.Tick(morning.AddHours(1),CancellationToken.None);
            Check(service.Current().LastChecked==morning.AddHours(1)&&service.Current().LastChanged==morning&&Directory.GetFiles(directory,"revision-*.json").Length==1,"unchanged brief preserves its words and last-changed timestamp without extra revisions");
            content=content with{Points=["Equities higher.","Rates steady.","Energy softer."]};
            await service.Tick(morning.AddHours(2),CancellationToken.None);
            Check(service.Current().Content!.Points[1]=="Rates steady."&&service.Current().LastChanged==morning.AddHours(2)&&Directory.GetFiles(directory,"revision-*.json").Length==2,"changed brief updates the living content and retains prior versions internally");
            fail=true;await service.Tick(morning.AddHours(3),CancellationToken.None);
            Check(service.Current().Status=="failed"&&service.Current().LastChecked==morning.AddHours(2)&&service.Current().Content==content&&!service.Current().Message.Contains("PRIVATE"),"failed research preserves the prior brief and truthful successful-check timestamps");
            var reopened=new MarketBriefService(directory,(_,_,_,_)=>throw new Exception("Must not replay an attempted hour"));
            await reopened.Tick(morning.AddHours(3).AddMinutes(10),CancellationToken.None);
            Check(reopened.Current().Content!.Outlook==content.Outlook&&reopened.Current().Status=="failed","brief and attempted-hour deduplication survive app restarts");
            fail=false;busy=true;await service.Tick(morning.AddHours(4),CancellationToken.None);var beforeCalls=calls;
            busy=false;await service.Tick(morning.AddHours(4).AddMinutes(1),CancellationToken.None);
            Check(calls==beforeCalls+1&&service.Current().Status=="ready","manual analysis defers a scheduled brief without consuming its hourly check");
            var catchupCalls=0;
            var catchup=new MarketBriefService(Path.Combine(directory,"catchup"),(_,_,_,_)=>{catchupCalls++;return Task.FromResult(content);});
            await catchup.Tick(morning.AddHours(6).AddMinutes(22),CancellationToken.None);
            Check(catchupCalls==1&&catchup.Current().Slot=="2026-10-21-14","starting midday performs one current-hour catch-up without replaying missed hours");
            var persisted=service.Current() with{Status="checking",Slot="2026-10-21-13"};
            File.WriteAllText(Path.Combine(directory,"current.json"),JsonSerializer.Serialize(persisted,CodexAnalysisProvider.Json));
            var interrupted=new MarketBriefService(directory,(_,_,_,_)=>throw new Exception());
            Check(interrupted.Current().Status=="interrupted"&&interrupted.Current().Content is not null,"restart during research marks the check interrupted while retaining its saved brief");
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var invocations=0;
            var provider=new CodexAnalysisProvider("fixture",runProcess:async(start,input,token)=>
            {
                invocations++;
                if(invocations==1){entered.SetResult();await Task.Delay(Timeout.Infinite,token);}
                var arguments=start.ArgumentList.ToArray();
                var result=new AnalysisResult("Mixed","Verified cutoff",content.Points,["Observed context"],[content.NextEvent],"Changes in news");
                await File.WriteAllTextAsync(arguments[Array.IndexOf(arguments,"--output-last-message")+1],JsonSerializer.Serialize(result,CodexAnalysisProvider.Json),token);
                Check(arguments.Contains("features.shell_tool=false")&&arguments.Contains("--ignore-rules"),"brief and analysis preserve restricted Codex CLI execution");
                return(0,"model: fixture");
            });
            var briefDir=Path.Combine(directory,"priority-brief");var manualDir=Path.Combine(directory,"priority-manual");Directory.CreateDirectory(briefDir);Directory.CreateDirectory(manualDir);
            var snapshot=JsonSerializer.SerializeToElement(new{kind="journal"});
            var brief=provider.AnalyzeBrief(snapshot,briefDir,CancellationToken.None,new("Brief fixture","fixture",true,TimeSpan.FromSeconds(120)));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var manual=provider.Analyze(snapshot,manualDir,CancellationToken.None);
            var canceled=false;try{await brief;}catch(OperationCanceledException){canceled=true;}
            await manual.WaitAsync(TimeSpan.FromSeconds(5));
            Check(canceled&&invocations==2,"manual analysis cancels lower-priority brief research and acquires the shared Codex connection");
            using(var lease=provider.EnterOperation())
            {
                var deferred=false;try{await provider.AnalyzeBrief(snapshot,briefDir,CancellationToken.None,new("fixture","fixture",true,TimeSpan.FromSeconds(120)));}catch(BriefDeferredException){deferred=true;}
                Check(deferred,"scheduled brief cannot overlap sign-in or active analysis");
            }
            var researched=new CodexAnalysisProvider("fixture",runProcess:async(start,input,token)=>
            {
                var arguments=start.ArgumentList.ToArray();
                Check(arguments.Contains("web_search=\"live\"")&&input!.Contains("previous")&&input.Contains("Preserve still-relevant bullets verbatim")&&!input.Contains("account balance"),"brief research uses live search and the previous broad-market brief without account context");
                var result=new AnalysisResult("RESEARCH_UNAVAILABLE","No fresh verification",["Unavailable"],["Unavailable"],["Unavailable"],"Unavailable");
                await File.WriteAllTextAsync(arguments[Array.IndexOf(arguments,"--output-last-message")+1],JsonSerializer.Serialize(result,CodexAnalysisProvider.Json),token);return(0,"");
            });
            var rejected=false;try{await MarketBriefService.Research(researched,service.Current(),morning,manualDir,CancellationToken.None);}catch(FormatException){rejected=true;}
            Check(rejected,"unavailable research never becomes a freshly checked market brief");
        }
        finally{Directory.Delete(directory,true);}
    }
}
