using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TradingWorkbench;

public record TimeframeDirection(string Timeframe,int Minutes,string Direction,string Ema,string Rsi,string Macd,int Bars,DateTimeOffset? AsOf,string Status);
public record DailyHistory(PriceBar[] Bars,DateTimeOffset FetchedAt,string? Error=null);

public static class TimeframeDirections
{
    public static PriceBar[] Aggregate(IEnumerable<PriceBar> source,int minutes,DateTimeOffset now)
    {
        var span=minutes*60000L;
        return source.GroupBy(b=>b.Time.ToUnixTimeMilliseconds()/span).OrderBy(g=>g.Key).Select(g=>g.OrderBy(b=>b.Time).ToArray())
            .Where(b=>b.Length==minutes&&b[0].Time.ToUnixTimeMilliseconds()%span==0&&b[^1].Time.AddMinutes(1)<=now&&b.Select(p=>p.Time).Distinct().Count()==minutes&&b[^1].Time-b[0].Time==TimeSpan.FromMinutes(minutes-1))
            .Select(b=>new PriceBar(b[0].Symbol,b[0].Time,b[0].Open,b.Max(p=>p.High),b.Min(p=>p.Low),b[^1].Close,b.Sum(p=>p.Volume))).ToArray();
    }
    public static TimeframeDirection[] Calculate(PriceBar[] minutes,PriceBar[] daily,DateTimeOffset now,string? dailyError=null)
    {
        var rows=new List<TimeframeDirection>();
        foreach(var period in new[]{1,5,15,30,60,1440})
        {
            var bars=period==1440?daily:Aggregate(minutes,period,now);
            var report=TechnicalIndicators.Calculate(bars,period,allowSessionGaps:period>1);var v=report.Values;
            var epsilon=Math.Max(.00000001m,(bars.LastOrDefault()?.Close??1)*.00000001m);
            string Vote(decimal? value)=>value is null?"—":Math.Abs(value.Value)<=epsilon?"NEUTRAL":value>0?"UP":"DOWN";
            var ema=Vote(v?.Ema9-v?.Ema21);
            var rsi=v?.Rsi14 is null?"—":v.Rsi14>55?"UP":v.Rsi14<45?"DOWN":"NEUTRAL";
            var macd=Vote(v?.MacdHistogram);
            var votes=new[]{ema,rsi,macd};var ready=!votes.Contains("—");var score=votes.Count(s=>s=="UP")-votes.Count(s=>s=="DOWN");
            var direction=!ready?"UNAVAILABLE":score==3?"STRONG_UP":score>0?"UP":score== -3?"STRONG_DOWN":score<0?"DOWN":votes.All(s=>s=="NEUTRAL")?"NEUTRAL":"MIXED";
            var historical=report.AsOf is null||now-report.AsOf>TimeSpan.FromMinutes(period+2);
            var status=!ready?$"Needs 34 completed bars ({report.BarsUsed}/34)":historical?"Historical":"Latest closed bars";
            if(period==1440&&dailyError is not null)status=dailyError+(ready?" · cached readings":"");
            rows.Add(new(period==1440?"1 day":period==60?"1 hour":period+" min",period,direction,ema,rsi,macd,report.BarsUsed,report.AsOf,status));
        }
        return rows.ToArray();
    }
}

// Daily candles are broker-supplied, never guessed from a partial intraday day.
public sealed class TimeframeService(MarketStore market,string directory,Func<string,CancellationToken,Task<PriceBar[]>> loadDaily)
{
    private readonly SemaphoreSlim gate=new(1,1);
    private static readonly TimeZoneInfo Eastern=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    private static DateOnly Date(DateTimeOffset time)=>DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time,Eastern).DateTime);
    private string FileFor(string symbol)=>Path.Combine(directory,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(symbol)))+".json");
    private DailyHistory Read(string symbol){var path=FileFor(symbol);return File.Exists(path)?JsonSerializer.Deserialize<DailyHistory>(File.ReadAllText(path),CodexAnalysisProvider.Json)!:new([],DateTimeOffset.MinValue);}
    public TimeframeDirection[] Get(string symbol)
    {
        symbol=MarketStore.Symbol(symbol);var state=Read(symbol);return TimeframeDirections.Calculate(market.Bars(symbol,7),state.Bars,DateTimeOffset.UtcNow,state.Error);
    }
    public async Task<TimeframeDirection[]> Refresh(string symbol,CancellationToken token)
    {
        symbol=MarketStore.Symbol(symbol);await gate.WaitAsync(token);
        try
        {
            var state=Read(symbol);var now=DateTimeOffset.UtcNow;
            if(Date(state.FetchedAt)!=Date(now)||(state.Error is not null&&now-state.FetchedAt>TimeSpan.FromMinutes(5)))
            {
                try{state=new(await loadDaily(symbol,token),now);}
                catch(Exception error) when(error is FormatException or HttpRequestException or TaskCanceledException)
                {
                    token.ThrowIfCancellationRequested();state=state with{FetchedAt=now,Error="Daily history unavailable from Schwab"};
                }
                token.ThrowIfCancellationRequested();Directory.CreateDirectory(directory);var path=FileFor(symbol);File.WriteAllText(path+".tmp",JsonSerializer.Serialize(state,CodexAnalysisProvider.Json));File.Move(path+".tmp",path,true);
            }
            return Get(symbol);
        }
        finally{gate.Release();}
    }
    public static void Map(WebApplication app,TimeframeService service)
    {
        app.MapGet("/api/market/timeframes",(string symbol)=>{try{return Results.Ok(service.Get(symbol));}catch(FormatException e){return Results.BadRequest(new{message=e.Message});}});
        app.MapPost("/api/market/timeframes/refresh",async(HistoryRequest request,HttpContext context)=>{try{return Results.Ok(await service.Refresh(request.Symbol,context.RequestAborted));}catch(FormatException e){return Results.BadRequest(new{message=e.Message});}});
    }
}
