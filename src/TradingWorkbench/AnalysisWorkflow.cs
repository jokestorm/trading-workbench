using System.Text.Json;

namespace TradingWorkbench;

public record AnalysisJob(string Id,string Kind,string? Symbol,string State,string Message,DateTimeOffset StartedAt,DateTimeOffset UpdatedAt,string? ResultId=null)
{
    public bool Active=>State is "preparing" or "refreshing" or "analyzing" or "canceling";
}

// Owns admission, background lifetime and durable status. The HTTP request only
// admits work; its cancellation token never owns the admitted run.
public sealed class AnalysisWorkflow:IAsyncDisposable
{
    private readonly AnalysisService analysis;
    private readonly string directory;
    private readonly Func<AnalysisRequest,CancellationToken,Task<MarketQuote>> refresh;
    private readonly object sync=new();
    private AnalysisJob? current;
    private CancellationTokenSource? cancellation;
    private Task? running;
    private bool stopping;

    public AnalysisWorkflow(AnalysisService analysis,string directory,Func<AnalysisRequest,CancellationToken,Task<MarketQuote>> refresh)
    {
        this.analysis=analysis;this.directory=directory;this.refresh=refresh;Directory.CreateDirectory(directory);
        var jobs=Directory.GetFiles(directory,"*.json").Select(path=>JsonSerializer.Deserialize<AnalysisJob>(File.ReadAllText(path),CodexAnalysisProvider.Json)!).ToArray();
        foreach(var job in jobs.Where(j=>j.Active))
        {
            // A crash after saving the result but before its status must not
            // disguise a completed run or cause another subscription call.
            AnalysisRun? saved=null;try{saved=analysis.Find(job.Id);}catch(FormatException){}
            Write(job with{State=saved is null?"interrupted":"completed",Message=saved is null?"App restarted during analysis. Start a new analysis when ready.":"Analysis saved.",UpdatedAt=DateTimeOffset.UtcNow,ResultId=saved?.Id});
        }
        current=Directory.GetFiles(directory,"*.json").Select(path=>JsonSerializer.Deserialize<AnalysisJob>(File.ReadAllText(path),CodexAnalysisProvider.Json)!).OrderByDescending(j=>j.StartedAt).FirstOrDefault();
    }
    private void Write(AnalysisJob job)
    {
        var path=Path.Combine(directory,job.Id+".json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(job,CodexAnalysisProvider.Json));File.Move(path+".tmp",path,true);
    }
    private void Set(AnalysisJob job){Write(job);current=job;}
    public AnalysisJob? Current(){lock(sync)return current;}
    public AnalysisJob Start(AnalysisRequest request)
    {
        lock(sync)
        {
            if(stopping)throw new FormatException("The app is stopping. Try again after it restarts.");
            if(current is {Active:true})return current;
            if(request.Kind is not ("journal" or "saved-market"))throw new FormatException("Choose market or journal analysis.");
            if(request.Kind=="saved-market")
            {
                request=request with{Symbol=MarketStore.Symbol(request.Symbol),Position=DecisionRules.Position(request.Position)};
                if(request.Source is not ("bars" or "quotes")||request.Days is not (1 or 3 or 7))throw new FormatException("Choose bars or quotes and a 1, 3 or 7 day range.");
            }
            // Journal filters and optional notes are resolved before returning
            // admission, so later edits cannot change this run's population.
            var preview=request.Kind=="journal"?analysis.Preview(request):null;
            var now=DateTimeOffset.UtcNow;
            var job=new AnalysisJob(Guid.NewGuid().ToString("N"),request.Kind,request.Kind=="saved-market"?request.Symbol:null,"preparing","Preparing data…",now,now);
            Set(job);cancellation?.Dispose();cancellation=new CancellationTokenSource();
            running=Task.Run(()=>Run(job,request,preview,cancellation.Token));return job;
        }
    }
    private void Progress(string state,string message)
    {
        lock(sync){cancellation!.Token.ThrowIfCancellationRequested();Set(current! with{State=state,Message=message,UpdatedAt=DateTimeOffset.UtcNow});}
    }
    private async Task Run(AnalysisJob job,AnalysisRequest request,AnalysisPreview? preview,CancellationToken token)
    {
        try
        {
            if(request.Kind=="saved-market")
            {
                Progress("refreshing","Updating prices…");
                var quote=await refresh(request,token);token.ThrowIfCancellationRequested();
                if(quote.Symbol!=request.Symbol)throw new FormatException("Refreshed quote does not match the requested contract.");
                preview=analysis.Preview(request,quote);
            }
            Progress("analyzing","Analyzing… You can leave this page and return later.");
            var result=await analysis.Execute(preview!.Token,token,job.Id);
            lock(sync)
            {
                // Cancellation and committing the result have one ordering:
                // canceled work cannot later appear as a successful analysis.
                token.ThrowIfCancellationRequested();analysis.Save(result);
                Set(current! with{State="completed",Message="Analysis saved.",UpdatedAt=DateTimeOffset.UtcNow,ResultId=result.Id});
            }
        }
        catch(Exception error)
        {
            lock(sync)
            {
                var state=stopping?"interrupted":token.IsCancellationRequested?"canceled":"failed";
                var message=state=="interrupted"?"App stopped during analysis. Start a new analysis when ready.":state=="canceled"?"Analysis canceled.":error is FormatException?error.Message:"Analysis failed. Check the connection and try again.";
                // Keep status reads truthful even if the disk itself fails.
                current=current! with{State=state,Message=message,UpdatedAt=DateTimeOffset.UtcNow};
                try{Write(current);}catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }
    }
    public AnalysisJob Cancel(string id)
    {
        lock(sync)
        {
            if(current?.Id!=id)throw new FormatException("That analysis is no longer the active run. Check current progress.");
            if(current.Active){Set(current with{State="canceling",Message="Canceling analysis…",UpdatedAt=DateTimeOffset.UtcNow});cancellation!.Cancel();}
            return current;
        }
    }
    public async ValueTask DisposeAsync()
    {
        Task? task;lock(sync){stopping=true;cancellation?.Cancel();task=running;}
        if(task is not null)await task;
        cancellation?.Dispose();
    }
    public static void Map(WebApplication app,AnalysisWorkflow workflow)
    {
        app.MapGet("/api/ai/runs/current",()=>new{run=workflow.Current()});
        app.MapPost("/api/ai/runs",(AnalysisRequest request)=>{try{return Results.Json(workflow.Start(request),statusCode:202);}catch(FormatException e){return Results.BadRequest(new{message=e.Message});}});
        app.MapPost("/api/ai/runs/{id}/cancel",(string id)=>{try{return Results.Ok(workflow.Cancel(id));}catch(FormatException e){return Results.BadRequest(new{message=e.Message});}});
    }
}
