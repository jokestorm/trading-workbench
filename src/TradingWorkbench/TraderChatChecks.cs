using System.Text.Json;

namespace TradingWorkbench;

public static class TraderChatChecks
{
    private static void Check(bool result,string message){if(!result)throw new Exception(message);Console.WriteLine("PASS "+message);}
    public static async Task Run()
    {
        await Priority();
        var directory=Path.Combine(Path.GetTempPath(),"trader-chat-check-"+Guid.NewGuid().ToString("N"));
        try
        {
            var fake=new FakeAgent();var tools=0;
            Task<object> Tool(string name,JsonElement args,CancellationToken token){tools++;return Task.FromResult<object>(new{source="Schwab transactions",fees=12.34m});}
            (string?,string?,object) Context(string? symbol)=>(symbol,symbol is null?null:"scope-a",new{asOf=DateTimeOffset.UtcNow});
            await using(var chat=new TraderChat(directory,fake,Tool,Context))
            {
                var request=new TraderChatRequest(Guid.NewGuid().ToString(),"Why this trade?","/MESZ26");
                chat.Send(request);await fake.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check(chat.Current().Busy&&chat.Send(request).Messages.Count(m=>m.Role=="user")==1&&fake.Calls==1,"chat admission is durable and duplicate client retries cannot spend another inference call");
                var rejected=false;try{chat.Send(request with{ClientId=Guid.NewGuid().ToString()});}catch(FormatException){rejected=true;}
                Check(rejected&&chat.Current().Messages.Length==1,"concurrent messages cannot start competing turns or overwrite a running reply");
                fake.Release.TrySetResult();await Finished(chat);
                var state=chat.Current();
                Check(state is {Busy:false,ThreadId:"fixture-thread",Error:null}&&state.Messages.Last().Text=="Verified fees: 12.34"&&tools==1,"chat reads its data tool and persists the streamed final reply and conversation identity");
                Check(chat.Discussion("/MESZ26","scope-a").Length==2&&chat.Discussion("/MESZ26","scope-b").Length==0&&chat.Discussion("/MCLZ26","scope-a").Length==0,"caller discussion memory stays within the selected dated contract and trade scope");
                var reads=fake.Calls;chat.Current();chat.Current();Check(fake.Calls==reads,"chat status polling never starts model inference");
            }
            var resumed=new FakeAgent();
            await using(var chat=new TraderChat(directory,resumed,Tool,Context))
            {
                Check(chat.Current().Messages.Length==2,"chat transcript survives an application restart");
                chat.Send(new(Guid.NewGuid().ToString(),"Continue"));await resumed.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check(resumed.Thread=="fixture-thread","follow-up messages resume the existing Codex conversation after restart");
                chat.Stop();await Finished(chat);
                Check(!chat.Current().Busy&&chat.Current().Error!.Contains("stopped")&&chat.Current().Messages.Count(m=>m.Role=="user")==2,"stopping a reply preserves prior discussion and releases admission");
            }
            var statePath=Path.Combine(directory,"conversation.json");var interrupted=JsonSerializer.Deserialize<TraderChatState>(File.ReadAllText(statePath),CodexAnalysisProvider.Json)!;
            File.WriteAllText(statePath,JsonSerializer.Serialize(interrupted with{Busy=true},CodexAnalysisProvider.Json));
            await using(var chat=new TraderChat(directory,new FakeAgent(),Tool,Context))Check(!chat.Current().Busy&&chat.Current().Error!.Contains("restarted"),"crash recovery retains conversation history without silently resending a paid turn");
        }
        finally{Directory.Delete(directory,true);}
    }
    private static async Task Priority()
    {
        var directory=Path.Combine(Path.GetTempPath(),"trader-priority-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var calls=0;
        var provider=new CodexAnalysisProvider("fixture",runProcess:async(start,_,token)=>
        {
            calls++;
            if(calls==1){entered.TrySetResult();await Task.Delay(Timeout.Infinite,token);}
            var arguments=start.ArgumentList.ToArray();var answer=new AnalysisResult("Fixture","Saved",["Observed"],["Fixture"],["Review"],"Fixture");
            await File.WriteAllTextAsync(arguments[Array.IndexOf(arguments,"--output-last-message")+1],JsonSerializer.Serialize(answer,CodexAnalysisProvider.Json),token);return(0,"model: fixture");
        });
        var snapshot=JsonSerializer.SerializeToElement(new{kind="saved-market",position=new{state="FLAT"},monitorContext=new{}});
        var monitor=provider.Analyze(snapshot,directory,stop.Token);
        try
        {
            await entered.Task.WaitAsync(stop.Token);
            using(var chat=await provider.EnterConversation(stop.Token))
            {
                var yielded=false;try{await monitor;}catch(ConversationPriorityException){yielded=true;}
                Check(yielded&&provider.ConversationActive&&calls==1,"Trader chat preempts active background inference and owns the shared AI slot without overlapping processes");
                var deferred=false;try{await provider.Analyze(snapshot,directory,stop.Token);}catch(ConversationPriorityException){deferred=true;}
                var brief=false;try{await provider.AnalyzeBrief(JsonSerializer.SerializeToElement(new{kind="journal"}),directory,stop.Token,new("Fixture","fixture",false,TimeSpan.FromSeconds(30)));}catch(BriefDeferredException){brief=true;}
                Check(deferred&&brief&&calls==1,"background caller and brief cannot queue model work ahead of an active conversation");
            }
            await provider.Analyze(JsonSerializer.SerializeToElement(new{kind="journal"}),directory,stop.Token);
            Check(!provider.ConversationActive&&calls==2,"releasing conversation priority restores normal inference admission");
        }
        finally{stop.Cancel();try{await monitor;}catch(OperationCanceledException){}Directory.Delete(directory,true);}
    }
    private static async Task Finished(TraderChat chat)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));while(chat.Current().Busy)await Task.Delay(5,timeout.Token);
    }
    private sealed class FakeAgent:ITraderAgent
    {
        public int Calls;public string? Thread;
        public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task Reply(string? thread,string input,Action<string,JsonElement> receive,Func<string,JsonElement,CancellationToken,Task<object>> tool,CancellationToken token)
        {
            Calls++;Thread=thread;Entered.TrySetResult();await Release.Task.WaitAsync(token);
            receive("session",JsonSerializer.SerializeToElement(new{threadId="fixture-thread",model="fixture-model"}));
            await tool("account_history",JsonSerializer.SerializeToElement(new{}),token);
            receive("item/agentMessage/delta",JsonSerializer.SerializeToElement(new{itemId="answer",delta="Verified fees: "}));
            receive("item/agentMessage/delta",JsonSerializer.SerializeToElement(new{itemId="answer",delta="12.34"}));
            receive("item/completed",JsonSerializer.SerializeToElement(new{item=new{type="agentMessage",id="answer",text="Verified fees: 12.34"}}));
        }
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
