using System.Text.Json;

namespace TradingWorkbench;

public static class ContinuousMonitorChecks
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
    private static bool Reject(Action work){try{work();return false;}catch(FormatException){return true;}}
    private sealed class Harness
    {
        public string Directory {get;}=Path.Combine(Path.GetTempPath(),"continuous-monitor-"+Guid.NewGuid().ToString("N"));
        public DateTimeOffset Now=new(2026,10,24,16,0,10,TimeSpan.Zero);
        public decimal Price=100;
        public bool Busy,FailGold,HistoryFailure,ChatBusy;
        public int Starts,Reads;
        public AnalysisRequest? Request;
        public Func<AnalysisRun,AnalysisRun>? Guard;
        public AnalysisJob? Job;
        public AnalysisRun? Saved;
        public BrokerTransactionReport? Transactions;
        public ContinuousMonitor Service;
        public Harness(){Service=Create();}
        public ContinuousMonitor Create()=>new(Directory,(spec,_,_)=>
        {
            Reads++;
            if(FailGold&&spec.Symbol=="/MGCZ26")throw new HttpRequestException("PRIVATE RESPONSE");
            var end=new DateTimeOffset(Now.Year,Now.Month,Now.Day,Now.Hour,Now.Minute,0,TimeSpan.Zero);
            var bars=Enumerable.Range(0,35).Select(i=>new PriceBar(spec.Symbol,end.AddMinutes(i-35),100,100.25m,99.75m,100,100)).ToArray();
            return Task.FromResult(new MonitorObservation(Quote(spec.Symbol),bars,HistoryFailure?"History unavailable":null));
        },(request,guard)=>
        {
            if(Busy||Job is {Active:true})return null;
            Starts++;Request=request;Guard=guard;Job=new(Guid.NewGuid().ToString("N"),"saved-market",request.Symbol,"analyzing","Analyzing",Now,Now,Origin:"monitor");return Job;
        },id=>Job?.Id==id?Job:null,_=>Saved!,id=>{if(Job?.Id==id)Job=Job with{State="canceled"};},()=>Now,transactions:()=>Transactions??new("waiting","Fixture has no broker connection"),aiAvailable:()=>!ChatBusy);
        public MarketQuote Quote(string symbol)=>new(symbol,"FUTURE",Price,Price-.25m,Price+.25m,null,null,Now,true,Now);
        public void Configure(params MonitorInstrument[] specs)
        {
            var s=Service.Current().Settings;
            Service.Configure(new(s.Revision,true,specs.Length>0?specs:[new("/MESZ26",5,.25m)],15,30,300));
        }
        public void Confirm(string side="FLAT",decimal? stop=null,decimal? target=null,DateTimeOffset? expires=null,string symbol="/MESZ26")
        {
            var p=Service.Current().Markets.Single(m=>m.Symbol==symbol).Exposure;
            Service.Confirm(new(symbol,p.Version,new(side,side is "LONG" or "SHORT"?1:null,side is "LONG" or "SHORT"?100:null),stop,target,expires));
        }
        public void Complete(string action="BUY",string? preferredSymbol=null)
        {
            var snapshot=JsonSerializer.SerializeToElement(new{monitorContext=Request!.MonitorContext},CodexAnalysisProvider.Json);
            var answer=new AnalysisResult(action,"Fixture product comparison",["Observed"],["Synthetic"],["Review"],"Below 99",new(action,"Fixture decision",100,99,102),preferredSymbol);
            Saved=Guard!(new(Job!.Id,"saved-market",Now,"Fixture","Fixture","monitor-fixture",snapshot,answer));
            Job=Job with{State="completed",ResultId=Job.Id};
        }
        public void Advance(int seconds)=>Now=Now.AddSeconds(seconds);
        public Task Tick()=>Service.Tick(CancellationToken.None);
    }
    public static async Task Run()
    {
        var coalesced=new Harness();coalesced.Configure(new("/MESZ26",5,.25m),new("/MNQZ26",2,.25m));coalesced.Confirm();coalesced.Confirm(symbol:"/MNQZ26");
        await coalesced.Tick();coalesced.Complete("WAIT");await coalesced.Tick();
        Check(coalesced.Starts==1,"one cross-product opportunity review does not immediately launch routine reviews for every other flat contract");
        coalesced.Advance(301);await coalesced.Tick();Check(coalesced.Starts==2,"coalesced reviews resume on the existing review interval");
        var chatting=new Harness{ChatBusy=true};chatting.Configure();chatting.Confirm("LONG",99,102);chatting.Price=98.75m;await chatting.Tick();
        Check(chatting.Reads>0&&chatting.Starts==0&&chatting.Service.Current().Markets.Single().Exposure.ExitRequired,"chat priority leaves quote refresh and deterministic stop exits running without background inference");
        var resume=new Harness{ChatBusy=true};resume.Configure();resume.Confirm();await resume.Tick();resume.ChatBusy=false;await resume.Tick();
        Check(resume.Starts==1,"fresh opportunity review resumes when Trader chat releases AI priority");
        var h=new Harness();h.Configure();await h.Tick();
        Check(h.Reads==1&&h.Starts==0,"continuous scanner observes unknown positions without issuing entry calls");
        h.Confirm();await h.Tick();
        Check(h.Starts==1&&h.Request?.MonitorContext?.Trigger=="Periodic opportunity review"&&h.Now.DayOfWeek==DayOfWeek.Saturday,"programmatic review wakes AI on any day without a profit target or session cutoff");
        Check(h.Request!.MonitorContext!.RecentMemory.Single().Kind=="position-confirmed","monitor analysis receives explicit scoped position context");
        h.Complete();await h.Tick();
        Check(h.Service.Current().Markets.Single().CurrentDecision?.Action=="BUY"&&h.Service.Current().Memory.Any(e=>e.Kind=="analysis"),"validated calls and scoped analysis memory survive independent status polling");
        Check(h.Service.Current().Markets.Single().AwaitingExecution,"an entry recommendation requires fill or flat reconciliation before another entry");
        h.Advance(61);Check(h.Service.Current().Markets.Single().CurrentDecision?.Action=="WAIT","saved recommendations stop being actionable after quote/call expiry");await h.Tick();
        h.Confirm();
        Check(h.Starts==1,"unchanged bars and prices do not spend another AI request before periodic review");
        h.Price=101;h.Advance(15);await h.Tick();
        Check(h.Starts==2&&h.Request.MonitorContext!.Trigger.Contains("ATR")&&h.Request.MonitorContext.RecentMemory.Any(e=>e.Decision?.Action=="BUY"),"material ATR displacement wakes AI with its previous decisions");
        h.Confirm("LONG",99,102);h.Complete();await h.Tick();
        Check(h.Saved?.Result.Decision?.GuardCode=="monitor-context-changed","position changes during inference invalidate pending entries before saving");
        Check(Reject(()=>h.Confirm("LONG",98))&&Reject(()=>h.Service.Configure(new(h.Service.Current().Settings.Revision,true,[]))),"committed stops cannot widen and open contracts cannot disappear from monitoring");

        var protection=new Harness();protection.Configure();protection.Confirm("LONG",99,102);await protection.Tick();
        Check(protection.Starts==1,"open exposure requests a thesis review");
        protection.Price=98.75m;protection.Advance(15);protection.HistoryFailure=true;await protection.Tick();
        Check(protection.Service.Current().Markets.Single().Exposure.ExitRequired&&protection.Service.Current().Markets.Single().CurrentDecision?.Action=="SELL_CLOSE","stop breach produces an exit while inference runs and history refresh fails");
        protection.Price=101;protection.Advance(15);await protection.Tick();protection.Complete("HOLD");await protection.Tick();
        Check(protection.Saved?.Result.Decision is {Action:"SELL_CLOSE",GuardCode:"monitor-exit-required"}&&protection.Starts==1,"recovered price and a late HOLD cannot clear a latched exit or start a new entry");
        var reopened=protection.Create();
        Check(reopened.Current().Markets.Single().Exposure is {ExitRequired:true,ConfirmedAt:null},"restart preserves exit requirements and demands a fresh broker exposure check");
        protection.Confirm("FLAT");Check(!protection.Service.Current().Markets.Single().Exposure.ExitRequired,"explicit flat confirmation clears the exit latch");

        var shortTrade=new Harness();shortTrade.Configure();shortTrade.Confirm("SHORT",101,98);shortTrade.Price=97.75m;await shortTrade.Tick();
        Check(shortTrade.Service.Current().Markets.Single().CurrentDecision?.Action=="BUY_COVER","short target checks use the ask and produce a cover recommendation");
        var expiry=new Harness();expiry.Configure();expiry.Confirm("LONG",99,expires:expiry.Now.AddSeconds(20));
        Check(Reject(()=>expiry.Confirm("LONG",99,expires:expiry.Now.AddMinutes(5))),"committed trade expiry cannot be extended to rescue a position");
        expiry.Advance(21);await expiry.Tick();Check(expiry.Service.Current().Markets.Single().Exposure.ExitReason=="Committed trade time expired","time expiry triggers an exit without model inference");

        var multi=new Harness{FailGold=true};multi.Configure(new("/MGCZ26",10,.1m),new("/MESZ26",5,.25m));multi.Confirm();await multi.Tick();
        Check(multi.Reads==2&&multi.Starts==1&&multi.Service.Current().Markets.Single(m=>m.Symbol=="/MGCZ26").Error is not null,"one failed futures market does not stop scanning another");
        var busy=new Harness{Busy=true};busy.Configure();busy.Confirm();await busy.Tick();
        Check(busy.Starts==0&&busy.Service.Current().Markets.Single().LastAttempt is null,"busy provider admission does not consume the opportunity or build a backlog");
        busy.Busy=false;await busy.Tick();Check(busy.Starts==1,"latest opportunity retries when shared analysis becomes idle");
        var settings=busy.Service.Current().Settings;busy.Service.Configure(new(settings.Revision,false,settings.Instruments));busy.Complete();
        Check(busy.Saved?.Result.Decision?.GuardCode=="monitor-context-changed"&&Reject(()=>busy.Service.Configure(new(settings.Revision,true,settings.Instruments))),"stopping cancels owned analysis and stale settings cannot reactivate it");
        var drift=new Harness();drift.Configure();drift.Confirm();await drift.Tick();drift.Price=105;drift.Advance(15);await drift.Tick();drift.Complete();
        Check(drift.Saved?.Result.Decision?.GuardCode=="monitor-entry-drift","entry validation rejects price movement beyond the proposed setup");
        var ongoing=new Harness();ongoing.Configure();ongoing.Confirm();ongoing.Advance(3600);await ongoing.Tick();
        Check(ongoing.Starts==1&&ongoing.Reads==1,"continuous calls have no arbitrary confirmation timer or daily cutoff");
        Check(ContinuousMonitor.Validate(new("/ZBZ26",1000,1m/32)).Symbol=="/ZBZ26"&&Reject(()=>ContinuousMonitor.Validate(new("/MES",5,.25m)))&&Reject(()=>ContinuousMonitor.Validate(new("/MESZ26",5,0))),"arbitrary dated futures require explicit specifications instead of guessed root metadata");
        var quote=drift.Quote("/MESZ26");
        Check(!ContinuousMonitor.Fresh(quote with{Realtime=false},quote.Symbol,drift.Now)&&!ContinuousMonitor.Fresh(quote with{QuoteTime=drift.Now.AddMinutes(1)},quote.Symbol,drift.Now)&&!ContinuousMonitor.Fresh(quote,quote.Symbol,drift.Now.AddSeconds(31)),"delayed, future-dated and stale quotes cannot authorize calls");
        var following=new Harness();following.Configure();following.Confirm();await following.Tick();following.Complete();await following.Tick();
        var fills=new List<BrokerFill>();
        void AddFill(string id,int quantity,decimal price,string effect="OPENING")
        {
            following.Advance(1);fills.Add(new(id,id,"/MESZ26",following.Now,quantity,price,effect));
            following.Transactions=new("ready","Fixture transaction fills",following.Now,following.Now,new(following.Now.AddDays(-59),following.Now,[new("fixture","Account 1",fills.ToArray())]));
        }
        AddFill("opening",2,100);await following.Tick();
        var exposure=following.Service.Current().Markets.Single().Exposure;
        Check(exposure is {Source:"transactions",Stop:99,Target:102}&&exposure.Position is {State:"LONG",Quantity:2,AverageEntry:100}&&!following.Service.Current().Markets.Single().AwaitingExecution,"order fills automatically reconcile an entry and carry its trade levels into the caller");
        Check(following.Request?.Position is {State:"LONG",Quantity:2}&&following.Request.MonitorContext!.RecentMemory.Any(e=>e.Kind=="broker-fill"),"the next AI request receives the filled position and execution memory without manual fill entry");
        AddFill("partial-exit",-1,101,"CLOSING");await following.Tick();await following.Tick();
        Check(following.Service.Current().Markets.Single().Exposure.Position is {State:"LONG",Quantity:1,AverageEntry:100},"partial exits update quantity once while preserving the average entry");
        following.Complete("HOLD");Check(following.Saved?.Result.Decision?.GuardCode=="monitor-context-changed","a newly arrived fill invalidates an in-flight position recommendation");
        AddFill("closing",-1,102,"CLOSING");await following.Tick();
        Check(following.Service.Current().Markets.Single().Exposure.Position.State=="FLAT"&&following.Service.Current().Memory.Count(e=>e.Kind=="broker-fill")==3,"closing order executions return the caller to flat without duplicate fill memory");
        AddFill("new-long",1,100);AddFill("reverse-close",-1,99,"CLOSING");AddFill("reverse-open",-1,99);await following.Tick();
        Check(following.Service.Current().Markets.Single().Exposure.Position is {State:"SHORT",Quantity:1,AverageEntry:99},"execution replay follows an actual reversal into its new trade scope");
        following.Transactions=following.Transactions! with{Status="failed"};
        Check(following.Service.Current().Markets.Single().CurrentDecision?.Action=="WAIT","failed execution refresh is visible instead of claiming a current broker-followed call");
        var products=new Harness();
        products.Transactions=new("ready","Fixture transactions",products.Now,products.Now,new(products.Now.AddDays(-59),products.Now,[new("fixture","Account 1",[])]));
        products.Service.ApplyUniverse(new(products.Now,[new("/MESZ26",5,.25m),new("/MNQZ26",2,.25m)],[],"Discovered two equity products"));
        Check(products.Service.Current().Markets.All(m=>m.Exposure.Position.State=="FLAT"&&m.Exposure.PositionBasis=="transaction-window"),"automatic products initialize their explicitly labeled transaction-window positions without contract entry");
        var automatic=products.Service.Current().Settings;products.Service.Configure(new(automatic.Revision,true,automatic.Instruments,15,30,300));await products.Tick();
        Check(products.Request?.MonitorContext?.Opportunities is {Length:2}&&products.Request.MonitorContext.Opportunities.All(o=>o.Ready),"AI receives ready cross-product spread, volume, volatility and setup comparisons");
        products.Complete("WAIT","/MNQZ26");await products.Tick();
        Check(products.Saved?.Result.Decision?.GuardCode=="monitor-product-selection"&&products.Request?.Symbol=="/MNQZ26"&&products.Service.Current().LastProductChoice?.Symbol=="/MNQZ26","AI chooses another product and the caller analyzes that contract before issuing an entry");
        products.Complete("BUY","/BOGUSZ26");
        Check(products.Saved?.Result.Decision?.GuardCode=="monitor-product-unavailable","AI cannot choose an unavailable contract or borrow its prices for another market");
        var rollover=new Harness();rollover.Configure();rollover.Confirm("LONG",99);var scope=rollover.Service.Current().Markets[0].Exposure.ScopeId;
        rollover.Service.ApplyUniverse(new(rollover.Now,[new("/MESH27",5,.25m)],[],"Rolled active contract"));
        Check(rollover.Service.Current().Settings.Instruments.Select(s=>s.Symbol).Order().SequenceEqual(new[]{"/MESH27","/MESZ26"}.Order())&&rollover.Service.Current().Markets.Single(m=>m.Symbol=="/MESZ26").Exposure.ScopeId==scope,"automatic rollover retains the actual held expiry and its trade scope");
        var legacy=new Harness();legacy.Configure(new MonitorInstrument("/CLZ26",1000,.01m));legacy.Confirm(symbol:"/CLZ26");
        var legacyState=legacy.Service.Current() with{PreferredSymbol="/CLZ26",LastProductChoice=new("/CLZ26","Older full-size selection",legacy.Now)};
        File.WriteAllText(Path.Combine(legacy.Directory,"current.json"),JsonSerializer.Serialize(legacyState,CodexAnalysisProvider.Json));
        var migrated=legacy.Create().Current();
        Check(migrated.Settings.Instruments.Length==0&&migrated.LastProductChoice is null&&migrated.PreferredSymbol is null,"restart removes persisted full-size flat contracts and stale AI product choices");
        var holding=new Harness();holding.Configure(new MonitorInstrument("/CLZ26",1000,.01m));holding.Confirm("LONG",99,symbol:"/CLZ26");
        holding.Service.ApplyUniverse(new(holding.Now,[new("/MCLZ26",100,.01m)],[],"Micro products"));
        Check(holding.Service.Current().Markets.Any(m=>m.Symbol=="/CLZ26"&&m.Exposure.Position.State=="LONG")&&holding.Service.Current().Settings.Instruments.Any(s=>s.Symbol=="/MCLZ26"),"micro-only discovery keeps an already-held full-size position available for exit management");
        await WorkflowIntegration();
    }
    private sealed class Provider:IAnalysisProvider
    {
        public TaskCompletionSource<JsonElement> Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<AnalysisResult> Finish=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<AnalysisResult> Analyze(JsonElement snapshot,string directory,CancellationToken token,AnalysisPrompt? prompt=null)
        {
            Check(snapshot.GetProperty("tickSize").GetDecimal()==.25m&&snapshot.GetProperty("pointValue").GetDecimal()==2&&prompt!.Version.EndsWith("+monitor-v4")&&!prompt.AllowResearch&&prompt.Text.Contains("suggestions are not fills")&&prompt.Text.Contains("Choose the product"),"caller analysis uses shared context without launching separate web research");
            Started.SetResult(snapshot);return await Finish.Task.WaitAsync(token);
        }
    }
    private sealed class YieldingProvider:IAnalysisProvider
    {
        public Task<AnalysisResult> Analyze(JsonElement snapshot,string directory,CancellationToken token,AnalysisPrompt? prompt=null)=>throw new ConversationPriorityException();
    }
    private static async Task WorkflowIntegration()
    {
        var directory=Path.Combine(Path.GetTempPath(),"monitor-workflow-"+Guid.NewGuid().ToString("N"));
        var market=new MarketStore(directory);var provider=new Provider();
        var analysis=new AnalysisService(new TradeStore(directory),provider,Path.Combine(directory,"analysis"),market);
        await using var workflow=new AnalysisWorkflow(analysis,Path.Combine(directory,"jobs"),(request,_)=>
        {
            var now=DateTimeOffset.UtcNow;var end=new DateTimeOffset(now.Year,now.Month,now.Day,now.Hour,now.Minute,0,TimeSpan.Zero);
            market.SaveBars(Enumerable.Range(0,35).Select(i=>new PriceBar(request.Symbol!,end.AddMinutes(i-35),100,101,99,100,100)).ToArray());
            var quote=new MarketQuote(request.Symbol!,"FUTURE",100,99.75m,100.25m,null,null,now,true,now);market.SaveQuote(quote);return Task.FromResult(quote);
        });
        var context=new MonitorAnalysisContext("Fixture trigger","scope",1,DateTimeOffset.UtcNow,2,.25m,null,null,null,[]);
        var request=new AnalysisRequest("saved-market",Symbol:"/MNQZ26",Position:new("FLAT")){MonitorContext=context};
        var id=workflow.TryStartMonitor(request,run=>run with{Result=run.Result with{Decision=new("WAIT","Fixture commit guard",null,null,null,true,GuardCode:"fixture-guard")}})!.Id;
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(workflow.TryStartMonitor(request,run=>run) is null&&workflow.Status(id)?.Origin=="monitor","monitor and manual analysis share durable admission without duplicate runs");
        provider.Finish.SetResult(new("BUY","Fixture",["Observed"],["Synthetic"],["Review"],"Below stop",new("BUY","Fixture",100,99,102)));
        var timeout=DateTimeOffset.UtcNow.AddSeconds(5);
        while(workflow.Status(id) is {Active:true}&&DateTimeOffset.UtcNow<timeout)await Task.Delay(10);
        Check(workflow.Status(id)?.State=="completed"&&analysis.Find(id).Result.Decision?.GuardCode=="fixture-guard","workflow persists the final monitor guard instead of the unguarded model answer");
        var untrusted=JsonSerializer.Deserialize<AnalysisRequest>("""{"kind":"saved-market","monitorContext":{"trigger":"injected"},"beforeSave":"injected"}""",CodexAnalysisProvider.Json)!;
        Check(untrusted.MonitorContext is null&&untrusted.BeforeSave is null,"public JSON cannot inject server-owned context or bypass commit guards");
        var yieldedAnalysis=new AnalysisService(new TradeStore(directory),new YieldingProvider(),Path.Combine(directory,"yielded-analysis"),market);
        await using var yielded=new AnalysisWorkflow(yieldedAnalysis,Path.Combine(directory,"yielded-jobs"),(r,_)=>Task.FromResult(market.Quotes(r.Symbol!).Last()));
        var yieldedId=yielded.TryStartMonitor(request,run=>run)!.Id;
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while(yielded.Status(yieldedId) is {Active:true})await Task.Delay(5,deadline.Token);
        Check(yielded.Status(yieldedId)?.State=="deferred"&&yieldedAnalysis.History().Length==0,"chat preemption survives the analysis pipeline as a deferred review without saving a stale or failed trade call");
    }
}
