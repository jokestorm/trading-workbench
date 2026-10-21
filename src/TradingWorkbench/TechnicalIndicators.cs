namespace TradingWorkbench;

public record IndicatorValues(decimal? Ema9,decimal? Ema21,decimal? Sma20,decimal? Sma50,decimal? Rsi14,decimal? Macd,decimal? MacdSignal,decimal? MacdHistogram,decimal? BollingerUpper,decimal? BollingerLower,decimal? Atr14,decimal? RelativeVolume20,decimal? Support20,decimal? Resistance20);
public record IndicatorPoint(DateTimeOffset Time,decimal? Ema9,decimal? Ema21,decimal? UpperBand,decimal? LowerBand);
public record IndicatorSignal(string Name,string Direction,string Description);
public record TechnicalReport(string Version,int BarsUsed,int BarsBeforeGap,DateTimeOffset? AsOf,IndicatorValues? Values,IndicatorSignal[] Signals,IndicatorPoint[] Series);

public static class TechnicalIndicators
{
    // Latest 180 completed minutes, reset after a missing minute. SMA-seeded
    // EMA; Wilder smoothing for RSI/ATR; population deviation for BB(20,2).
    public static TechnicalReport Calculate(IEnumerable<PriceBar> source,int minutes=1,bool allowSessionGaps=false)
    {
        var all=source.OrderBy(b=>b.Time).TakeLast(180).ToArray();var start=0;
        for(var i=1;i<all.Length;i++)if(!allowSessionGaps&&all[i].Time-all[i-1].Time!=TimeSpan.FromMinutes(minutes))start=i;
        var bars=all.Skip(start).ToArray();var n=bars.Length;
        if(n==0)return new("technicals-v1",0,0,null,null,[],[]);
        var close=bars.Select(b=>b.Close).ToArray();
        decimal? Sma(int end,int length)=>end+1<length?null:close.Skip(end-length+1).Take(length).Average();
        decimal?[] Smooth(decimal?[] values,int length,bool wilder=false)
        {
            var output=new decimal?[values.Length];decimal? previous=null;var seed=new List<decimal>();var alpha=wilder?1m/length:2m/(length+1);
            for(var i=0;i<values.Length;i++)
            {
                if(values[i] is not decimal value)continue;
                if(previous is null){seed.Add(value);if(seed.Count==length)previous=seed.Average();}
                else previous+=alpha*(value-previous.Value);
                output[i]=previous;
            }
            return output;
        }
        var ema9=Smooth(close.Select(v=>(decimal?)v).ToArray(),9);var ema21=Smooth(close.Select(v=>(decimal?)v).ToArray(),21);
        var fast=Smooth(close.Select(v=>(decimal?)v).ToArray(),12);var slow=Smooth(close.Select(v=>(decimal?)v).ToArray(),26);
        var macd=fast.Zip(slow,(a,b)=>a-b).ToArray();var signal=Smooth(macd,9);
        var gains=new decimal?[n];var losses=new decimal?[n];var ranges=new decimal?[n];
        for(var i=1;i<n;i++){var change=close[i]-close[i-1];gains[i]=Math.Max(change,0);losses[i]=Math.Max(-change,0);ranges[i]=Math.Max(bars[i].High-bars[i].Low,Math.Max(Math.Abs(bars[i].High-close[i-1]),Math.Abs(bars[i].Low-close[i-1])));}
        var avgGain=Smooth(gains,14,true);var avgLoss=Smooth(losses,14,true);var atr=Smooth(ranges,14,true);
        IndicatorValues At(int i)
        {
            var mean=Sma(i,20);decimal? deviation=mean is null?null:(decimal)Math.Sqrt((double)close.Skip(i-19).Take(20).Average(v=>(v-mean.Value)*(v-mean.Value)));
            decimal? rsi=avgGain[i] is null?null:avgGain[i]==0&&avgLoss[i]==0?50:avgLoss[i]==0?100:100-100/(1+avgGain[i]/avgLoss[i]);
            var previous=bars.Take(i).TakeLast(20).ToArray();var volume=previous.Length==20?previous.Average(b=>(decimal)b.Volume):0;
            return new(ema9[i],ema21[i],mean,Sma(i,50),rsi,macd[i],signal[i],macd[i]-signal[i],mean+2*deviation,mean-2*deviation,atr[i],volume>0?bars[i].Volume/volume:null,previous.Length==20?previous.Min(b=>b.Low):null,previous.Length==20?previous.Max(b=>b.High):null);
        }
        var values=Enumerable.Range(0,n).Select(At).ToArray();var current=values[^1];var events=new List<IndicatorSignal>();
        if(n>1)
        {
            var previous=values[^2];
            void Cross(decimal? before,decimal? after,string name)
            {
                if(before is null||after is null)return;
                if(before<=0&&after>0)events.Add(new(name,"UP","Bullish cross on the latest closed minute"));
                if(before>=0&&after<0)events.Add(new(name,"DOWN","Bearish cross on the latest closed minute"));
            }
            Cross(previous.Ema9-previous.Ema21,current.Ema9-current.Ema21,"EMA 9/21");
            Cross(previous.MacdHistogram,current.MacdHistogram,"MACD");
            if(current.Rsi14 is >=70)events.Add(new("RSI 14","NEUTRAL","Overbought reading; not an automatic sell"));
            if(current.Rsi14 is <=30)events.Add(new("RSI 14","NEUTRAL","Oversold reading; not an automatic buy"));
            if(current.Resistance20 is decimal resistance&&close[^1]>resistance)events.Add(new("20-bar breakout","UP","Close above the preceding 20 bars' high"));
            if(current.Support20 is decimal support&&close[^1]<support)events.Add(new("20-bar breakdown","DOWN","Close below the preceding 20 bars' low"));
            if(current.RelativeVolume20 is >=1.5m)events.Add(new("Volume","NEUTRAL","Latest bar volume at least 1.5× the preceding 20-bar average"));
        }
        return new("technicals-v1",n,start,bars[^1].Time.AddMinutes(minutes),current,events.ToArray(),values.Select((v,i)=>new IndicatorPoint(bars[i].Time,v.Ema9,v.Ema21,v.BollingerUpper,v.BollingerLower)).ToArray());
    }
}
