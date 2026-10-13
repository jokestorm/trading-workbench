using System.Text.Json;
namespace TradingWorkbench;
public static class OutcomeChecks
{
    public static void Run(){
        void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
        var ready=DateTimeOffset.UtcNow.AddHours(-1);var run=new AnalysisRun(Guid.NewGuid().ToString("N"),"saved-market",ready,"fixture","fixture","v1",JsonSerializer.SerializeToElement(new{symbol="AAPL",asOf=ready.AddMinutes(-30),bars=Array.Empty<PriceBar>()}),new("s","a",["e"],["l"],["n"],"i"));
        var points=Enumerable.Range(-5,40).Select(i=>new FollowupPoint(ready.AddMinutes(i),100+i,101+i,99+i,ready.AddMinutes(i-1))).ToArray();
        var report=AnalysisOutcomes.Calculate(run,points,ready.AddMinutes(40));Check(report.ReferenceAt==ready&&report.ReferencePrice==100,"follow-up excludes price movement before analysis completion");
        Check(report.Windows[0].Change==5&&report.Windows[0].ChangePercent==5&&report.Windows[2].Change==30,"forward price changes use post-analysis reference and distinct horizons");
        Check(report.Windows[0].ObservedHigh==106&&report.Windows[0].ObservedLow==100&&report.Windows[0].CompleteMinuteCoverage,"observed extrema and complete minute coverage reconcile");
        var missing=AnalysisOutcomes.Calculate(run,points.Where(p=>p.Time!=ready.AddMinutes(2)).ToArray(),ready.AddMinutes(40));Check(!missing.Windows[0].CompleteMinuteCoverage&&missing.Windows[0].Status=="observed","a missing interior minute marks incomplete coverage without interpolation");
        var gap=AnalysisOutcomes.Calculate(run,points.Where(p=>p.Time<ready.AddMinutes(5)||p.Time>ready.AddMinutes(7)).ToArray(),ready.AddMinutes(40));Check(gap.Windows[0].Status=="missing-data"&&gap.Windows[0].Price is null,"missing horizon prices are not replaced by distant observations");
        Check(AnalysisOutcomes.Calculate(run,points,ready.AddMinutes(1)).Windows.All(w=>w.Status=="pending"),"future windows cannot use observations beyond evaluation time");
        var absent=AnalysisOutcomes.Calculate(run,points.Where(p=>p.Time<ready||p.Time>ready.AddMinutes(2)).ToArray(),ready.AddMinutes(40));Check(absent.ReferenceStatus=="missing-data","missing post-analysis reference remains unavailable");
        var quoteRun=run with{Snapshot=JsonSerializer.SerializeToElement(new{symbol="AAPL",asOf=ready,quotes=Array.Empty<MarketQuote>()})};Check(!AnalysisOutcomes.Calculate(quoteRun,points,ready.AddMinutes(40)).Windows[0].CompleteMinuteCoverage,"quote samples never claim complete OHLCV coverage");
        var rejected=false;try{AnalysisOutcomes.Calculate(run with{Kind="sample-market"},points,ready);}catch(FormatException){rejected=true;}Check(rejected,"synthetic analyses do not receive real-market follow-up outcomes");
        var directory=Path.Combine(Path.GetTempPath(),"outcome-store-"+Guid.NewGuid().ToString("N"));var market=new MarketStore(directory);market.SaveBars(points.Select(p=>new PriceBar("AAPL",p.Start!.Value,p.Price,p.High!.Value,p.Low!.Value,p.Price,10)).ToArray());Check(market.Followup("AAPL","bars",ready).All(p=>p.Time<=ready.AddMinutes(40))&&market.Followup("AAPL","bars",ready).Any(p=>p.Time==ready.AddMinutes(30)),"stored follow-up queries retain a bounded historical window");
    }
}
