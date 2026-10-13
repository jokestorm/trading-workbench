using System.Text.Json;
namespace TradingWorkbench;
public record FollowupPoint(DateTimeOffset Time,decimal Price,decimal? High=null,decimal? Low=null,DateTimeOffset? Start=null,bool? Realtime=null);
public record FollowupWindow(int Minutes,string Status,DateTimeOffset? Target,DateTimeOffset? ObservedAt,decimal? Price,decimal? Change,decimal? ChangePercent,decimal? ObservedHigh,decimal? ObservedLow,int Observations,bool CompleteMinuteCoverage);
public record FollowupReport(string AnalysisId,string Symbol,string Source,DateTimeOffset AnalysisAvailableAt,DateTimeOffset SnapshotAsOf,DateTimeOffset? ReferenceAt,decimal? ReferencePrice,string ReferenceStatus,FollowupWindow[] Windows,string Note);
public static class AnalysisOutcomes
{
    public static (string Symbol,string Source,DateTimeOffset SnapshotAsOf) Metadata(AnalysisRun run)
    {
        if(run.Kind!="saved-market")throw new FormatException("Follow-up is available for saved Schwab market analyses only.");
        try{return(MarketStore.Symbol(run.Snapshot.GetProperty("symbol").GetString()),run.Snapshot.TryGetProperty("bars",out _)?"bars":"quotes",run.Snapshot.GetProperty("asOf").GetDateTimeOffset());}
        catch(Exception e) when(e is KeyNotFoundException or InvalidOperationException or FormatException){throw new FormatException("This analysis has no supported market snapshot metadata.");}
    }
    public static FollowupReport Calculate(AnalysisRun run,FollowupPoint[] points,DateTimeOffset now)
    {
        var (symbol,source,asOf)=Metadata(run);
        // Prices before the result existed cannot serve as an actionable follow-up reference.
        var reference=points.Where(p=>p.Time>=run.CreatedAt&&p.Time<=run.CreatedAt.AddMinutes(2)&&p.Time<=now).OrderBy(p=>p.Time).FirstOrDefault();
        var referenceStatus=reference is not null?"observed":now<run.CreatedAt.AddMinutes(2)?"pending":"missing-data";
        var windows=new[]{5,15,30}.Select(minutes=>{
            if(reference is null)return new FollowupWindow(minutes,referenceStatus,null,null,null,null,null,null,null,0,false);
            var target=reference.Time.AddMinutes(minutes);
            var exit=points.Where(p=>p.Time>=target&&p.Time<=target.AddMinutes(2)&&p.Time<=now).OrderBy(p=>p.Time).FirstOrDefault();
            if(exit is null)return new FollowupWindow(minutes,now<target?"pending":"missing-data",target,null,null,null,null,null,null,0,false);
            var observed=points.Where(p=>p.Time>reference.Time&&p.Time<=exit.Time&&(!p.Start.HasValue||p.Start>=reference.Time)).OrderBy(p=>p.Time).ToArray();
            var expected=(int)Math.Round((exit.Time-reference.Time).TotalMinutes);
            var complete=source=="bars"&&observed.Select(p=>p.Time).Distinct().Count()==expected&&observed.All(p=>p.Start.HasValue&&p.Time-p.Start.Value==TimeSpan.FromMinutes(1))&&observed.Select((p,i)=>p.Time==reference.Time.AddMinutes(i+1)).All(v=>v);
            var change=exit.Price-reference.Price;
            return new FollowupWindow(minutes,"observed",target,exit.Time,exit.Price,change,reference.Price==0?null:change/Math.Abs(reference.Price)*100,observed.Length>0?observed.Max(p=>p.High??p.Price):null,observed.Length>0?observed.Min(p=>p.Low??p.Price):null,observed.Length,complete);
        }).ToArray();
        return new(run.Id,symbol,source,run.CreatedAt,asOf,reference?.Time,reference?.Price,referenceStatus,windows,"Reference is the first saved price at/after analysis completion, within two minutes. Each horizon uses the first saved price at/after its target, within two minutes. Gaps are never interpolated. Observed extrema cover supplied data only; quote samples may be delayed. These are price observations, not executed trades, fees, or a measured strategy win rate.");
    }
}
