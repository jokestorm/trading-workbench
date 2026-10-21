using System.Text.Json;

namespace TradingWorkbench;
public static class TechnicalIndicatorChecks
{
    public static void Run()
    {
        void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS "+message);}
        bool Near(decimal? value,decimal expected,decimal tolerance=.000001m)=>value.HasValue&&Math.Abs(value.Value-expected)<tolerance;
        var start=new DateTimeOffset(2026,10,20,14,0,0,TimeSpan.Zero);
        PriceBar Bar(int i,decimal close,long volume=100)=>new("AAPL",start.AddMinutes(i),close,close+1,close-1,close,volume);
        var bars=Enumerable.Range(0,60).Select(i=>Bar(i,100+i)).ToArray();var report=TechnicalIndicators.Calculate(bars);var v=report.Values!;
        Check(Near(v.Ema9,155)&&Near(v.Ema21,149)&&Near(v.Sma20,149.5m)&&Near(v.Sma50,134.5m),"SMA-seeded EMA and SMA periods match a known linear sequence");
        Check(Near(v.Rsi14,100)&&Near(v.Macd,7)&&Near(v.MacdSignal,7)&&Near(v.MacdHistogram,0)&&Near(v.Atr14,2),"RSI, MACD and Wilder ATR match a steady rising sequence");
        Check(Near(v.BollingerUpper,149.5m+2*(decimal)Math.Sqrt(33.25))&&Near(v.BollingerLower,149.5m-2*(decimal)Math.Sqrt(33.25)),"Bollinger bands use a 20-bar mean and two population standard deviations");
        var alternating=Enumerable.Range(0,15).Select(i=>Bar(i,100+i%2)).Append(Bar(15,102)).ToArray();var smoothed=TechnicalIndicators.Calculate(alternating).Values!;
        Check(Near(smoothed.Rsi14,56.6666666667m)&&Near(smoothed.Atr14,29m/14),"RSI and ATR apply Wilder smoothing after the initial 14 changes");
        var flat=Enumerable.Range(0,50).Select(i=>Bar(i,100,0)).ToArray();var f=TechnicalIndicators.Calculate(flat).Values!;
        Check(f.Rsi14==50&&f.RelativeVolume20 is null&&f.BollingerUpper==100&&f.BollingerLower==100,"flat prices and zero volume avoid misleading extremes and division by zero");
        var crossed=flat.Append(Bar(50,102,300)).ToArray();var signal=TechnicalIndicators.Calculate(crossed);
        Check(signal.Signals.Any(s=>s.Name=="EMA 9/21"&&s.Direction=="UP")&&signal.Signals.Any(s=>s.Name=="MACD"&&s.Direction=="UP")&&signal.Signals.Any(s=>s.Name=="20-bar breakout"),"latest completed-bar crossovers and prior-range breakouts are identified");
        Check(!TechnicalIndicators.Calculate(crossed.Append(Bar(51,102))).Signals.Any(s=>s.Name=="EMA 9/21"),"an old EMA crossover is not repeated as a new crossover");
        var volume=bars[..^1].Append(Bar(59,159,300));Check(TechnicalIndicators.Calculate(volume).Values!.RelativeVolume20==3,"relative volume compares only the preceding 20 bars");
        var gap=TechnicalIndicators.Calculate(bars.Append(Bar(70,200)));Check(gap.BarsUsed==1&&gap.BarsBeforeGap==60&&gap.Values!.Rsi14 is null&&gap.Values.Ema9 is null,"history gaps reset warm-up instead of inventing continuous minutes");
        Check(TechnicalIndicators.Calculate(bars.Take(14)).Values!.Rsi14 is null&&TechnicalIndicators.Calculate(bars.Take(33)).Values!.MacdSignal is null&&TechnicalIndicators.Calculate([]).Values is null,"insufficient history leaves indicator values unavailable");
        Check(TechnicalIndicators.Calculate(Enumerable.Range(0,250).Select(i=>Bar(i,100+i))).BarsUsed==180,"indicator calculations share the AI snapshot's 180-bar window");
        var store=new MarketStore(Path.Combine(Path.GetTempPath(),"indicator-check-"+Guid.NewGuid().ToString("N")));var now=DateTimeOffset.UtcNow;
        store.SaveBars(Enumerable.Range(0,60).Select(i=>new PriceBar("AAPL",now.AddMinutes(i-61),100,102,99,101,100)).ToArray());
        var snapshot=JsonSerializer.SerializeToElement(store.Snapshot("AAPL","bars",1),CodexAnalysisProvider.Json);
        Check(snapshot.GetProperty("technicals").GetProperty("values").GetProperty("rsi14").GetDecimal()==50,"AI snapshots include the same calculated technical readings as the monitor");
    }
}
