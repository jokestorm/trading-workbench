using System.Text.Json;
namespace TradingWorkbench;

public static class SignalContinuityChecks
{
    private sealed class TriggerProvider:IAnalysisProvider
    {
        public JsonElement LastSnapshot {get;private set;}
        public string LastPrompt {get;private set;}="";
        public Task<AnalysisResult> Analyze(JsonElement snapshot,string directory,CancellationToken token,AnalysisPrompt? prompt=null)
        {
            LastSnapshot=snapshot;LastPrompt=prompt!.Text;
            return Task.FromResult(new AnalysisResult("UP · WAIT","Watch the breakout",["Observed resistance"],["Pending confirmation"],["Watch 100"],"Invalid below 99",new("WAIT","NEXT: Buy after a one-minute close above 100 within 10 minutes.",null,null,null,false,"UP")));
        }
    }
    public static async Task Run()
    {
        var directory=Path.Combine(Path.GetTempPath(),"signal-continuity-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var trades=new TradeStore(directory);var market=new MarketStore(directory);var provider=new TriggerProvider();
            var analysis=new AnalysisService(trades,provider,Path.Combine(directory,"analysis"),market);
            var now=DateTimeOffset.UtcNow;var minute=DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds()/60*60);
            market.SaveBars(Enumerable.Range(0,6).Select(i=>new PriceBar("/MESZ26",minute.AddMinutes(i-6),99,100,98,99,10)).ToArray());
            market.SaveQuote(new("/MESZ26","FUTURE",99,null,null,null,null,now,true,now));
            var request=new AnalysisRequest("saved-market",Symbol:"/MESZ26",Position:new("FLAT"));
            await analysis.Analyze(analysis.Preview(request).Token,CancellationToken.None);
            market.SaveBars([new("/MESZ26",minute.AddMinutes(-1),99,101.5m,99,101,20)]);
            market.SaveQuote(new("/MESZ26","FUTURE",101,null,null,null,null,now,true,now));
            await analysis.Analyze(analysis.Preview(request).Token,CancellationToken.None);
            if(!provider.LastSnapshot.TryGetProperty("previousSignal",out var previous)||!previous.GetRawText().Contains("one-minute close above 100")||!provider.LastPrompt.Contains("one-minute close above 100"))
                throw new Exception("Second analysis cannot see the earlier NEXT trigger even though a fresh closed candle crossed it.");
            Console.WriteLine("PASS repeat analysis receives its previous NEXT condition alongside fresh bars");
            if(!provider.LastPrompt.Contains("original expiry")||!provider.LastPrompt.Contains("NOW trade")||!provider.LastPrompt.Contains("not an obligation to trade"))throw new Exception("Prompt must evaluate prior triggers without forcing an entry or renewing expiry.");
            Console.WriteLine("PASS market prompt re-evaluates prior triggers without moving levels, renewing expiry, or forcing entries");
            var saved=analysis.History().First();var cases=0;
            void Isolated(AnalysisRun candidate,AnalysisRequest input,bool expected)
            {
                var isolated=new AnalysisService(trades,provider,Path.Combine(directory,"case-"+cases++),market);
                isolated.Save(candidate);
                var snapshot=isolated.Preview(input).Snapshot;
                var present=snapshot.GetProperty("previousSignal").ValueKind==JsonValueKind.Object;
                if(present!=expected)throw new Exception("Prior signals must match exact contract, position, history source, and recent time.");
            }
            Isolated(saved,request,true);
            Isolated(saved with{CreatedAt=now.AddHours(-4)},request,false);
            Isolated(saved with{CreatedAt=now.AddMinutes(10)},request,false);
            Isolated(saved,request with{Position=new("LONG",1,101)},false);
            var otherSnapshot=JsonSerializer.SerializeToNode(saved.Snapshot,CodexAnalysisProvider.Json)!.AsObject();otherSnapshot["symbol"]="/MESU26";
            Isolated(saved with{Snapshot=JsonSerializer.SerializeToElement(otherSnapshot,CodexAnalysisProvider.Json)},request,false);
            otherSnapshot=JsonSerializer.SerializeToNode(saved.Snapshot,CodexAnalysisProvider.Json)!.AsObject();otherSnapshot.Remove("bars");
            Isolated(saved with{Snapshot=JsonSerializer.SerializeToElement(otherSnapshot,CodexAnalysisProvider.Json)},request,false);
            Console.WriteLine("PASS continuity excludes other contracts, changed positions, future/old signals, and incompatible chart sources");
            var inputSnapshot=provider.LastSnapshot;
            var opening=new AnalysisResult("DOWN · SELL","Confirmed trigger",["Close confirmed"],["Observed data"],["Manage position"],"Above resistance",new("SELL_SHORT","NOW: Original trigger confirmed; short for 5–20 minutes.",101,102,99,false,"DOWN"));
            if(DecisionRules.Apply(opening,inputSnapshot,now).Decision!.Action!="SELL_SHORT")throw new Exception("A fresh, confirmed short must survive backend guards.");
            if(DecisionRules.Apply(opening,inputSnapshot,now.AddMinutes(3)).Decision is not {Guarded:true,GuardCode:"bars-stale"})throw new Exception("Stale data must retain a classified backend WAIT.");
            Console.WriteLine("PASS confirmed fresh short survives checks while stale data keeps a classified app WAIT");
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(directory,true);}
    }
}
