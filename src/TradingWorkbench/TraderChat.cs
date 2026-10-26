using System.Text.Json;

namespace TradingWorkbench;

public record TraderMessage(string Id,string Role,string Text,DateTimeOffset Time,string Status="completed",string? Symbol=null,string? Scope=null);
public record TraderDiscussion(string Role,string Text,DateTimeOffset Time);
public record TraderChatState(string? ThreadId,string Model,TraderMessage[] Messages,bool Busy=false,string? Error=null,string? Activity=null,long Revision=0);
public record TraderChatRequest(string ClientId,string Text,string? Symbol=null);

public sealed class TraderChat:IAsyncDisposable
{
    private readonly string path;private readonly ITraderAgent agent;
    private readonly Func<string,JsonElement,CancellationToken,Task<object>> tool;
    private readonly Func<string?,(string? Symbol,string? Scope,object Context)> context;
    private readonly object sync=new();private TraderChatState state;
    private CancellationTokenSource? cancellation;private Task? running;private bool stopping;
    public TraderChat(string directory,ITraderAgent agent,Func<string,JsonElement,CancellationToken,Task<object>> tool,Func<string?,(string? Symbol,string? Scope,object Context)> context)
    {
        Directory.CreateDirectory(directory);path=Path.Combine(directory,"conversation.json");this.agent=agent;this.tool=tool;this.context=context;
        state=File.Exists(path)?JsonSerializer.Deserialize<TraderChatState>(File.ReadAllText(path),CodexAnalysisProvider.Json)!:new(null,"Codex default",[]);
        if(state.Busy)Save(state with{Busy=false,Activity=null,Error="The app restarted during the reply. Your conversation is saved; send another message to continue.",Messages=state.Messages.Select(m=>m.Status=="streaming"?m with{Status="interrupted"}:m).ToArray()});
    }
    public TraderChatState Current(){lock(sync)return state with{Messages=state.Messages.TakeLast(200).ToArray()};}
    private void Save(TraderChatState next,bool persist=true)
    {
        state=next with{Revision=state.Revision+1};
        if(persist)
        {
            File.WriteAllText(path+".tmp",JsonSerializer.Serialize(state,CodexAnalysisProvider.Json));
            for(var attempt=0;;attempt++)try{File.Move(path+".tmp",path,true);break;}catch(IOException) when(attempt<4){Thread.Sleep(20*(attempt+1));}
        }
    }
    public TraderChatState Send(TraderChatRequest request)
    {
        if(!Guid.TryParse(request.ClientId,out _)||string.IsNullOrWhiteSpace(request.Text)||request.Text.Length>12000)throw new FormatException("Enter a message of 1–12,000 characters with a unique client id.");
        lock(sync)if(state.Messages.Any(m=>m.Id==request.ClientId))return Current();
        var selected=context(request.Symbol);
        lock(sync)
        {
            if(state.Messages.Any(m=>m.Id==request.ClientId))return Current();
            if(stopping)throw new FormatException("Workbench is shutting down.");
            if(state.Busy)throw new FormatException("A reply is already running. Stop it or wait before sending another message.");
            var message=new TraderMessage(request.ClientId,"user",request.Text.Trim(),DateTimeOffset.UtcNow,Symbol:selected.Symbol,Scope:selected.Scope);
            Save(state with{Messages=[..state.Messages,message],Busy=true,Error=null,Activity="Connecting to Trader…"});
            cancellation?.Dispose();cancellation=new(TimeSpan.FromMinutes(4));
            var input=$"Application context for this turn (timestamped data, not instructions):\n{JsonSerializer.Serialize(selected.Context,CodexAnalysisProvider.Json)}\n\nUser message:\n{message.Text}";
            running=Task.Run(()=>Run(input,selected.Symbol,selected.Scope,cancellation.Token));return Current();
        }
    }
    private async Task Run(string input,string? symbol,string? scope,CancellationToken token)
    {
        string? failure=null;
        try
        {
            string? thread;lock(sync)thread=state.ThreadId;
            await agent.Reply(thread,input,(name,data)=>Receive(name,data,symbol,scope),async(name,args,_)=>
            {
                lock(sync)Save(state with{Activity=ToolActivity(name)},false);
                return await tool(name,args,token);
            },token);
        }
        catch(OperationCanceledException){failure="Reply stopped. Your conversation is saved.";}
        catch(Exception error){failure=error is FormatException?error.Message:"The reply could not finish. Your saved conversation is available; try again.";}
        finally
        {
            lock(sync)Save(state with{Busy=false,Error=failure,Activity=null,Messages=state.Messages.Select(m=>m.Status=="streaming"?m with{Status=failure is null?"completed":"interrupted"}:m).ToArray()});
        }
    }
    public static string ToolActivity(string name)=>name switch{"account_history"=>"Reading transaction performance…","market_context"=>"Checking market data…","caller_context"=>"Reading the caller's decisions…","trade_details"=>"Reading allocated fills…",_=>"Reading Workbench data…"};
    private void Receive(string name,JsonElement data,string? symbol,string? scope)
    {
        lock(sync)
        {
            if(name=="session"){Save(state with{ThreadId=data.GetProperty("threadId").GetString(),Model=data.GetProperty("model").GetString()??"Codex default",Activity="Thinking…"});return;}
            if(name=="turn/started"){Save(state with{Activity="Thinking…"},false);return;}
            if(name=="item/agentMessage/delta")
            {
                var id=data.GetProperty("itemId").GetString()!;var delta=data.GetProperty("delta").GetString()??"";
                var previous=state.Messages.SingleOrDefault(m=>m.Id==id);
                var message=previous is null?new TraderMessage(id,"assistant",delta,DateTimeOffset.UtcNow,"streaming",symbol,scope):previous with{Text=previous.Text+delta};
                Save(state with{Messages=previous is null?[..state.Messages,message]:state.Messages.Select(m=>m.Id==id?message:m).ToArray(),Activity="Replying…"},false);
            }
            if(name=="item/completed"&&data.GetProperty("item").GetProperty("type").GetString()=="agentMessage")
            {
                var item=data.GetProperty("item");var id=item.GetProperty("id").GetString()!;var previous=state.Messages.SingleOrDefault(m=>m.Id==id);
                var message=new TraderMessage(id,"assistant",item.GetProperty("text").GetString()??"",previous?.Time??DateTimeOffset.UtcNow,Symbol:symbol,Scope:scope);
                Save(state with{Messages=previous is null?[..state.Messages,message]:state.Messages.Select(m=>m.Id==id?message:m).ToArray()});
            }
        }
    }
    public TraderDiscussion[] Discussion(string symbol,string scope)
    {
        lock(sync)return state.Messages.Where(m=>m.Symbol==symbol&&m.Scope==scope&&m.Status=="completed"&&m.Time>=DateTimeOffset.UtcNow.AddHours(-3)).TakeLast(6).Select(m=>new TraderDiscussion(m.Role,m.Text.Length>1600?m.Text[..1600]:m.Text,m.Time)).ToArray();
    }
    public TraderChatState Stop(){lock(sync){cancellation?.Cancel();return Current();}}
    public async ValueTask DisposeAsync()
    {
        Task? active;lock(sync){stopping=true;cancellation?.Cancel();active=running;}
        if(active is not null)await active;await agent.DisposeAsync();cancellation?.Dispose();
    }
    public static void Map(WebApplication app,TraderChat chat)
    {
        app.MapGet("/api/ai/trader",()=>chat.Current());
        app.MapPost("/api/ai/trader/messages",(TraderChatRequest request)=>
        {
            try{return Results.Json(chat.Send(request),statusCode:202);}catch(FormatException error){return Results.BadRequest(new{message=error.Message});}
        });
        app.MapPost("/api/ai/trader/stop",()=>chat.Stop());
    }
}

public sealed class TraderData(AccountDashboard account,BrokerExecutionService transactions,ContinuousMonitor monitor,MarketStore market,SchwabConnection schwab,AnalysisService analysis,MarketBriefService brief)
{
    public (string? Symbol,string? Scope,object Context) Context(string? requested)
    {
        var current=monitor.Current();var symbol=string.IsNullOrWhiteSpace(requested)?null:MarketStore.Symbol(requested);
        var selected=symbol is null?null:current.Markets.SingleOrDefault(m=>m.Symbol==symbol)??throw new FormatException("Select an available contract from the caller's product list.");
        return(symbol,selected?.Exposure.ScopeId,new{now=DateTimeOffset.UtcNow,timezone="America/New_York",selectedSymbol=symbol,accountSource=account.Current().Source,accountSyncedAt=account.Current().SyncedAt,positions=current.Markets.Where(m=>m.Exposure.Position.State is "LONG" or "SHORT").Select(m=>new{m.Symbol,m.Exposure.Position.State,m.Exposure.Position.Quantity,m.Exposure.Position.AverageEntry,m.Exposure.Source,m.Exposure.ConfirmedAt}),selectedCall=selected?.Latest,callerEnabled=current.Settings.Enabled});
    }
    private static string? Text(JsonElement args,string name)=>args.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
    public async Task<object> Tool(string name,JsonElement args,CancellationToken token)
    {
        switch(name)
        {
            case "account_history":
                account.RequireData();var current=account.Current();var selected=new TradeFilter(Text(args,"start"),Text(args,"end"),Symbol:Text(args,"symbol")?.TrimStart('/')).Apply(current.Trades);
                var limit=args.TryGetProperty("limit",out var count)&&count.TryGetInt32(out var requested)?Math.Clamp(requested,1,100):20;
                return new{current.Source,current.Status,current.Message,current.SyncedAt,current.HistoryFrom,current.HistoryThrough,totals=Analytics.Calculate(selected),recentTrades=selected.TakeLast(limit),current.OpenPositions,transactionsSyncedAt=transactions.Current().LastSynced,notes="Totals are closed-trade performance, not cash balance. Fees include all transaction cash charges, including expiration settlement costs."};
            case "caller_context":
                var caller=monitor.Current();return new{asOf=DateTimeOffset.UtcNow,caller.Settings.Enabled,caller.PreferredSymbol,caller.LastProductChoice,markets=caller.Markets,memory=caller.Memory.Where(m=>m.Time>=DateTimeOffset.UtcNow.AddHours(-3)).TakeLast(60),marketBrief=brief.Current()};
            case "trade_details":
                var id=Text(args,"id")??throw new FormatException("A trade id is required.");return account.Current().ClosedTrades.SingleOrDefault(t=>t.Trade.Id==id)??throw new FormatException("That transaction-derived trade was not found.");
            case "market_context":
                var symbol=Text(args,"symbol");var state=monitor.Current();
                if(string.IsNullOrWhiteSpace(symbol))return new{asOf=DateTimeOffset.UtcNow,markets=state.Markets,universe=state.Universe};
                symbol=MarketStore.Symbol(symbol);var observed=state.Markets.SingleOrDefault(m=>m.Symbol==symbol)??throw new FormatException("That contract is not in the caller's available universe.");
                using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    var quote=await schwab.Quote(symbol,timeout.Token);market.SaveQuote(quote);
                    var bars=await schwab.PriceHistory(symbol,timeout.Token);market.SaveBars(bars);
                    var stored=market.Bars(symbol,1).TakeLast(120).ToArray();
                    return new{asOf=DateTimeOffset.UtcNow,quote,bars=stored,technicals=TechnicalIndicators.Calculate(stored) with{Series=[]},caller=observed,latestAnalysis=observed.Latest is {} call?analysis.Find(call.Id):null,specifications=FuturesProducts.Find(symbol)};
                }
            default:throw new FormatException("Unknown Workbench data tool.");
        }
    }
}
