using System.Text.Json;

namespace TradingWorkbench;

public static class AnalyticsChecks
{
    public static void Run()
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
        var input=new[]{
            new TradeData{Id="a",Date="2026-10-01",Time="10:00",Symbol="MESZ26",AccountingGrossPnl=100,Fees=10,Risk=45},
            new TradeData{Id="b",Date="2026-10-02",Time="10:00",Symbol="MESZ26",AccountingGrossPnl=-50,Fees=5,Risk=55},
            new TradeData{Id="c",Date="2026-10-02",Time="11:00",Symbol="AAPL",AccountingGrossPnl=0,Fees=0,Risk=null}};
        JsonElement Summary(TradeData[] trades)=>JsonSerializer.SerializeToElement(Analytics.Calculate(trades));
        var result=Summary(new TradeFilter().Apply(input));
        Check(result.GetProperty("gross").GetDecimal()==50&&result.GetProperty("fees").GetDecimal()==15&&result.GetProperty("gross").GetDecimal()-result.GetProperty("fees").GetDecimal()==result.GetProperty("net").GetDecimal(),"gross trading P/L and separate fees reconcile to net P/L");
        Check(!result.TryGetProperty("setups",out _)&&!Analytics.Export(input).Split('\n')[0].Contains("Setup"),"journal reports and CSV exports omit removed setup categories");
        Check(result.GetProperty("curve").EnumerateArray().Select(p=>p.GetProperty("Time").GetString()).SequenceEqual(new[]{"10:00","10:00","11:00"}),"journal curve retains Eastern closing times for time-axis zoom");
        Check(result.GetProperty("net").GetDecimal()==35&&result.GetProperty("expectancy").GetDecimal()==35m/3&&result.GetProperty("breakEven").GetInt32()==1,"expectancy and win rate include break-even trades");
        Check(result.GetProperty("daily").EnumerateArray().Sum(d=>d.GetProperty("Net").GetDecimal())==35&&result.GetProperty("instruments").EnumerateArray().Sum(d=>d.GetProperty("Net").GetDecimal())==35,"daily and instrument breakdowns reconcile");
        Check(result.GetProperty("drawdown").GetDecimal()==55&&result.GetProperty("currentDrawdown").GetDecimal()==55,"drawdown starts from zero and tracks the realized peak");
        Check(result.GetProperty("riskCount").GetInt32()==2&&result.GetProperty("totalR").GetDecimal()==1&&result.GetProperty("medianR").GetDecimal()==0.5m,"R statistics exclude missing risk with explicit coverage");
        Check(result.GetProperty("distribution").EnumerateArray().Sum(b=>b.GetProperty("Count").GetInt32())==3,"distribution includes every selected trade exactly once");
        var selected=new TradeFilter("2026-10-02","2026-10-02",Symbol:"MESZ26").Apply(input);Check(selected.Length==1&&selected[0].Id=="b","inclusive combined filters select the intended population");
        var empty=Summary([]);Check(empty.GetProperty("winRate").ValueKind==JsonValueKind.Null&&empty.GetProperty("totalR").ValueKind==JsonValueKind.Null,"empty metrics remain unavailable");
        Check(Summary([input[0]]).GetProperty("profitFactorIsInfinite").GetBoolean(),"profit factor without losses is distinguished from unavailable");
        var export=Analytics.Export([input[0] with{Notes="=SUM(A1)\nquoted \"text\""}]);var csv=CsvReader.Parse(export);Check(csv.Count==2&&csv[1][14]=="'=SUM(A1)\nquoted \"text\""&&csv[1][11]=="90","export preserves quoted notes and protects spreadsheet formulas");
        var rejected=false;try{new TradeFilter("2026-10-03","2026-10-01").Apply(input);}catch(FormatException){rejected=true;}Check(rejected,"invalid ranges cannot export misleading results");
        Console.WriteLine("All analytics checks passed.");
    }
}
