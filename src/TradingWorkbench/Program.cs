using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.DataProtection;
using TradingWorkbench;

if (args.Contains("--self-test")) { await CodexLoginChecks.Run(); HostAccessChecks.Run(); JournalChecks.Run(); ImportChecks.Run(); StatementChecks.Run(); StatementOverlapChecks.Run(); AnalyticsChecks.Run(); await SchwabChecks.Run(); await AnalysisChecks.Run(); MarketChecks.Run(); OutcomeChecks.Run(); return; }
if(args.Contains("--codex-probe")){await CodexLoginChecks.Probe();return;}
if(args.Contains("--ai-smoke")){await AnalysisChecks.Run(true);return;}
if(args.Contains("--review-statement"))
{
    var file=args[Array.IndexOf(args,"--review-statement")+1];
    var checkStore=new TradeStore(Path.Combine(Path.GetTempPath(),"statement-review-"+Guid.NewGuid().ToString("N")));
    var preview=new ImportService(checkStore).Preview(new(Path.GetFileName(file),File.ReadAllText(file),new(),"Schwab futures","Futures",1,"Eastern"));
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{preview.RowCount,closedTrades=preview.Trades.Length,preview.Errors,preview.Reconciliation,open=preview.OpenPositions.Select(p=>new{p.Symbol,p.Quantity,p.AverageEntry,p.EntryFees})},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    if(preview.Errors.Length>0)Environment.ExitCode=1;
    return;
}
var root = AppContext.BaseDirectory;
while(!File.Exists(Path.Combine(root,"web","index.html")))root=Directory.GetParent(root.TrimEnd(Path.DirectorySeparatorChar))?.FullName ?? throw new InvalidOperationException("Cannot locate the web folder.");
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.WebHost.ConfigureKestrel(options=>options.Limits.MaxRequestBodySize=2_500_000);
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:5173");
var secretsDirectory=Path.Combine(builder.Configuration["DataDirectory"]??Path.Combine(root,"data"),"secrets");
var protection=builder.Services.AddDataProtection().SetApplicationName("TradingWorkbench").PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(secretsDirectory,"keys")));
if(OperatingSystem.IsWindows())protection.ProtectKeysWithDpapi();
var app = builder.Build();
var store = new TradeStore(app.Configuration["DataDirectory"] ?? Path.Combine(root, "data"));
var imports = new ImportService(store);
var schwab=new SchwabConnection(secretsDirectory,app.Services.GetRequiredService<IDataProtectionProvider>(),new HttpClient{Timeout=TimeSpan.FromSeconds(20)});
var aiProvider=new CodexAnalysisProvider(builder.Configuration["CodexExecutable"],builder.Configuration["CodexHome"]);
await using var codexLogin=new CodexLoginService(()=>new CodexLoginSession(aiProvider),aiProvider.Status,aiProvider.Logout,aiProvider.EnterOperation);
var market=new MarketStore(app.Configuration["DataDirectory"]??Path.Combine(root,"data"));
var analysis=new AnalysisService(store,aiProvider,Path.Combine(app.Configuration["DataDirectory"]??Path.Combine(root,"data"),"analysis"),market);
var ingress=app.Configuration.GetValue<bool>("HomeAssistantIngress");
app.Use(async (context, next) =>
{
    if(context.Request.Path.StartsWithSegments("/api/schwab")||context.Request.Path.StartsWithSegments("/api/ai"))context.Response.Headers.CacheControl="no-store";
    if (!HostAccess.Allowed(context, ingress)) { context.Response.StatusCode = 403; return; }
    if (ingress && context.Request.Method == "GET" && context.Request.Path.Value is "/" or "/index.html")
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsync(HostAccess.IngressDocument(await File.ReadAllTextAsync(Path.Combine(root, "web", "index.html")), context));
        return;
    }
    await next();
});
app.MapGet("/api/status", () => new { stage = 5, aiPilot=true, storage = "SQLite", instruments = InstrumentCatalog.All });
SchwabRoutes.Map(app,schwab);
MarketRoutes.Map(app,market,schwab);
AnalysisService.Map(app,analysis,aiProvider);
CodexLoginService.Map(app,codexLogin);
app.MapGet("/api/analytics",(HttpRequest request)=>
{
    try{return Results.Ok(Analytics.Calculate(TradeFilter.Read(request.Query).Apply(store.List())));}
    catch(FormatException error){return Results.BadRequest(new{message=error.Message});}
});
app.MapGet("/api/trades/export",(HttpRequest request)=>
{
    try{return Results.File(System.Text.Encoding.UTF8.GetBytes("\uFEFF"+Analytics.Export(TradeFilter.Read(request.Query).Apply(store.List()))),"text/csv; charset=utf-8","trades-filtered.csv");}
    catch(FormatException error){return Results.BadRequest(new{message=error.Message});}
});
app.MapGet("/api/trades", () => store.List());
app.MapGet("/api/positions",()=>store.Positions());
app.MapGet("/api/trades/{id}/fills",(string id)=>store.Fills(id));
app.MapPost("/api/trades", (TradeData input) =>
{
    var errors = input.Validate();
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var trade = input with { Id = Guid.NewGuid().ToString("N"), Account="Personal",Source="Manual",AccountingGrossPnl=null,Symbol = input.Symbol.Trim().ToUpperInvariant(), Notes = (input.Notes ?? "").Trim() };
    store.Save(trade);
    return Results.Created($"/api/trades/{trade.Id}", trade);
});
app.MapPut("/api/trades/{id}", (string id, TradeData input) =>
{
    var existing=store.List().FirstOrDefault(t=>t.Id==id);
    if (existing is null) return Results.NotFound();
    var candidate=existing.Source=="CSV"?existing with{Notes=input.Notes??"",Risk=input.Risk,Setup=input.Setup}:input with{Account=existing.Account,Source="Manual",AccountingGrossPnl=null};
    var errors = candidate.Validate();
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var trade = candidate with { Id = id, Symbol = candidate.Symbol.Trim().ToUpperInvariant(), Notes = (candidate.Notes ?? "").Trim() };
    store.Save(trade);
    return Results.Ok(trade);
});
app.MapGet("/api/backup", () => Results.File(store.Backup(), "application/vnd.sqlite3", "trading-backup.db"));
app.MapPost("/api/imports/preview",(ImportRequest request)=>
{
    try{return Results.Ok(imports.Preview(request));}
    catch(FormatException error){return Results.BadRequest(new{message=error.Message});}
});
app.MapPost("/api/imports/commit",(CommitRequest request)=>
{
    try{return Results.Ok(imports.Commit(request.Token));}
    catch(FormatException error){return Results.BadRequest(new{message=error.Message});}
});
var files = new PhysicalFileProvider(Path.Combine(root, "web"));
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
app.Run();
