using System.Text.Json;
namespace TradingWorkbench;
public static class DecisionRules
{
    public static AnalysisPosition Position(AnalysisPosition? input){var p=input??new();if(p.State is not ("UNKNOWN" or "FLAT" or "LONG" or "SHORT"))throw new FormatException("Choose unknown, flat, long or short position.");if(p.State is "UNKNOWN" or "FLAT")return new(p.State);if(p.Quantity is null or <1 or >10000||p.AverageEntry is null or <=0)throw new FormatException("Enter current contracts and average entry for a long or short position.");return p;}
    public static AnalysisResult Apply(AnalysisResult result,JsonElement snapshot,DateTimeOffset now)
    {
        var d=result.Decision??throw new FormatException("AI did not return a trading recommendation. Try Analyze again.");
        var state=snapshot.GetProperty("position").GetProperty("state").GetString();string? blocked=null;
        if(state=="UNKNOWN")blocked="Current position is unknown. Set your position before requesting a buy or sell recommendation.";
        else if(!snapshot.TryGetProperty("bars",out _))blocked="Quote samples alone are insufficient for this day-trading recommendation.";
        else if(!snapshot.TryGetProperty("asOf",out var asOf)||(now-asOf.GetDateTimeOffset()).TotalSeconds>120)blocked="Market data is too old for a current buy/sell recommendation. Update prices and analyze again.";
        else if(!snapshot.TryGetProperty("latestQuote",out var quote)||quote.ValueKind!=JsonValueKind.Object||!quote.TryGetProperty("realtime",out var realtime)||realtime.ValueKind!=JsonValueKind.True||!quote.TryGetProperty("quoteTime",out var quoteTime)||quoteTime.ValueKind==JsonValueKind.Null||(now-quoteTime.GetDateTimeOffset()).TotalSeconds>120)blocked="A fresh realtime quote is required for a current buy/sell recommendation.";
        else if(d.Action is "BUY" or "SELL_SHORT"&&state!="FLAT"||d.Action=="SELL_CLOSE"&&state!="LONG"||d.Action=="BUY_COVER"&&state!="SHORT"||d.Action=="HOLD"&&state is not ("LONG" or "SHORT"))blocked="The proposed action does not match your stated position.";
        if(blocked is null&&d.Action is "BUY" or "SELL_SHORT"){
            if(d.Entry is null or <=0||d.Stop is null or <=0||d.Target is null or <=0||!(d.Action=="BUY"?d.Stop<d.Entry&&d.Entry<d.Target:d.Target<d.Entry&&d.Entry<d.Stop))blocked="The proposed entry, stop and target do not define a valid setup.";
            else if(snapshot.TryGetProperty("tickSize",out var tickValue)){var tick=tickValue.GetDecimal();if(new[]{d.Entry.Value,d.Stop.Value,d.Target.Value}.Any(v=>v%tick!=0))blocked="Proposed price levels do not match this instrument's tick size.";}
        }
        var guardCode=blocked is null?null:state=="UNKNOWN"?"position-unknown":blocked.StartsWith("Quote samples")?"history-required":blocked.StartsWith("Market data")?"bars-stale":blocked.StartsWith("A fresh")?"quote-unverified-or-stale":blocked.StartsWith("The proposed action")?"position-mismatch":blocked.StartsWith("The proposed entry")?"invalid-levels":"off-tick-levels";
        return blocked is null?result:result with{Summary="WAIT",Assessment=blocked,NextSteps=[state=="UNKNOWN"?"Set your current position, then analyze again.":"Update the market data and analyze again."],Invalidation="This result cannot support a current buy/sell action.",Decision=new("WAIT",blocked,null,null,null,true,GuardCode:guardCode)};
    }
}
