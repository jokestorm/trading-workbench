using System.Text.Json;
namespace TradingWorkbench;

public static class TimeframeChecks
{
    public static async Task Run()
    {
        void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS "+message);}
        var start=new DateTimeOffset(2026,10,20,0,0,0,TimeSpan.Zero);var now=start.AddDays(5);
        PriceBar Bar(int i)=>new("AAPL",start.AddMinutes(i),100+i,102+i,99+i,101+i,100);
        var minutes=Enumerable.Range(0,3600).Select(Bar).ToArray();
        var five=TimeframeDirections.Aggregate(minutes.Take(5),5,now).Single();
        Check(five.Open==100&&five.Close==105&&five.High==106&&five.Low==99&&five.Volume==500,"higher-timeframe aggregation conserves OHLCV and bucket boundaries");
        Check(TimeframeDirections.Aggregate(minutes.Take(4),5,now).Length==0&&TimeframeDirections.Aggregate(minutes.Take(5).Where((_,i)=>i!=2),5,now).Length==0&&TimeframeDirections.Aggregate(minutes.Take(5),5,start.AddMinutes(4)).Length==0,"partial, gapped and unfinished higher-timeframe candles are excluded");
        var daily=Enumerable.Range(0,60).Select(i=>new PriceBar("AAPL",start.AddDays(i-65),100+i,102+i,99+i,101+i,100)).ToArray();
        var rows=TimeframeDirections.Calculate(minutes,daily,now);
        Check(rows.Select(r=>r.Timeframe).SequenceEqual(new[]{"1 min","5 min","15 min","30 min","1 hour","1 day"})&&rows.All(r=>r.Direction is "UP" or "STRONG_UP"),"all six timeframes independently estimate a known rising sequence");
        Check(rows.Single(r=>r.Minutes==60).Bars==60&&rows.Single(r=>r.Minutes==1440).Bars==60,"hour and daily rows count real candles rather than reusing minute indicators");
        var limited=TimeframeDirections.Calculate(minutes.Take(40).ToArray(),[],now);
        Check(limited.Single(r=>r.Minutes==60).Direction=="UNAVAILABLE"&&limited[^1].Direction=="UNAVAILABLE","longer timeframes stay unavailable until they have enough complete candles");
        using var candles=JsonDocument.Parse(JsonSerializer.Serialize(new{candles=new[]{new{datetime=start.AddHours(4).ToUnixTimeMilliseconds(),open=100,high=102,low=99,close=101,volume=100},new{datetime=start.AddDays(1).AddHours(4).ToUnixTimeMilliseconds(),open=100,high=102,low=99,close=101,volume=100}}}));
        var native=SchwabConnection.ParseDailyHistory(candles.RootElement,"AAPL",start.AddDays(1).AddHours(18));
        Check(native.Length==1,"native daily history excludes the current Eastern calendar day");
        var directory=Path.Combine(Path.GetTempPath(),"timeframe-check-"+Guid.NewGuid().ToString("N"));var store=new MarketStore(directory);var calls=0;
        var service=new TimeframeService(store,Path.Combine(directory,"daily"),(symbol,token)=>{calls++;return Task.FromResult(daily);});
        await service.Refresh("AAPL",CancellationToken.None);await service.Refresh("AAPL",CancellationToken.None);
        var reopened=new TimeframeService(store,Path.Combine(directory,"daily"),(_,_)=>throw new Exception("Cached daily history must survive restart"));
        Check(calls==1&&reopened.Get("AAPL")[^1].Bars==60&&reopened.Get("MSFT")[^1].Direction=="UNAVAILABLE","daily candles cache by symbol across repeated views and app restarts");
        var failure=new TimeframeService(store,Path.Combine(directory,"unavailable"),(_,_)=>throw new FormatException("Fixture unsupported daily symbol"));
        var unavailable=await failure.Refresh("/MESZ26",CancellationToken.None);
        Check(unavailable[^1].Direction=="UNAVAILABLE"&&unavailable[^1].Status.Contains("unavailable"),"unsupported daily history leaves an explicit unavailable daily row");
    }
}
