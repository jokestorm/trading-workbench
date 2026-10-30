using System.Text.Json;
using System.Text.RegularExpressions;

namespace TradingWorkbench;

public record MonitorInstrument(string Symbol,decimal PointValue,decimal TickSize);
public record MonitorSettings(bool Enabled,MonitorInstrument[] Instruments,int PollSeconds=15,int AiCooldownSeconds=90,int ReviewSeconds=300,string Revision="initial",decimal DailyProfitTarget=1000,string? BrokerAccountId=null,decimal? AccountEquity=null);
public record MonitorSettingsEdit(string Revision,bool Enabled,MonitorInstrument[] Instruments,int PollSeconds=15,int AiCooldownSeconds=90,int ReviewSeconds=300,decimal DailyProfitTarget=1000,string? BrokerAccountId=null,decimal? AccountEquity=null);
public record MonitorPositionEdit(string Symbol,long Version,AnalysisPosition Position,decimal? Stop=null,decimal? Target=null,DateTimeOffset? ExpiresAt=null);
public record MonitorPosition(string Symbol,AnalysisPosition Position,long Version,string ScopeId,DateTimeOffset? ConfirmedAt,decimal? Stop=null,decimal? Target=null,DateTimeOffset? ExpiresAt=null,bool ExitRequired=false,string? ExitReason=null,string Source="manual",string PositionBasis="manual");
public record MonitorFillCursor(string Symbol,string AccountId,DateTimeOffset BaselineAt,string[] Seen);
public record MonitorObservation(MarketQuote Quote,PriceBar[] Bars,string? HistoryError=null);
public record MonitorMemory(DateTimeOffset Time,string Symbol,string ScopeId,string Kind,string Detail,TradeDecision? Decision=null);
public record MonitorCall(string Id,DateTimeOffset AvailableAt,DateTimeOffset ValidUntil,long PositionVersion,TradeDecision? Decision,string Invalidation);
public record MonitorMarket(string Symbol,MonitorPosition Exposure,DateTimeOffset? CheckedAt=null,MarketQuote? Quote=null,DateTimeOffset? BarThrough=null,DateTimeOffset? LastAttempt=null,DateTimeOffset? AttemptBar=null,decimal? AttemptPrice=null,string? Trigger=null,string? Error=null,MonitorCall? Latest=null,TradeDecision? CurrentDecision=null,bool AwaitingExecution=false);
public record ProductOpportunity(string Symbol,string Product,string Group,bool Ready,decimal? Last,decimal? SpreadTicks,long? DayVolume,decimal? AtrDollars,decimal? RelativeVolume,string[] Signals,string Position,decimal Score,DateTimeOffset? QuoteTime,DateTimeOffset? BarThrough);
public record ProductChoice(string Symbol,string Reason,DateTimeOffset Time);
public record MonitorState(MonitorSettings Settings,MonitorMarket[] Markets,MonitorMemory[] Memory,string? JobId=null,string? JobSymbol=null,string? JobScope=null,string? Message=null,MonitorFillCursor[]? FillCursors=null,BrokerTransactionReport? Transactions=null,FuturesUniverseState? Universe=null,string? PreferredSymbol=null,ProductChoice? LastProductChoice=null);
public record MonitorAnalysisContext(string Trigger,string ScopeId,long PositionVersion,DateTimeOffset? PositionConfirmedAt,decimal PointValue,decimal TickSize,decimal? CommittedStop,decimal? CommittedTarget,DateTimeOffset? TradeExpiresAt,MonitorMemory[] RecentMemory,string PositionSource="manual",DateTimeOffset? TransactionsSyncedAt=null,ProductOpportunity[]? Opportunities=null,string PositionBasis="manual",decimal? AccountEquity=null,TraderDiscussion[]? Discussion=null,MarketBriefState? MarketBrief=null);

// Durable monitoring and deterministic exits are independent of model latency.
// Transaction fills advance the explicitly confirmed starting position.
public sealed class ContinuousMonitor
{
    private readonly string path;
    private readonly Func<MonitorInstrument,bool,CancellationToken,Task<MonitorObservation>> observe;
    private readonly Func<AnalysisRequest,Func<AnalysisRun,AnalysisRun>,AnalysisJob?> start;
    private readonly Func<string,AnalysisJob?> job;
    private readonly Func<string,AnalysisRun> result;
    private readonly Action<string> cancel;
    private readonly Func<DateTimeOffset> clock;
    private readonly ILogger<ContinuousMonitor> log;
    private readonly Func<BrokerTransactionReport>? transactions;
    private readonly Func<string,string,TraderDiscussion[]>? discussion;
    private readonly Func<bool> aiAvailable;
    private readonly Func<MarketBriefState>? briefContext;
    private readonly object sync=new();
    private readonly SemaphoreSlim sweep=new(1,1);
    private MonitorState state;
    private readonly Dictionary<string,MonitorObservation> observations=new();

    public ContinuousMonitor(string directory,Func<MonitorInstrument,bool,CancellationToken,Task<MonitorObservation>> observe,
        Func<AnalysisRequest,Func<AnalysisRun,AnalysisRun>,AnalysisJob?> start,Func<string,AnalysisJob?> job,
        Func<string,AnalysisRun> result,Action<string> cancel,Func<DateTimeOffset>? clock=null,ILogger<ContinuousMonitor>? logger=null,Func<BrokerTransactionReport>? transactions=null,Func<string,string,TraderDiscussion[]>? discussion=null,Func<bool>? aiAvailable=null,Func<MarketBriefState>? briefContext=null,bool chatOnly=false)
    {
        Directory.CreateDirectory(directory);path=Path.Combine(directory,"current.json");
        this.observe=observe;this.start=start;this.job=job;this.result=result;this.cancel=cancel;this.clock=clock??(()=>DateTimeOffset.UtcNow);
        this.transactions=transactions;this.discussion=discussion;this.aiAvailable=aiAvailable??(()=>true);this.briefContext=briefContext;
        log=logger??Microsoft.Extensions.Logging.Abstractions.NullLogger<ContinuousMonitor>.Instance;
        state=File.Exists(path)?JsonSerializer.Deserialize<MonitorState>(File.ReadAllText(path),CodexAnalysisProvider.Json)!:new(new(false,[]),[],[]);
        if(chatOnly)state=state with{Settings=state.Settings with{Enabled=false},JobId=null,JobSymbol=null,JobScope=null,
            Markets=state.Markets.Select(m=>m with{Latest=null,CurrentDecision=null,AwaitingExecution=false}).ToArray()};
        ApplyUniverse(new(state.Universe?.CheckedAt,state.Settings.Instruments.Where(s=>FuturesProducts.IsTradingProduct(s.Symbol)).ToArray(),state.Universe?.Unavailable.Where(FuturesProducts.IsTradingProduct).ToArray()??[],"Discovering micro futures products automatically."));
        // Restart does not establish that a previously confirmed position is still held.
        Save(state with{Markets=state.Markets.Select(m=>m with{Exposure=m.Exposure with{ConfirmedAt=null}}).ToArray(),Message=chatOnly?"Background AI disabled. Discuss markets in Trader chat.":"Trade caller ready. Confirm current positions after restart."});
    }
    public MonitorState Current()
    {
        lock(sync)
        {
            var report=transactions?.Invoke();
            if(report?.History is {} history)report=report with{History=history with{Accounts=history.Accounts.Select(a=>a with{Fills=a.Fills.TakeLast(30).ToArray()}).ToArray()}};
            return state with{Markets=state.Markets.Select(m=>m with{CurrentDecision=DisplayDecision(m,clock())}).ToArray(),Transactions=report};
        }
    }
    public void ApplyUniverse(FuturesUniverseState discovered)
    {
        lock(sync)
        {
            var held=state.Markets.Where(m=>m.Exposure.Position.State is "LONG" or "SHORT").Select(m=>state.Settings.Instruments.Single(s=>s.Symbol==m.Symbol));
            var specs=held.Concat(discovered.Instruments.Select(Validate).Where(s=>FuturesProducts.IsTradingProduct(s.Symbol))).DistinctBy(s=>s.Symbol).Take(32).ToArray();
            if(specs.Length==0)
            {
                Save(state with{Settings=state.Settings with{Instruments=[],Revision=state.Settings.Instruments.Length>0?Guid.NewGuid().ToString("N"):state.Settings.Revision},Markets=[],FillCursors=[],PreferredSymbol=null,LastProductChoice=null,Universe=discovered});
                return;
            }
            var report=transactions?.Invoke();var history=TransactionsFresh(report,clock())?report!.History:null;
            var accounts=history?.Accounts??[];
            var account=state.Settings.BrokerAccountId is {} id?accounts.FirstOrDefault(a=>a.Id==id):accounts.Length==1?accounts[0]:null;
            var estimates=account is null?[]:BrokerPositionCalculator.Calculate(history! with{Accounts=[account]});
            var cursors=(state.FillCursors??[]).ToList();var markets=new List<MonitorMarket>();var memory=state.Memory;
            foreach(var spec in specs)
            {
                var existing=state.Markets.FirstOrDefault(m=>m.Symbol==spec.Symbol);
                var market=existing??new MonitorMarket(spec.Symbol,new(spec.Symbol,new(),0,"unconfirmed",null));
                if(account is not null&&(existing is null||market.Exposure.ConfirmedAt is null&&market.Exposure.PositionBasis=="transaction-window"))
                {
                    var estimate=estimates.FirstOrDefault(p=>p.Symbol==spec.Symbol);
                    if(estimate?.State!="UNKNOWN"&&(estimate is not null||market.Exposure.Position.State is not ("LONG" or "SHORT")))
                    {
                        var p=market.Exposure;var position=estimate is null?new AnalysisPosition("FLAT"):new(estimate.State,estimate.Quantity,estimate.AverageEntry);
                        var changed=position!=p.Position;var scope=changed?Guid.NewGuid().ToString("N"):p.ScopeId;
                        market=market with{Exposure=p with{Position=position,Version=p.Version+1,ScopeId=scope,ConfirmedAt=history!.Through,Source="transactions",PositionBasis="transaction-window",
                            ExitRequired=!changed&&p.ExitRequired,ExitReason=!changed?p.ExitReason:null,Stop=changed?null:p.Stop,Target=changed?null:p.Target,ExpiresAt=changed?null:p.ExpiresAt},Latest=null};
                        cursors.RemoveAll(c=>c.Symbol==spec.Symbol);cursors.Add(new(spec.Symbol,account.Id,history!.Through,[]));
                        memory=memory.Append(new(clock(),spec.Symbol,scope,"transaction-baseline","Position estimated from the complete fetched transaction window, assuming flat before its start. Not a broker holdings response.")).Where(e=>e.Time>=clock().AddHours(-3)).TakeLast(240).ToArray();
                    }
                }
                markets.Add(market);
            }
            var changedList=!state.Settings.Instruments.SequenceEqual(specs);
            Save(state with{Settings=state.Settings with{Instruments=specs,Revision=changedList?Guid.NewGuid().ToString("N"):state.Settings.Revision},Markets=markets.ToArray(),FillCursors=cursors.Where(c=>specs.Any(s=>s.Symbol==c.Symbol)).ToArray(),Memory=memory,Universe=discovered,
                PreferredSymbol=state.PreferredSymbol is {} preferred&&FuturesProducts.IsTradingProduct(preferred)?preferred:null,
                LastProductChoice=state.LastProductChoice is {} choice&&FuturesProducts.IsTradingProduct(choice.Symbol)?choice:null});
        }
    }
    private TradeDecision DisplayDecision(MonitorMarket market,DateTimeOffset now)
    {
        var p=market.Exposure;
        if(p.ExitRequired)return new(p.Position.State=="LONG"?"SELL_CLOSE":"BUY_COVER","Exit required: "+p.ExitReason+". Verify actual broker exposure.",null,null,null,true,GuardCode:"monitor-exit-required");
        string? reason=!state.Settings.Enabled?"Monitoring is stopped.":!Confirmed(p,now)?"Confirm current broker exposure to enable calls.":!Fresh(market.Quote,market.Symbol,now)?"Fresh realtime futures quote unavailable.":market.Latest is null?"Waiting for a programmatic trigger and AI analysis.":market.Latest.PositionVersion!=p.Version?"Position changed since this call.":now>market.Latest.ValidUntil?market.AwaitingExecution?"Entry call expired. Confirm the actual fill or confirm flat before another entry.":"Previous call expired; monitoring for a fresh setup.":!EntryAvailable(market,market.Latest.Decision,state.Settings.Instruments.Single(s=>s.Symbol==market.Symbol))?"Price moved outside the proposed entry; confirm any actual fill before another entry.":null;
        if(BrokerFollowing(p)&&!TransactionsFresh(transactions?.Invoke(),now))reason="Refreshing broker executions before the next call.";
        return reason is not null?new("WAIT",reason,null,null,null,true,GuardCode:"monitor-not-current"):market.Latest!.Decision??new("WAIT","No decision available.",null,null,null);
    }
    private void Save(MonitorState next)
    {
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(next,CodexAnalysisProvider.Json));
        // Windows indexing/security readers can briefly deny replacement. Keep the
        // atomic rename and bound retries; permanent storage errors still surface.
        for(var attempt=0;;attempt++)
        {
            try{File.Move(path+".tmp",path,true);break;}
            catch(Exception e) when(attempt<4&&e is IOException or UnauthorizedAccessException){Thread.Sleep(20*(attempt+1));}
        }
        state=next;
    }
    private MonitorMemory[] Remember(MonitorMemory item)=>state.Memory.Append(item).Where(e=>e.Time>=clock().AddHours(-3)).TakeLast(240).ToArray();
    private void Replace(MonitorMarket market)=>Save(state with{Markets=state.Markets.Select(m=>m.Symbol==market.Symbol?market:m).ToArray()});
    public static MonitorInstrument Validate(MonitorInstrument spec)
    {
        if(spec is null)throw new FormatException("Supply contract specifications for each monitored market.");
        var symbol=MarketStore.Symbol(spec.Symbol);
        if(!Regex.IsMatch(symbol,@"^/[A-Z0-9]{1,8}[FGHJKMNQUVXZ][0-9]{2}$")||spec.PointValue is <=0 or >1_000_000||spec.TickSize is <=0 or >1000)
            throw new FormatException("Use a dated futures symbol, positive dollar point value and positive tick size. Verify these with the exchange.");
        return spec with{Symbol=symbol};
    }
    public MonitorState Configure(MonitorSettingsEdit edit)
    {
        string? cancelId;
        lock(sync)
        {
            if(edit.Revision!=state.Settings.Revision)throw new FormatException("Monitor settings changed. Reload before saving.");
            if(edit.BrokerAccountId is not null&&!Regex.IsMatch(edit.BrokerAccountId,@"^[A-F0-9]{64}$"))throw new FormatException("Select a linked broker account.");
            if(edit.BrokerAccountId!=state.Settings.BrokerAccountId&&state.Markets.Any(m=>m.Exposure.Position.State is "LONG" or "SHORT"))throw new FormatException("Confirm recorded positions flat before switching the followed account.");
            if(edit.Instruments is null||edit.Instruments.Length>32||edit.PollSeconds is <10 or >300||edit.AiCooldownSeconds is <30 or >3600||edit.ReviewSeconds<edit.AiCooldownSeconds||edit.ReviewSeconds>3600||edit.DailyProfitTarget is <=0 or >1_000_000_000)
                throw new FormatException("Use up to 32 contracts, polling of 10–300 seconds, AI cooldown of 30–3600 seconds and a review interval between cooldown and 3600 seconds.");
            var specs=edit.Instruments.Select(Validate).ToArray();
            if(specs.Select(s=>s.Symbol).Distinct().Count()!=specs.Length||edit.Enabled&&specs.Length==0)throw new FormatException("Use distinct dated contracts; add at least one before starting monitoring.");
            foreach(var existing in state.Markets.Where(m=>m.Exposure.Position.State is "LONG" or "SHORT"))
            {
                var old=state.Settings.Instruments.Single(s=>s.Symbol==existing.Symbol);
                if(!specs.Contains(old))throw new FormatException("Keep contracts and their specifications unchanged while a position is recorded. Confirm the actual position is flat before removing it.");
            }
            var markets=specs.Select(s=>state.Markets.FirstOrDefault(m=>m.Symbol==s.Symbol)??new(s.Symbol,new(s.Symbol,new(),0,"unconfirmed",null))).ToArray();
            // Changed contract specifications require a new explicit position confirmation.
            markets=markets.Select(m=>state.Settings.Instruments.Contains(specs.Single(s=>s.Symbol==m.Symbol))?m:m with{Exposure=m.Exposure with{ConfirmedAt=null},Latest=null}).ToArray();
            cancelId=state.JobId;
            if(edit.BrokerAccountId!=state.Settings.BrokerAccountId)markets=markets.Select(m=>m with{Exposure=m.Exposure with{ConfirmedAt=null}}).ToArray();
            Save(state with{Settings=new(edit.Enabled,specs,edit.PollSeconds,edit.AiCooldownSeconds,edit.ReviewSeconds,Guid.NewGuid().ToString("N"),edit.DailyProfitTarget,edit.BrokerAccountId,edit.AccountEquity??state.Settings.AccountEquity),Markets=markets,
                FillCursors=edit.BrokerAccountId!=state.Settings.BrokerAccountId?[]:state.FillCursors,
                Message=edit.Enabled?"Scanning futures for entries and reviewing recorded positions.":"Trade caller stopped."});
        }
        // Never call the workflow while holding the monitor lock (commit guards take it).
        if(cancelId is not null)TryCancel(cancelId);
        return Current();
    }
    public MonitorState Confirm(MonitorPositionEdit edit)
    {
        lock(sync)
        {
            var symbol=MarketStore.Symbol(edit.Symbol);var market=state.Markets.SingleOrDefault(m=>m.Symbol==symbol)??throw new FormatException("Add this dated contract to monitoring first.");
            var before=market.Exposure;
            if(before.Version!=edit.Version)throw new FormatException("Position changed. Reload and check the broker before confirming.");
            var position=DecisionRules.Position(edit.Position);var open=position.State is "LONG" or "SHORT";
            if(before.Position.State is "LONG" or "SHORT"&&position.State!=before.Position.State&&position.State!="FLAT")throw new FormatException("Confirm the recorded position flat before changing sides or marking it unknown.");
            var spec=state.Settings.Instruments.Single(s=>s.Symbol==symbol);
            var sameTrade=open&&before.Position.State==position.State;
            if(open)
            {
                if(edit.Stop is null or <=0||edit.Target is <=0||new[]{edit.Stop,edit.Target}.Any(v=>v.HasValue&&v.Value%spec.TickSize!=0))throw new FormatException("An open position needs a positive protective stop on the contract's tick grid; any target must also be on that grid.");
                if(!sameTrade&&(position.State=="LONG"?edit.Stop>=position.AverageEntry:edit.Stop<=position.AverageEntry))throw new FormatException("The initial protective stop must be below a long entry or above a short entry.");
                if(sameTrade&&before.Stop.HasValue&&(position.State=="LONG"?edit.Stop<before.Stop:edit.Stop>before.Stop))throw new FormatException("The committed stop can tighten but cannot widen within a trade.");
                if(edit.Target.HasValue&&(position.State=="LONG"?edit.Target<=position.AverageEntry:edit.Target>=position.AverageEntry))throw new FormatException("A target must be above a long entry or below a short entry.");
                if(edit.ExpiresAt.HasValue&&edit.ExpiresAt<=clock()&&(!sameTrade||before.ExpiresAt!=edit.ExpiresAt))throw new FormatException("Choose a future trade expiry.");
                if(sameTrade&&before.ExpiresAt.HasValue&&(edit.ExpiresAt is null||edit.ExpiresAt>before.ExpiresAt))throw new FormatException("A committed trade expiry cannot be extended or removed.");
            }
            if(before.ExitRequired&&position.State!="FLAT"&&(!sameTrade||position.Quantity!=before.Position.Quantity||position.AverageEntry!=before.Position.AverageEntry))
                throw new FormatException("An exit is required. Confirm the position flat before recording a new trade; unchanged exposure may be reconfirmed.");
            var sameScope=sameTrade||position.State=="FLAT"&&before.Position.State=="FLAT";
            var scope=sameScope?before.ScopeId:Guid.NewGuid().ToString("N");
            var exposure=new MonitorPosition(symbol,position,before.Version+1,scope,clock(),open?edit.Stop:null,open?edit.Target:null,open?edit.ExpiresAt:null,
                position.State!="FLAT"&&before.ExitRequired,position.State!="FLAT"?before.ExitReason:null);
            var unchanged=position==before.Position&&edit.Stop==before.Stop&&edit.Target==before.Target&&edit.ExpiresAt==before.ExpiresAt;
            var updated=market with{Exposure=exposure,Latest=null,LastAttempt=unchanged?market.LastAttempt:null,AttemptBar=unchanged?market.AttemptBar:null,AttemptPrice=unchanged?market.AttemptPrice:null,AwaitingExecution=false};
            if(open&&!sameTrade&&market.Latest is {} previous&&clock()-previous.AvailableAt<=TimeSpan.FromHours(3))
                Save(state with{Memory=Remember(new(clock(),symbol,scope,"entry-context",previous.Invalidation,previous.Decision))});
            var memory=Remember(new(clock(),symbol,scope,"position-confirmed",$"Manual broker check: {position.State}; quantity {position.Quantity}; entry {position.AverageEntry}; stop {exposure.Stop}. Not API-verified."));
            var cursors=(state.FillCursors??[]).Where(c=>c.Symbol!=symbol).Append(new MonitorFillCursor(symbol,state.Settings.BrokerAccountId??"",exposure.ConfirmedAt!.Value,[])).ToArray();
            Save(state with{Markets=state.Markets.Select(m=>m.Symbol==symbol?updated:m).ToArray(),Memory=memory,FillCursors=cursors});
            return Current();
        }
    }
    public static bool Fresh(MarketQuote? quote,string symbol,DateTimeOffset now)=>quote is {Realtime:true,Last:>0,QuoteTime:not null}&&quote.Symbol==symbol&&quote.AssetType=="FUTURE"&&quote.Bid is not <=0&&quote.Ask is not <=0&&!(quote.Bid>quote.Ask)&&now-quote.QuoteTime<=TimeSpan.FromSeconds(30)&&quote.QuoteTime<=now.AddSeconds(5);
    private static bool EntryAvailable(MonitorMarket market,TradeDecision? decision,MonitorInstrument spec)
    {
        if(decision?.Action is not ("BUY" or "SELL_SHORT"))return true;
        if(decision.Entry is null||decision.Stop is null||decision.Target is null||market.Quote is null)return false;
        var price=decision.Action=="BUY"?market.Quote.Ask??market.Quote.Last:market.Quote.Bid??market.Quote.Last;
        var allowance=Math.Max(spec.TickSize*2,Math.Abs(decision.Entry.Value-decision.Stop.Value)*.25m);
        return price.HasValue&&Math.Abs(price.Value-decision.Entry.Value)<=allowance&&(decision.Action=="BUY"?decision.Stop<price&&price<decision.Target:decision.Target<price&&price<decision.Stop);
    }
    private static bool Confirmed(MonitorPosition exposure,DateTimeOffset now)=>exposure.Position.State!="UNKNOWN"&&exposure.ConfirmedAt.HasValue&&exposure.ConfirmedAt<=now;
    private static bool BrokerFollowing(MonitorPosition exposure)=>exposure.Source=="transactions";
    private static bool TransactionsFresh(BrokerTransactionReport? report,DateTimeOffset now)=>report is {Status:"ready",LastSynced:not null,History:not null}&&report.LastSynced<=now.AddSeconds(5)&&now-report.LastSynced<=TimeSpan.FromSeconds(30);
    private void ReconcileTransactions()
    {
        var report=transactions?.Invoke();if(!TransactionsFresh(report,clock()))return;
        lock(sync)
        {
            var accounts=report!.History!.Accounts;
            var account=state.Settings.BrokerAccountId is {} id?accounts.SingleOrDefault(a=>a.Id==id):accounts.Length==1?accounts[0]:null;
            if(account is null)return;
            foreach(var market in state.Markets.ToArray())
            {
                try
                {
                var p=market.Exposure;if(!Confirmed(p,clock()))continue;
                var cursor=(state.FillCursors??[]).FirstOrDefault(c=>c.Symbol==market.Symbol)??new(market.Symbol,account.Id,p.ConfirmedAt!.Value,[]);
                if(cursor.AccountId.Length>0&&cursor.AccountId!=account.Id)continue;
                var seen=cursor.Seen.ToHashSet(StringComparer.Ordinal);
                var fills=account.Fills.Where(f=>f.Symbol==market.Symbol&&f.Time>cursor.BaselineAt&&!seen.Contains(f.Id)).ToArray();
                var (position,crossed)=BrokerExecutions.Apply(p.Position,fills);
                var newScope=crossed||position.State!=p.Position.State;
                var scope=newScope?Guid.NewGuid().ToString("N"):p.ScopeId;
                var opening=position.State is "LONG" or "SHORT";
                var proposed=market.Latest?.Decision;
                var matched=newScope&&!crossed&&market.AwaitingExecution&&market.Latest is {} latest&&fills.Length>0&&fills.All(f=>f.Time>=latest.AvailableAt&&f.Time<=latest.ValidUntil)&&proposed is {Guarded:false}&&(position.State=="LONG"&&proposed.Action=="BUY"||position.State=="SHORT"&&proposed.Action=="SELL_SHORT");
                var exposure=p with{Position=position,ScopeId=scope,Source="transactions",Version=p.Version+(fills.Length>0?1:0),
                    Stop=!opening?null:newScope?matched?proposed!.Stop:null:p.Stop,Target=!opening?null:newScope?matched?proposed!.Target:null:p.Target,
                    ExpiresAt=newScope?null:p.ExpiresAt,ExitRequired=!newScope&&p.ExitRequired,ExitReason=!newScope?p.ExitReason:null};
                var updated=market with{Exposure=exposure,Latest=fills.Length>0?null:market.Latest,AwaitingExecution=fills.Length>0?false:market.AwaitingExecution,
                    LastAttempt=fills.Length>0?null:market.LastAttempt,AttemptBar=fills.Length>0?null:market.AttemptBar,AttemptPrice=fills.Length>0?null:market.AttemptPrice};
                var memory=state.Memory;
                if(matched)memory=memory.Append(new MonitorMemory(market.Latest!.AvailableAt,market.Symbol,scope,"entry-context",market.Latest.Invalidation,proposed)).TakeLast(240).ToArray();
                foreach(var fill in fills)
                {
                    memory=memory.Append(new MonitorMemory(fill.Time,market.Symbol,scope,"broker-fill",$"Executed {(fill.Amount>0?"BUY":"SELL")} {Math.Abs(fill.Amount)} @ {fill.Price}; resulting position {position.State} {position.Quantity}.")).Where(e=>e.Time>=clock().AddHours(-3)).TakeLast(240).ToArray();
                    seen.Add(fill.Id);
                }
                var currentIds=account.Fills.Where(f=>f.Symbol==market.Symbol&&f.Time>cursor.BaselineAt).Select(f=>f.Id).ToHashSet();
                var nextCursor=cursor with{AccountId=account.Id,Seen=seen.Where(currentIds.Contains).ToArray()};
                if(fills.Length>0||p.Source!="transactions"||cursor.AccountId!=account.Id)
                    Save(state with{Markets=state.Markets.Select(m=>m.Symbol==market.Symbol?updated:m).ToArray(),Memory=memory,
                        FillCursors=(state.FillCursors??[]).Where(c=>c.Symbol!=market.Symbol).Append(nextCursor).ToArray()});
                }
                catch(FormatException error)
                {
                    var updated=market with{Exposure=market.Exposure with{ConfirmedAt=null,Source="transactions"},Latest=null,Error=error.Message};
                    Save(state with{Markets=state.Markets.Select(m=>m.Symbol==market.Symbol?updated:m).ToArray(),Memory=Remember(new(clock(),market.Symbol,market.Exposure.ScopeId,"broker-reconcile-required",error.Message))});
                }
            }
        }
    }
    private MonitorMarket Protect(MonitorMarket market,DateTimeOffset now)
    {
        var p=market.Exposure;
        if(p.ExitRequired||p.Position.State is not ("LONG" or "SHORT"))return market;
        string? reason=p.ExpiresAt<=now?"Committed trade time expired":null;
        if(Fresh(market.Quote,market.Symbol,now))
        {
            var price=p.Position.State=="LONG"?market.Quote!.Bid??market.Quote.Last:market.Quote!.Ask??market.Quote.Last;
            if(p.Stop.HasValue&&(p.Position.State=="LONG"?price<=p.Stop:price>=p.Stop))reason="Committed protective stop breached";
            else if(reason is null&&p.Target.HasValue&&(p.Position.State=="LONG"?price>=p.Target:price<=p.Target))reason="Committed target reached";
        }
        if(reason is null)return market;
        Save(state with{Memory=Remember(new(now,market.Symbol,p.ScopeId,"exit-required",reason))});
        return market with{Exposure=p with{ExitRequired=true,ExitReason=reason},Latest=null};
    }
    private void Collect()
    {
        string? id;lock(sync)id=state.JobId;if(id is null)return;
        var status=job(id);if(status is {Active:true})return;
        AnalysisRun? completed=status?.State=="completed"?result(id):null;
        lock(sync)
        {
            if(state.JobId!=id)return;
            var market=state.Markets.FirstOrDefault(m=>m.Symbol==state.JobSymbol);
            if(completed is not null&&market is not null&&market.Exposure.ScopeId==state.JobScope)
            {
                var memory=Remember(new(completed.CreatedAt,market.Symbol,market.Exposure.ScopeId,"analysis",completed.Result.Invalidation,completed.Result.Decision));
                var version=completed.Snapshot.GetProperty("monitorContext").GetProperty("positionVersion").GetInt64();
                var validity=completed.Result.Decision?.Action is "BUY" or "SELL_SHORT"?60:state.Settings.ReviewSeconds;
                var call=new MonitorCall(completed.Id,completed.CreatedAt,completed.CreatedAt.AddSeconds(validity),version,completed.Result.Decision,completed.Result.Invalidation);
                var pending=completed.Result.Decision is {Action:"BUY" or "SELL_SHORT",Guarded:false}&&market.Exposure.Version==version;
                Save(state with{Memory=memory,Markets=state.Markets.Select(m=>m.Symbol==market.Symbol?m with{Latest=call,AwaitingExecution=m.AwaitingExecution||pending}:m).ToArray()});
            }
            if(status?.State=="deferred"&&market is not null)Replace(market with{LastAttempt=null,AttemptBar=null,AttemptPrice=null});
            Save(state with{JobId=null,JobSymbol=null,JobScope=null,Message=status?.State=="deferred"?"Trader chat has AI priority. Market and exit monitoring continue.":completed is not null?"Analysis saved. Monitoring continues.":"Analysis did not complete. Monitoring and deterministic exit checks continue."});
        }
    }
    private sealed record Candidate(MonitorMarket Market,MonitorInstrument Instrument,DateTimeOffset BarThrough,decimal Price,string Reason,int Priority,decimal Score);
    private ProductOpportunity Opportunity(MonitorMarket market,MonitorInstrument spec,MonitorObservation data,DateTimeOffset now)
    {
        var technicals=TechnicalIndicators.Calculate(data.Bars);var q=data.Quote;var values=technicals.Values;var product=FuturesProducts.Find(market.Symbol);
        var spread=q.Bid.HasValue&&q.Ask.HasValue?(q.Ask-q.Bid)/spec.TickSize:null;
        var directional=technicals.Signals.Count(s=>s.Direction is "UP" or "DOWN");
        var score=directional*8+Math.Min(values?.RelativeVolume20??0,4)*3+(decimal)Math.Log10(Math.Max(1,q.TotalVolume??0))-Math.Min(spread??10,20);
        var ready=market.Error is null&&Confirmed(market.Exposure,now)&&Fresh(q,market.Symbol,now)&&technicals.BarsUsed>=21&&technicals.AsOf<=now&&now-technicals.AsOf<=TimeSpan.FromSeconds(90)&&(FuturesProducts.IsTradingProduct(market.Symbol)||market.Exposure.Position.State is "LONG" or "SHORT");
        return new(market.Symbol,product?.Name??market.Symbol,product?.Group??"Other futures",ready,q.Last,spread,q.TotalVolume,values?.Atr14*spec.PointValue,values?.RelativeVolume20,technicals.Signals.Select(s=>s.Name+" "+s.Direction).ToArray(),market.Exposure.Position.State,score,q.QuoteTime,technicals.AsOf);
    }
    private Candidate? Evaluate(MonitorMarket market,MonitorInstrument spec,MonitorObservation data,DateTimeOffset now)
    {
        if(market.Exposure.Position.State=="FLAT"&&!FuturesProducts.IsTradingProduct(market.Symbol))return null;
        if(!Confirmed(market.Exposure,now)||market.Exposure.ExitRequired||market.AwaitingExecution||!Fresh(data.Quote,market.Symbol,now)||data.HistoryError is not null||BrokerFollowing(market.Exposure)&&!TransactionsFresh(transactions?.Invoke(),now))return null;
        if(market.LastAttempt.HasValue&&now-market.LastAttempt<TimeSpan.FromSeconds(state.Settings.AiCooldownSeconds))return null;
        var technicals=TechnicalIndicators.Calculate(data.Bars);
        if(technicals.BarsUsed<21||technicals.AsOf is null||now-technicals.AsOf>TimeSpan.FromSeconds(90)||technicals.AsOf>now)return null;
        var newBar=market.AttemptBar is null||technicals.AsOf>market.AttemptBar;
        var open=market.Exposure.Position.State is "LONG" or "SHORT";
        var lastReview=open?market.LastAttempt:state.Markets.Where(m=>m.Exposure.Position.State=="FLAT").Select(m=>m.LastAttempt).Max();
        var periodic=lastReview is null||now-lastReview>=TimeSpan.FromSeconds(state.Settings.ReviewSeconds);
        var moved=technicals.Values?.Atr14 is >0&&market.AttemptPrice.HasValue&&Math.Abs(data.Quote.Last!.Value-market.AttemptPrice.Value)>=technicals.Values.Atr14*.75m;
        var directional=technicals.Signals.Where(s=>s.Direction is "UP" or "DOWN").ToArray();
        var selected=state.PreferredSymbol==market.Symbol;
        var reason=open&&(newBar||moved||periodic)?"Open-position thesis review":selected?"AI-selected product: assess this contract's entry":newBar&&directional.Length>0?string.Join("; ",directional.Select(s=>s.Name+" "+s.Direction)):moved?"Price moved at least 0.75 ATR since the last AI request":periodic?"Periodic opportunity review":null;
        return reason is null?null:new(market,spec,technicals.AsOf.Value,data.Quote.Last!.Value,reason,open?100:selected?90:directional.Length>0?50:moved?40:10,Opportunity(market,spec,data,now).Score);
    }
    public async Task Tick(CancellationToken token)
    {
        if(!await sweep.WaitAsync(0,token))return;
        try
        {
            Collect();var before=Current();if(!before.Settings.Enabled)return;
            ReconcileTransactions();before=Current();
            var now=clock();var due=before.Markets.Where(m=>m.CheckedAt is null||now-m.CheckedAt>=TimeSpan.FromSeconds(before.Settings.PollSeconds)).ToArray();
            // Bounded concurrency; one failed market does not abort the remaining universe.
            foreach(var batch in due.Chunk(4))await Task.WhenAll(batch.Select(async market=>
            {
                var spec=before.Settings.Instruments.Single(s=>s.Symbol==market.Symbol);
                MonitorObservation? data=null;string? error=null;
                try
                {
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    data=await observe(spec,market.BarThrough is null||clock()-market.BarThrough>=TimeSpan.FromSeconds(60),timeout.Token).WaitAsync(timeout.Token);
                    if(data.Quote.Symbol!=spec.Symbol||data.Bars.Any(b=>b.Symbol!=spec.Symbol))throw new FormatException("Market response does not match the monitored contract.");
                }
                catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
                catch(Exception e) when(e is FormatException or HttpRequestException or OperationCanceledException){error="Market refresh failed. Previous observations are historical.";log.LogWarning("Continuous monitor refresh failed errorType={ErrorType}",e.GetType().Name);}
                lock(sync)
                {
                    if(state.Settings.Revision!=before.Settings.Revision||!state.Settings.Enabled)return;
                    var current=state.Markets.Single(m=>m.Symbol==market.Symbol);var checkedAt=clock();
                    if(data is not null)
                    {
                        // A late/out-of-order quote cannot erase a newer observation or exit.
                        if(current.Quote?.QuoteTime is not null&&data.Quote.QuoteTime<current.Quote.QuoteTime)data=data with{Quote=current.Quote};
                        observations[market.Symbol]=data;
                        current=current with{Quote=data.Quote,BarThrough=data.Bars.Length>0?data.Bars.Max(b=>b.Time).AddMinutes(1):null};
                    }
                    current=current with{CheckedAt=checkedAt,Error=error??data?.HistoryError??(!Fresh(current.Quote,market.Symbol,checkedAt)?"Fresh realtime futures quote unavailable.":null)};
                    Replace(Protect(current,checkedAt));
                }
            }));
            Candidate? candidate;MonitorAnalysisContext context;string revision;
            lock(sync)
            {
                if(!state.Settings.Enabled||state.JobId is not null||!aiAvailable())return;
                now=clock();
                candidate=state.Markets.Select(m=>observations.TryGetValue(m.Symbol,out var data)&&m.Error is null?Evaluate(m,state.Settings.Instruments.Single(s=>s.Symbol==m.Symbol),data,now):null)
                    .Where(c=>c is not null).OrderByDescending(c=>c!.Priority).ThenByDescending(c=>c!.Score).ThenBy(c=>c!.Market.LastAttempt??DateTimeOffset.MinValue).FirstOrDefault();
                if(candidate is null)return;
                var p=candidate.Market.Exposure;revision=state.Settings.Revision;
                var memory=state.Memory.Where(e=>e.Symbol==candidate.Market.Symbol&&e.ScopeId==p.ScopeId&&e.Time>=now.AddHours(-3)).TakeLast(16).ToArray();
                var opportunities=state.Markets.Where(m=>observations.ContainsKey(m.Symbol)).Select(m=>Opportunity(m,state.Settings.Instruments.Single(s=>s.Symbol==m.Symbol),observations[m.Symbol],now)).OrderByDescending(o=>o.Ready).ThenByDescending(o=>o.Score).Take(24).ToArray();
                context=new(candidate.Reason,p.ScopeId,p.Version,p.ConfirmedAt,candidate.Instrument.PointValue,candidate.Instrument.TickSize,p.Stop,p.Target,p.ExpiresAt,memory,p.Source,transactions?.Invoke().LastSynced,opportunities,p.PositionBasis,state.Settings.AccountEquity,discussion?.Invoke(candidate.Market.Symbol,p.ScopeId),briefContext?.Invoke());
            }
            var request=new AnalysisRequest("saved-market",Symbol:candidate.Market.Symbol,Days:1,Position:candidate.Market.Exposure.Position){MonitorContext=context};
            var admitted=start(request,run=>Guard(run,candidate,revision));
            if(admitted is null)return; // Shared workflow is occupied; keep latest observations, never queue old calls.
            lock(sync)
            {
                Save(state with{JobId=admitted.Id,JobSymbol=candidate.Market.Symbol,JobScope=context.ScopeId,PreferredSymbol=state.PreferredSymbol==candidate.Market.Symbol?null:state.PreferredSymbol,Message="Comparing products and analyzing the strongest opportunity."});
                var current=state.Markets.FirstOrDefault(m=>m.Symbol==candidate.Market.Symbol);
                if(current is not null)Replace(current with{LastAttempt=clock(),AttemptBar=candidate.BarThrough,AttemptPrice=candidate.Price,Trigger=candidate.Reason});
            }
            if(Current().Settings.Revision!=revision)TryCancel(admitted.Id);
        }
        finally{sweep.Release();}
    }
    private static AnalysisRun Override(AnalysisRun run,string action,string reason,string code)=>run with{Result=run.Result with{Summary=action,Assessment=reason,Decision=new(action,reason,null,null,null,true,GuardCode:code)}};
    private AnalysisRun Guard(AnalysisRun run,Candidate request,string revision)
    {
        ReconcileTransactions();
        lock(sync)
        {
            var market=state.Markets.FirstOrDefault(m=>m.Symbol==request.Market.Symbol);var now=clock();
            if(!state.Settings.Enabled||state.Settings.Revision!=revision||market is null||market.Exposure.Version!=request.Market.Exposure.Version)
                return Override(run,"WAIT","Monitor settings or confirmed position changed during analysis.","monitor-context-changed");
            market=Protect(market,now);Replace(market);
            if(market.Exposure.ExitRequired)return Override(run,market.Exposure.Position.State=="LONG"?"SELL_CLOSE":"BUY_COVER",market.Exposure.ExitReason!,"monitor-exit-required");
            if(!Confirmed(market.Exposure,now))return Override(run,"WAIT","Confirm current broker exposure after restart.","monitor-position-unconfirmed");
            if(BrokerFollowing(market.Exposure)&&!TransactionsFresh(transactions?.Invoke(),now))return Override(run,"WAIT","Refreshing broker executions before the next call.","monitor-transactions-stale");
            if(!Fresh(market.Quote,market.Symbol,now))return Override(run,"WAIT","A realtime futures quote within 30 seconds is required at publication.","monitor-quote-stale");
            if(run.Result.Decision?.Action is "BUY" or "SELL_SHORT"&&!FuturesProducts.IsTradingProduct(market.Symbol))return Override(run,"WAIT","New entries are limited to the micro product universe.","monitor-product-excluded");
            if(run.Result.PreferredSymbol is {} selected)
            {
                var opportunities=run.Snapshot.GetProperty("monitorContext").TryGetProperty("opportunities",out var choices)&&choices.ValueKind==JsonValueKind.Array?choices.Deserialize<ProductOpportunity[]>(CodexAnalysisProvider.Json)??[]:[];
                if(!opportunities.Any(o=>o.Symbol==selected&&o.Ready))return Override(run,"WAIT","AI selected a product without a ready market snapshot.","monitor-product-unavailable");
                if(market.Exposure.Position.State=="FLAT"&&!state.Markets.Any(m=>m.Exposure.Position.State is "LONG" or "SHORT"))
                {
                    Save(state with{LastProductChoice=new(selected,run.Result.Assessment,now),PreferredSymbol=selected!=market.Symbol?selected:state.PreferredSymbol});
                    if(selected!=market.Symbol)return Override(run,"WAIT","AI selected "+selected+"; analyzing its full chart next.","monitor-product-selection");
                }
            }
            if(!EntryAvailable(market,run.Result.Decision,request.Instrument))return Override(run,"WAIT","Price moved outside the entry allowance; request a fresh setup.","monitor-entry-drift");
            return run;
        }
    }
    private void TryCancel(string id){try{cancel(id);}catch(FormatException){}}
    public async Task Run(CancellationToken token)
    {
        while(!token.IsCancellationRequested)
        {
            try{await Tick(token);await Task.Delay(TimeSpan.FromSeconds(1),token);}
            catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
            catch(Exception e){log.LogWarning("Continuous monitor stopped sweep errorType={ErrorType}",e.GetType().Name);try{await Task.Delay(TimeSpan.FromSeconds(5),token);}catch(OperationCanceledException){break;}}
        }
    }
    public static void Map(WebApplication app,ContinuousMonitor monitor)
    {
        app.MapGet("/api/ai/monitor",()=>monitor.Current());
        app.MapPost("/api/ai/monitor/settings",(MonitorSettingsEdit edit)=>{try{return Results.Ok(monitor.Configure(edit));}catch(FormatException e){return Results.BadRequest(new{message=e.Message});}});
        app.MapPost("/api/ai/monitor/position",(MonitorPositionEdit edit)=>{try{return Results.Ok(monitor.Confirm(edit));}catch(FormatException e){return Results.BadRequest(new{message=e.Message});}});
    }
}
