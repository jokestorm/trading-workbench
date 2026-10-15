using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TradingWorkbench;
public record AnalysisPosition(string State="UNKNOWN",int? Quantity=null,decimal? AverageEntry=null);
public record TradeDecision(string Action,string Reason,decimal? Entry,decimal? Stop,decimal? Target,bool Guarded=false,string? Trend=null);
public record AnalysisRequest(string Kind,TradeFilter? Filters=null,bool IncludeNotes=false,string? Symbol=null,string Source="bars",int Days=7,AnalysisPosition? Position=null);
public record AnalysisPreview(string Token,string Kind,DateTimeOffset ExpiresAt,JsonElement Snapshot);
public record AnalysisResult(string Summary,string Assessment,string[] Evidence,string[] Limitations,string[] NextSteps,string Invalidation,TradeDecision? Decision=null);
public record AnalysisRun(string Id,string Kind,DateTimeOffset CreatedAt,string Provider,string Model,string PromptVersion,JsonElement Snapshot,AnalysisResult Result);
public interface IAnalysisProvider { Task<AnalysisResult> Analyze(JsonElement snapshot,string directory,CancellationToken token); }
public record CodexStatus(bool Installed,bool SubscriptionLogin,string Message);

public sealed class CodexAnalysisProvider(string? executable=null,string? home=null,Func<ProcessStartInfo,string?,CancellationToken,Task<(int Exit,string Output)>>? runProcess=null,ILogger<CodexAnalysisProvider>? logger=null):IAnalysisProvider
{
    private readonly ILogger<CodexAnalysisProvider> log=logger??Microsoft.Extensions.Logging.Abstractions.NullLogger<CodexAnalysisProvider>.Instance;
    private readonly SemaphoreSlim operation=new(1,1);
    public IDisposable EnterOperation()
    {
        if(!operation.Wait(0))throw new FormatException("AI is busy signing in or analyzing. Wait for it to finish.");
        return new OperationLease(operation);
    }
    private sealed class OperationLease(SemaphoreSlim gate):IDisposable{public void Dispose()=>gate.Release();}
    public string Model {get;private set;}="CLI default";
    public static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true};
    public static string? FindExecutable()=> (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator).Select(p=>Path.Combine(p.Trim('"'),OperatingSystem.IsWindows()?"codex.exe":"codex")).FirstOrDefault(File.Exists);
    public static async Task<(int Exit,string Output)> RunProcess(ProcessStartInfo start,string? input,CancellationToken token)
    {
        using var process=new Process{StartInfo=start};
        try{process.Start();}catch(System.ComponentModel.Win32Exception){throw new FormatException("Codex CLI could not start. Install it or configure CodexExecutable, then restart this app.");}
        var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        try{if(input is not null){await process.StandardInput.WriteAsync(input.AsMemory(),token);process.StandardInput.Close();}await process.WaitForExitAsync(token);var errorOutput=await stderr;return(process.ExitCode,(await stdout)+errorOutput);}
        catch(OperationCanceledException){if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();throw;}
    }
    internal ProcessStartInfo Start(params string[] arguments)
    {
        var start=new ProcessStartInfo(executable??FindExecutable()??throw new FormatException("Codex CLI was not found on PATH. Restart after installing it.")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var key in new[]{"OPENAI_API_KEY","CODEX_API_KEY","CODEX_ACCESS_TOKEN"})start.Environment.Remove(key);
        if(!string.IsNullOrWhiteSpace(home))
        {
            var directory=Path.GetFullPath(home);Directory.CreateDirectory(directory);
            start.Environment["CODEX_HOME"]=directory;start.WorkingDirectory=directory;
            start.ArgumentList.Add("-c");start.ArgumentList.Add("cli_auth_credentials_store=\"file\"");
        }
        start.ArgumentList.Add("-c");start.ArgumentList.Add("forced_login_method=\"chatgpt\"");
        start.ArgumentList.Add("-c");start.ArgumentList.Add("model_provider=\"openai\"");
        foreach(var argument in arguments)start.ArgumentList.Add(argument);return start;
    }
    public async Task<CodexStatus> Status()
    {
        try {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));var result=await (runProcess??RunProcess)(Start("login","status"),null,timeout.Token);var connected=result.Exit==0&&result.Output.Contains("ChatGPT",StringComparison.OrdinalIgnoreCase);return new(true,connected,connected?"ChatGPT connected.":"Sign in with ChatGPT to enable analysis.");}
        catch(Exception error) when(error is FormatException or OperationCanceledException){return new(executable is not null||FindExecutable() is not null,false,"Codex is unavailable. Install the latest Trading Workbench app update, then check again.");}
    }
    public async Task Logout(){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));var result=await (runProcess??RunProcess)(Start("logout"),null,timeout.Token);if(result.Exit!=0)throw new FormatException("Could not sign out of Codex. Try again.");}
    public async Task<AnalysisResult> Analyze(JsonElement snapshot,string directory,CancellationToken token)
    {
        using var lease=EnterOperation();
        var schemaPath=Path.Combine(directory,"schema.json");var resultPath=Path.Combine(directory,"result.json");
        var schema=new JsonObject{["type"]="object",["additionalProperties"]=false};var properties=new JsonObject();
        foreach(var key in new[]{"summary","assessment","invalidation"})properties[key]=new JsonObject{["type"]="string"};
        foreach(var key in new[]{"evidence","limitations","nextSteps"})properties[key]=new JsonObject{["type"]="array",["items"]=new JsonObject{["type"]="string"}};
        var policy=AnalysisPrompts.For(snapshot);
        var market=snapshot.TryGetProperty("position",out _);
        properties["decision"]=market?JsonNode.Parse("""{"type":"object","additionalProperties":false,"properties":{"trend":{"type":"string","enum":["UP","DOWN","SIDEWAYS","UNCLEAR"]},"action":{"type":"string","enum":["BUY","SELL_SHORT","SELL_CLOSE","BUY_COVER","HOLD","WAIT"]},"reason":{"type":"string","maxLength":180},"entry":{"type":["number","null"]},"stop":{"type":["number","null"]},"target":{"type":["number","null"]}},"required":["trend","action","reason","entry","stop","target"]}"""):new JsonObject{["type"]="null"};
        schema["properties"]=properties;schema["required"]=new JsonArray(properties.Select(p=>(JsonNode?)JsonValue.Create(p.Key)).ToArray());
        await File.WriteAllTextAsync(schemaPath,schema.ToJsonString(),token);
        var start=Start("exec","--ignore-user-config","--ignore-rules","--ephemeral","--skip-git-repo-check","--sandbox","read-only","--color","never","-c","forced_login_method=\"chatgpt\"","-c","model_provider=\"openai\"","-c",policy.AllowResearch?"web_search=\"live\"":"web_search=\"disabled\"","-c","features.shell_tool=false","-c","features.remote_plugin=false","--output-schema",schemaPath,"--output-last-message",resultPath,"-");start.WorkingDirectory=directory;
        var runId=Path.GetFileName(directory);var elapsed=Stopwatch.StartNew();
        log.LogInformation("Analysis {RunId} Codex starting research={Research} limitSeconds={Limit}",runId,policy.AllowResearch,policy.TimeLimit.TotalSeconds);
        (int Exit,string Output) response;
        try{response=await (runProcess??RunProcess)(start,policy.Text,token);}
        catch(Exception error)
        {
            log.LogWarning("Analysis {RunId} Codex stopped errorType={ErrorType} elapsedSeconds={Elapsed:F1}",runId,error.GetType().Name,elapsed.Elapsed.TotalSeconds);throw;
        }
        log.LogInformation("Analysis {RunId} Codex exited exitCode={ExitCode} resultPresent={ResultPresent} elapsedSeconds={Elapsed:F1}",runId,response.Exit,File.Exists(resultPath),elapsed.Elapsed.TotalSeconds);
        if(response.Exit!=0)
        {
            var failure=response.Output.Contains("429",StringComparison.OrdinalIgnoreCase)||response.Output.Contains("usage limit",StringComparison.OrdinalIgnoreCase)?"rate-or-usage-limit":response.Output.Contains("401",StringComparison.OrdinalIgnoreCase)||response.Output.Contains("unauthorized",StringComparison.OrdinalIgnoreCase)?"authentication":response.Output.Contains("reconnect",StringComparison.OrdinalIgnoreCase)||response.Output.Contains("connection",StringComparison.OrdinalIgnoreCase)?"connection":"unclassified";
            log.LogWarning("Analysis {RunId} Codex failureCategory={FailureCategory}; raw output withheld",runId,failure);
        }
        var model=System.Text.RegularExpressions.Regex.Match(response.Output,@"(?m)^model:\s*([^\r\n]+)");Model=model.Success?model.Groups[1].Value.Trim():"CLI default (not reported)";
        if(response.Exit!=0||!File.Exists(resultPath))throw new FormatException("Codex analysis did not complete. Check ChatGPT login and subscription limits, then try again.");
        try {return Validate(await File.ReadAllTextAsync(resultPath,token));}catch(JsonException){throw new FormatException("Codex returned invalid structured output. No analysis was saved.");}
    }
    public static AnalysisResult Validate(string json)
    {
        var result=JsonSerializer.Deserialize<AnalysisResult>(json,Json)??throw new FormatException("Missing analysis result.");
        if(result.Decision?.Trend is not (null or "UP" or "DOWN" or "SIDEWAYS" or "UNCLEAR"))throw new FormatException("Invalid trend direction. No analysis was saved.");
        if(new[]{result.Summary,result.Assessment,result.Invalidation}.Any(s=>string.IsNullOrWhiteSpace(s)||s.Length>6000)||new[]{result.Evidence,result.Limitations,result.NextSteps}.Any(a=>a is null||a.Length is <1 or >10||a.Any(s=>string.IsNullOrWhiteSpace(s)||s.Length>2000)))throw new FormatException("Codex returned incomplete analysis. No analysis was saved.");
        if(result.Decision is {} d&&(d.Action is not ("BUY" or "SELL_SHORT" or "SELL_CLOSE" or "BUY_COVER" or "HOLD" or "WAIT")||string.IsNullOrWhiteSpace(d.Reason)||d.Reason.Length>2000))throw new FormatException("Invalid trading recommendation. No analysis was saved.");return result;
    }
}

public sealed class AnalysisService
{
    private readonly TradeStore trades;private readonly IAnalysisProvider provider;private readonly string directory;
    private readonly Dictionary<string,AnalysisPreview> previews=new();private readonly SemaphoreSlim gate=new(1,1);
    private readonly MarketStore? market;
    public AnalysisService(TradeStore trades,IAnalysisProvider provider,string directory,MarketStore? market=null){this.trades=trades;this.provider=provider;this.directory=directory;this.market=market;Directory.CreateDirectory(directory);}
    public AnalysisPreview Preview(AnalysisRequest request,MarketQuote? refreshedQuote=null)
    {
        object snapshot;
        if(request.Kind=="journal"){
            var selected=(request.Filters??new()).Apply(trades.List());if(selected.Length==0)throw new FormatException("No saved trades match this range.");if(selected.Length>250)throw new FormatException("Narrow the range to at most 250 trades for this first version.");
            snapshot=new{kind="journal",provenance="Saved closed trades; timestamps Eastern; USD; no intratrade market path",createdAt=DateTimeOffset.UtcNow,summary=Analytics.Calculate(selected),notesIncluded=request.IncludeNotes,trades=selected.Select((t,i)=>new{label=$"Trade {i+1}",t.Symbol,t.Date,t.Time,t.Direction,t.Quantity,t.NetPnl,t.Fees,t.Risk,t.Setup,notes=request.IncludeNotes?t.Notes[..Math.Min(t.Notes.Length,500)]:null})};
        }else if(request.Kind=="sample-market")snapshot=SampleMarket();else if(request.Kind=="saved-market"){
            var data=JsonSerializer.SerializeToNode((market??throw new FormatException("Market storage is unavailable.")).Snapshot(request.Symbol??"",request.Source,request.Days),CodexAnalysisProvider.Json)!.AsObject();
            var position=DecisionRules.Position(request.Position);
            data["position"]=JsonSerializer.SerializeToNode(new{position.State,position.Quantity,position.AverageEntry,source="Manual input for this dated contract; not broker verified",providedAt=DateTimeOffset.UtcNow},CodexAnalysisProvider.Json);
            var symbol=MarketStore.Symbol(request.Symbol);var root=symbol.TrimStart('/');var instrument=InstrumentCatalog.All.FirstOrDefault(i=>root.StartsWith(i.Root,StringComparison.Ordinal));
            data["pointValue"]=instrument?.PointValue;data["tickSize"]=root.StartsWith("MES")?.25m:root.StartsWith("MGC")?.1m:(root.StartsWith("MCL")||root.StartsWith("CL"))?.01m:root.StartsWith("MET")?.5m:1m;
            data["latestQuote"]=JsonSerializer.SerializeToNode(refreshedQuote??market.Quotes(symbol).LastOrDefault(),CodexAnalysisProvider.Json);
            snapshot=data;
        }else throw new FormatException("Choose journal, saved-market or sample-market analysis.");
        var preview=new AnalysisPreview(Guid.NewGuid().ToString("N"),request.Kind,DateTimeOffset.UtcNow.AddMinutes(20),JsonSerializer.SerializeToElement(snapshot,CodexAnalysisProvider.Json));
        lock(previews){foreach(var expired in previews.Where(p=>p.Value.ExpiresAt<DateTimeOffset.UtcNow).Select(p=>p.Key).ToArray())previews.Remove(expired);if(previews.Count>=10)previews.Remove(previews.Keys.First());previews[preview.Token]=preview;}return preview;
    }
    public static object SampleMarket()
    {
        var prices=new decimal[]{100,100.5m,100.25m,101,101.5m,101.25m,101.75m,102,101.5m,101.25m,102,102.5m,102.25m,102.75m,103,102.5m,102.25m,102.75m,103.25m,103};
        var bars=prices.Select((close,i)=>new{timeEastern=$"2026-10-06T09:{30+i:00}:00",open=i==0?99.75m:prices[i-1],high=Math.Max(i==0?99.75m:prices[i-1],close)+.25m,low=Math.Min(i==0?99.75m:prices[i-1],close)-.25m,close,volume=100+i*7}).ToArray();
        return new{kind="market",provenance="SYNTHETIC fixture, not observed market prices. For workflow testing only.",symbol="DEMO",timeframe="1 minute",asOfEastern=bars[^1].timeEastern,bars,features=new{changeFromFirstClose=prices[^1]-prices[0],lastClose=prices[^1],fiveBarMean=prices.TakeLast(5).Average(),rangeHigh=bars.Max(b=>b.high),rangeLow=bars.Min(b=>b.low)}};
    }
    public async Task<AnalysisRun> Analyze(string token,CancellationToken cancellation)
    {
        var run=await Execute(token,cancellation);cancellation.ThrowIfCancellationRequested();Save(run);return run;
    }
    internal async Task<AnalysisRun> Execute(string token,CancellationToken cancellation,string? runId=null)
    {
        if(!await gate.WaitAsync(0,cancellation))throw new FormatException("An analysis is already running. Wait for it to finish.");
        try{
            AnalysisPreview preview;lock(previews){if(string.IsNullOrEmpty(token)||!previews.TryGetValue(token,out preview!)||preview.ExpiresAt<DateTimeOffset.UtcNow)throw new FormatException("Preview expired. Preview the data again.");previews.Remove(token);}
            var id=runId??Guid.NewGuid().ToString("N");var runDirectory=Path.Combine(directory,"runs",id);Directory.CreateDirectory(runDirectory);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);var policy=AnalysisPrompts.For(preview.Snapshot);timeout.CancelAfter(policy.TimeLimit);
            AnalysisResult result;try{result=await provider.Analyze(preview.Snapshot,runDirectory,timeout.Token);}catch(OperationCanceledException){throw new FormatException($"Analysis was canceled or reached its {policy.TimeLimit.TotalSeconds:0}-second limit. Start a new analysis to retry.");}
            if(preview.Kind=="saved-market")result=DecisionRules.Apply(result,preview.Snapshot,DateTimeOffset.UtcNow);
            var run=new AnalysisRun(id,preview.Kind,DateTimeOffset.UtcNow,"Codex CLI · ChatGPT",provider is CodexAnalysisProvider codex?codex.Model:"Fixture provider",policy.Version,preview.Snapshot,result);
            return run;
        }finally{gate.Release();}
    }
    internal void Save(AnalysisRun run)
    {
        var path=Path.Combine(directory,run.Id+".json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(run,CodexAnalysisProvider.Json));File.Move(path+".tmp",path);
    }
    public AnalysisRun[] History()=>Directory.GetFiles(directory,"*.json").Select(path=>JsonSerializer.Deserialize<AnalysisRun>(File.ReadAllText(path),CodexAnalysisProvider.Json)!).OrderByDescending(r=>r.CreatedAt).Take(50).ToArray();
    public AnalysisRun Find(string id)
    {
        if(!Guid.TryParseExact(id,"N",out _))throw new FormatException("Invalid analysis identifier.");var path=Path.Combine(directory,id+".json");if(!File.Exists(path))throw new FormatException("Saved analysis was not found.");return JsonSerializer.Deserialize<AnalysisRun>(File.ReadAllText(path),CodexAnalysisProvider.Json)!;
    }
    public FollowupReport Outcomes(string id){var run=Find(id);var metadata=AnalysisOutcomes.Metadata(run);return AnalysisOutcomes.Calculate(run,(market??throw new FormatException("Market storage is unavailable.")).Followup(metadata.Symbol,metadata.Source,run.CreatedAt),DateTimeOffset.UtcNow);}
    public static void Map(WebApplication app,AnalysisService service,CodexAnalysisProvider provider)
    {
        app.MapGet("/api/ai/status",()=>provider.Status());app.MapGet("/api/ai/history",()=>service.History());
        app.MapGet("/api/ai/outcomes",(string id)=>{try{return Results.Ok(service.Outcomes(id));}catch(FormatException e){return Results.BadRequest(new{message=e.Message});}});
    }
}
