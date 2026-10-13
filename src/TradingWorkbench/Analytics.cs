using System.Globalization;

namespace TradingWorkbench;

public record TradeFilter(string? Start=null,string? End=null,string? Account=null,string? Symbol=null,string? Direction=null,string? Setup=null)
{
    public static TradeFilter Read(IQueryCollection query)=>new(query["start"],query["end"],query["account"],query["symbol"],query["direction"],query["setup"]);
    public TradeData[] Apply(IEnumerable<TradeData> trades)
    {
        foreach(var date in new[]{Start,End})if(!string.IsNullOrEmpty(date)&&!DateOnly.TryParseExact(date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _))throw new FormatException("Use a valid yyyy-MM-dd date.");
        if(!string.IsNullOrEmpty(Start)&&!string.IsNullOrEmpty(End)&&string.CompareOrdinal(Start,End)>0)throw new FormatException("Start date must be on or before end date.");
        if(!string.IsNullOrEmpty(Direction)&&Direction is not ("LONG" or "SHORT"))throw new FormatException("Direction must be LONG or SHORT.");
        return trades.Where(t=>(string.IsNullOrEmpty(Start)||string.CompareOrdinal(t.Date,Start)>=0)&&(string.IsNullOrEmpty(End)||string.CompareOrdinal(t.Date,End)<=0)
            &&(string.IsNullOrEmpty(Account)||t.Account==Account)&&(string.IsNullOrEmpty(Symbol)||t.Symbol==Symbol)&&(string.IsNullOrEmpty(Direction)||t.Direction==Direction)&&(string.IsNullOrEmpty(Setup)||t.Setup==Setup))
            .OrderBy(t=>t.Date).ThenBy(t=>t.Time).ThenBy(t=>t.Id).ToArray();
    }
}
public record CurvePoint(string Date,decimal Value,decimal Drawdown);
public record Breakdown(string Key,int Count,decimal Net,decimal Fees,decimal Expectancy,decimal WinRate);
public record DistributionBucket(string Label,string Range,int Count,string Kind);
public static class Analytics
{
    public static object Calculate(TradeData[] trades)
    {
        var values=trades.Select(t=>t.NetPnl).ToArray();var winners=values.Where(v=>v>0).ToArray();var losers=values.Where(v=>v<0).ToArray();
        var net=values.Sum();var profits=winners.Sum();var losses=-losers.Sum();decimal running=0,peak=0,maxDrawdown=0;
        var curve=trades.Select(t=>{running+=t.NetPnl;peak=Math.Max(peak,running);var dd=peak-running;maxDrawdown=Math.Max(maxDrawdown,dd);return new CurvePoint(t.Date,running,dd);}).ToArray();
        Breakdown[] Group(Func<TradeData,string> key)=>trades.GroupBy(key).Select(g=>new Breakdown(g.Key,g.Count(),g.Sum(t=>t.NetPnl),g.Sum(t=>t.Fees),g.Average(t=>t.NetPnl),(decimal)g.Count(t=>t.NetPnl>0)/g.Count())).ToArray();
        var r=trades.Where(t=>t.Risk>0).Select(t=>t.NetPnl/t.Risk!.Value).Order().ToArray();
        decimal? medianR=r.Length==0?null:r.Length%2==1?r[r.Length/2]:(r[r.Length/2-1]+r[r.Length/2])/2;
        var labels=new[]{"< −1k","−1k…−250","−250…−100","−100…0","$0","0…100","100…250","250…1k","> 1k"};
        var ranges=new[]{"P/L < -$1,000","-$1,000 ≤ P/L < -$250","-$250 ≤ P/L < -$100","-$100 ≤ P/L < $0","P/L = $0","$0 < P/L ≤ $100","$100 < P/L ≤ $250","$250 < P/L ≤ $1,000","P/L > $1,000"};
        var counts=new int[9];foreach(var v in values)counts[v< -1000?0:v< -250?1:v< -100?2:v<0?3:v==0?4:v<=100?5:v<=250?6:v<=1000?7:8]++;
        return new{net,count=trades.Length,wins=winners.Length,losses=losers.Length,breakEven=values.Count(v=>v==0),winRate=trades.Length==0?(decimal?)null:(decimal)winners.Length/trades.Length,
            expectancy=trades.Length==0?(decimal?)null:net/trades.Length,profitFactor=losses>0?(decimal?)profits/losses:null,profitFactorIsInfinite=losses==0&&profits>0,
            averageWinner=winners.Length==0?(decimal?)null:winners.Average(),averageLoser=losers.Length==0?(decimal?)null:losers.Average(),largestWinner=winners.Length==0?(decimal?)null:winners.Max(),largestLoser=losers.Length==0?(decimal?)null:losers.Min(),
            fees=trades.Sum(t=>t.Fees),drawdown=maxDrawdown,currentDrawdown=peak-running,curve,
            daily=Group(t=>t.Date).OrderBy(g=>g.Key),instruments=Group(t=>t.Symbol).OrderByDescending(g=>g.Net),setups=Group(t=>t.Setup).OrderByDescending(g=>g.Net),
            distribution=labels.Select((label,i)=>new DistributionBucket(label,ranges[i],counts[i],i<4?"loss":i==4?"flat":"win")),
            riskCount=r.Length,riskCoverage=trades.Length==0?(decimal?)null:(decimal)r.Length/trades.Length,totalR=r.Length==0?(decimal?)null:r.Sum(),averageR=r.Length==0?(decimal?)null:r.Average(),medianR};
    }
    public static string Export(TradeData[] trades)
    {
        string Text(string value){if(value.TrimStart().StartsWith('=')||value.TrimStart().StartsWith('+')||value.TrimStart().StartsWith('-')||value.TrimStart().StartsWith('@'))value="'"+value;return '"'+value.Replace("\"","\"\"")+'"';}
        string Number(decimal? value)=>value?.ToString("G29",CultureInfo.InvariantCulture)??"";
        var rows=new List<string>{"Account,Instrument,Direction,Opened at ET,Closed at ET,Quantity,Average entry,Average exit,Point value,Gross P/L,Fees,Net P/L,Planned risk,R multiple,Setup,Notes,Source"};
        rows.AddRange(trades.Select(t=>string.Join(',',new[]{Text(t.Account),Text(t.Symbol),Text(t.Direction),Text(t.OpenedAt),Text($"{t.Date}T{t.Time}"),Number(t.Quantity),Number(t.Entry),Number(t.Exit),Number(t.Multiplier),Number(t.GrossPnl),Number(t.Fees),Number(t.NetPnl),Number(t.Risk),Number(t.Risk>0?t.NetPnl/t.Risk.Value:null),Text(t.Setup),Text(t.Notes),Text(t.Source)})));
        return string.Join("\r\n",rows)+"\r\n";
    }
}
