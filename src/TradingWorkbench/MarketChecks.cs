using System.Text.Json;
namespace TradingWorkbench;
public static class MarketChecks
{
    public static void Run(){
        void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
        var contractDate=new DateTimeOffset(2026,10,12,12,0,0,TimeSpan.Zero);
        Check(FutureContracts.Candidates("/MES",contractDate).SequenceEqual(new[]{"/MESZ26","/MESH27","/MESM27","/MESU27"}),"MES contract candidates follow quarterly months across years");
        Check(FutureContracts.Candidates("/MGC",contractDate).SequenceEqual(new[]{"/MGCV26","/MGCZ26","/MGCG27","/MGCJ27"})&&FutureContracts.Candidates("/MCL",contractDate).SequenceEqual(new[]{"/MCLV26","/MCLX26","/MCLZ26","/MCLF27"}),"gold and crude use their own month schedules");
        Check(FutureContracts.Candidates("/MCL",new DateTimeOffset(2027,1,1,2,0,0,TimeSpan.Zero))[0]=="/MCLZ26","contract calendar follows Eastern date at UTC year boundary");
        var activeQuote=new MarketQuote("/MCLX26","FUTURE",93,null,null,null,null,contractDate,true,contractDate){TotalVolume=1000,Expiration=contractDate.AddDays(7)};
        Check(FutureContracts.MostActive("/MCL",new[]{activeQuote with{Symbol="/MCLV26",TotalVolume=9000,Expiration=contractDate.AddDays(-1)},activeQuote,activeQuote with{Symbol="/MCLZ26",TotalVolume=100}},contractDate)?.Symbol=="/MCLX26","active selection excludes expired contracts and uses verified volume");
        Check(FutureContracts.MostActive("/MCL",new[]{activeQuote with{Expiration=null},activeQuote with{Symbol="/MESZ26"}},contractDate) is null&&!FutureContracts.ValidActive("/MCL","/MCLX26,OTHER"),"unknown expiration and mismatched active metadata cannot select a contract");
        var directory=Path.Combine(Path.GetTempPath(),"market-check-"+Guid.NewGuid().ToString("N"));var store=new MarketStore(directory);store.SaveWatchlist(["aapl","/mesz26","AAPL"]);Check(new MarketStore(directory).Watchlist().SequenceEqual(new[]{"AAPL","/MESZ26"}),"watchlist normalizes, deduplicates and persists");
        var now=DateTimeOffset.UtcNow;var q=new MarketQuote("AAPL","EQUITY",100,99,101,1,1,now.AddMinutes(-1),false,now);store.SaveQuote(q);store.SaveQuote(q with{FetchedAt=now.AddSeconds(1)});Check(store.Quotes("AAPL").Length==1&&store.Quotes("AAPL")[0].Realtime==false,"identical quotes deduplicate without losing delayed provenance");
        var bars=Enumerable.Range(0,6).Select(i=>new PriceBar("AAPL",now.AddMinutes(-10+i),100+i,102+i,99+i,101+i,100)).ToArray();store.SaveBars(bars);store.SaveBars(bars);Check(store.Bars("AAPL").Length==6,"overlapping historical bars do not duplicate");store.SaveBars([bars[0] with{Close=102}]);Check(store.Bars("AAPL")[0].Close==102,"corrected historical bars update their original timestamp");
        var rejected=false;try{store.SaveBars([bars[0] with{Symbol="BAD"},bars[1] with{Symbol="BAD",Low=1000}]);}catch(FormatException){rejected=true;}Check(rejected&&store.Bars("BAD").Length==0,"invalid history rolls back the entire batch");
        using var response=JsonDocument.Parse(JsonSerializer.Serialize(new{candles=new[]{new{datetime=now.AddMinutes(-2).ToUnixTimeMilliseconds(),open=100,high=102,low=99,close=101,volume=10},new{datetime=now.AddSeconds(-15).ToUnixTimeMilliseconds(),open=100,high=102,low=99,close=101,volume=10}}}));Check(SchwabConnection.ParseHistory(response.RootElement,"AAPL",now).Length==1,"unfinished minute bars are excluded");
        var snapshot=JsonSerializer.SerializeToElement(store.Snapshot("AAPL","bars",1));Check(snapshot.GetProperty("bars").GetArrayLength()==6&&snapshot.GetProperty("provenance").GetString()!.Contains("closed"),"AI market snapshot preserves observed bar provenance and range");
        rejected=false;try{store.Snapshot("AAPL","quotes");}catch(FormatException){rejected=true;}Check(rejected,"insufficient quote history cannot produce an AI snapshot");
        for(var i=0;i<5;i++)store.SaveQuote(q with{QuoteTime=now.AddSeconds(-50+i),Last=100+i});var quotes=JsonSerializer.SerializeToElement(store.Snapshot("AAPL","quotes"));Check(quotes.GetProperty("quotes").GetArrayLength()==6&&quotes.GetProperty("provenance").GetString()!.Contains("NOT OHLCV"),"sampled quotes remain distinct from OHLCV bars for AI");
    }
}
