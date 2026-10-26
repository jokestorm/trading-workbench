using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.DataProtection;
using TradingWorkbench;

if(args.Contains("--self-test")||args.Contains("--trader-test")){await TraderChatChecks.Run();if(args.Contains("--trader-test"))return;}
if(args.Contains("--self-test")||args.Contains("--account-test")){await AccountDashboardChecks.Run();if(args.Contains("--account-test"))return;}
if(args.Contains("--self-test")||args.Contains("--universe-test")){await FuturesUniverseChecks.Run();if(args.Contains("--universe-test"))return;}
if(args.Contains("--self-test")||args.Contains("--execution-test")){await BrokerExecutionChecks.Run();if(args.Contains("--execution-test"))return;}
if(args.Contains("--self-test"))await ContinuousMonitorChecks.Run();
if (args.Contains("--self-test")) { await BrokerJournalChecks.Run(); await BrokerPositionChecks.Run(); await SignalContinuityChecks.Run(); await MarketBriefChecks.Run(); await TimeframeChecks.Run(); TechnicalIndicatorChecks.Run(); await PromptSettingsChecks.Run(); StatementExecutionChecks.Run(); CashSettlementChecks.Run(); await AnalysisWorkflowChecks.Run(); await CodexLoginChecks.Run(); HostAccessChecks.Run(); JournalChecks.Run(); ImportChecks.Run(); StatementChecks.Run(); StatementOverlapChecks.Run(); AnalyticsChecks.Run(); await SchwabChecks.Run(); await AnalysisChecks.Run(); MarketChecks.Run(); OutcomeChecks.Run(); return; }
if(args.Contains("--monitor-test")){await ContinuousMonitorChecks.Run();return;}
if(args.Contains("--workflow-test")){await AnalysisWorkflowChecks.Run();return;}
if(args.Contains("--signal-continuity-test")){await SignalContinuityChecks.Run();return;}
if(args.Contains("--settlement-test")){CashSettlementChecks.Run();return;}
if(args.Contains("--workflow-crash")){await AnalysisWorkflowChecks.Crash(args[Array.IndexOf(args,"--workflow-crash")+1]);return;}
if(args.Contains("--codex-probe")){await CodexLoginChecks.Probe();return;}
if(args.Contains("--ai-smoke")){await AnalysisChecks.Run(true);return;}
if(args.Contains("--review-statement"))
{
    var file=args[Array.IndexOf(args,"--review-statement")+1];
    var checkStore=new TradeStore(Path.Combine(Path.GetTempPath(),"statement-review-"+Guid.NewGuid().ToString("N")));
    try
    {
    var reviewer=new ImportService(checkStore);
    ImportRequest ReadStatement(string path)=>new(Path.GetFileName(path),File.ReadAllText(path),new(),"Schwab futures","Futures",1,"Eastern");
    if(args.Contains("--prior-statement")){var prior=reviewer.Preview(ReadStatement(args[Array.IndexOf(args,"--prior-statement")+1]));if(prior.Errors.Length>0)throw new FormatException("The prior statement does not reconcile.");reviewer.Commit(prior.Token);}
    var request=ReadStatement(file);var preview=reviewer.Preview(request);
    if(args.Contains("--verify-reimport")&&preview.Errors.Length==0){reviewer.Commit(preview.Token);var repeat=reviewer.Preview(request);Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{newTrades=preview.Trades.Length,preview.Duplicates,reconciled=preview.Reconciliation?.Reconciled,errors=preview.Errors.Length,reimportNew=repeat.Trades.Length,reimportDuplicates=repeat.Duplicates,reimportErrors=repeat.Errors.Length}));return;}
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{preview.RowCount,closedTrades=preview.Trades.Length,preview.Errors,preview.Reconciliation,open=preview.OpenPositions.Select(p=>new{p.Symbol,p.Quantity,p.AverageEntry,p.EntryFees})},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    if(preview.Errors.Length>0)Environment.ExitCode=1;
    }
    catch(FormatException error){Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{errors=new[]{new ImportIssue(0,error.Message)}}));Environment.ExitCode=1;}
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
var aiProvider=new CodexAnalysisProvider(builder.Configuration["CodexExecutable"],builder.Configuration["CodexHome"],logger:app.Services.GetRequiredService<ILogger<CodexAnalysisProvider>>());
await using var codexLogin=new CodexLoginService(()=>new CodexLoginSession(aiProvider),aiProvider.Status,aiProvider.Logout,aiProvider.EnterOperation);
var market=new MarketStore(app.Configuration["DataDirectory"]??Path.Combine(root,"data"));
var brokerPositions=new BrokerPositionService(Path.Combine(app.Configuration["DataDirectory"]??Path.Combine(root,"data"),"broker-positions"),schwab.FuturesTransactions);
BrokerPositionService.Map(app,brokerPositions);
var brokerJournal=new BrokerJournalService(store,schwab.FuturesTransactions);
BrokerJournalService.Map(app,brokerJournal);
var brokerExecutions=new BrokerExecutionService(Path.Combine(app.Configuration["DataDirectory"]??Path.Combine(root,"data"),"broker-executions"),schwab.FuturesTransactions,app.Services.GetRequiredService<ILogger<BrokerExecutionService>>());
var accountDashboard=new AccountDashboard(store,Path.Combine(app.Configuration["DataDirectory"]??Path.Combine(root,"data"),"account-dashboard"),brokerExecutions.Refresh,app.Services.GetRequiredService<ILogger<AccountDashboard>>());
var timeframes=new TimeframeService(market,Path.Combine(app.Configuration["DataDirectory"]??Path.Combine(root,"data"),"daily-history"),(symbol,token)=>schwab.PriceHistory(symbol,token,daily:true));
TimeframeService.Map(app,timeframes);
var promptSettings=new PromptSettings(Path.Combine(app.Configuration["DataDirectory"]??Path.Combine(root,"data"),"prompt-settings"));
PromptSettings.Map(app,promptSettings);
var analysis=new AnalysisService(store,aiProvider,Path.Combine(app.Configuration["DataDirectory"]??Path.Combine(root,"data"),"analysis"),market,promptSettings,timeframes,accountDashboard.Trades);
await using var analysisWorkflow=new AnalysisWorkflow(analysis,Path.Combine(app.Configuration["DataDirectory"]??Path.Combine(root,"data"),"analysis","jobs"),(request,token)=>MarketRoutes.RefreshForAnalysis(market,schwab,request,token),app.Services.GetRequiredService<ILogger<AnalysisWorkflow>>());
var ingress=app.Configuration.GetValue<bool>("HomeAssistantIngress");
var marketBrief=new MarketBriefService(Path.Combine(app.Configuration["DataDirectory"]??Path.Combine(root,"data"),"market-brief"),
    (previous,now,directory,token)=>MarketBriefService.Research(aiProvider,previous,now,directory,token),
    ()=>analysisWorkflow.Current() is {Active:true}||aiProvider.ConversationActive,app.Services.GetRequiredService<ILogger<MarketBriefService>>());
MarketBriefService.Map(app,marketBrief);
app.Use(async (context, next) =>
{
    if(context.Request.Path.StartsWithSegments("/api/schwab")||context.Request.Path.StartsWithSegments("/api/ai")||context.Request.Path.StartsWithSegments("/api/account"))context.Response.Headers.CacheControl="no-store";
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
AnalysisService.Map(app,analysis,aiProvider);
AnalysisWorkflow.Map(app,analysisWorkflow);
BrokerExecutionService.Map(app,brokerExecutions);
AccountDashboard.Map(app,accountDashboard);
TraderChat? traderChat=null;
var continuousMonitor=new ContinuousMonitor(Path.Combine(app.Configuration["DataDirectory"]??Path.Combine(root,"data"),"continuous-monitor"),
    async(spec,refreshBars,token)=>
    {
        var quote=await schwab.Quote(spec.Symbol,token);token.ThrowIfCancellationRequested();market.SaveQuote(quote);
        string? historyError=null;
        if(refreshBars)
        {
            using var historyTimeout=CancellationTokenSource.CreateLinkedTokenSource(token);historyTimeout.CancelAfter(TimeSpan.FromSeconds(8));
            try{var bars=await schwab.PriceHistory(spec.Symbol,historyTimeout.Token);token.ThrowIfCancellationRequested();market.SaveBars(bars);}
            catch(Exception e) when(e is FormatException or HttpRequestException||e is OperationCanceledException&&!token.IsCancellationRequested){historyError="History refresh failed. Quote-based exit monitoring continues.";}
        }
        return new MonitorObservation(quote,market.Bars(spec.Symbol,1),historyError);
    },analysisWorkflow.TryStartMonitor,analysisWorkflow.Status,analysis.Find,id=>analysisWorkflow.Cancel(id),
    logger:app.Services.GetRequiredService<ILogger<ContinuousMonitor>>(),transactions:brokerExecutions.Current,discussion:(symbol,scope)=>traderChat?.Discussion(symbol,scope)??[],aiAvailable:()=>!aiProvider.ConversationActive,briefContext:marketBrief.Current);
ContinuousMonitor.Map(app,continuousMonitor);
var traderDirectory=Path.Combine(app.Configuration["DataDirectory"]??Path.Combine(root,"data"),"trader-chat");
var traderData=new TraderData(accountDashboard,brokerExecutions,continuousMonitor,market,schwab,analysis,marketBrief);
await using var trader=new TraderChat(traderDirectory,new CodexTraderAgent(aiProvider,Path.Combine(traderDirectory,"workspace")),traderData.Tool,traderData.Context);
traderChat=trader;TraderChat.Map(app,trader);
MarketRoutes.Map(app,market,schwab,timeframes,()=>continuousMonitor.Current().Settings.Instruments);
var futuresUniverse=new FuturesUniverse(async(symbols,token)=>{var quotes=await schwab.Quotes(symbols,token);foreach(var quote in quotes)market.SaveQuote(quote);return quotes;},market.Watchlist,brokerExecutions.Refresh,continuousMonitor.ApplyUniverse,app.Services.GetRequiredService<ILogger<FuturesUniverse>>());
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
    var candidate=existing.Source is "CSV" or "Schwab API"?existing with{Notes=input.Notes??"",Risk=input.Risk}:input with{Account=existing.Account,Source="Manual",AccountingGrossPnl=null};
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
using var briefStop=CancellationTokenSource.CreateLinkedTokenSource(app.Lifetime.ApplicationStopping);
var briefTask=marketBrief.Run(briefStop.Token);
var monitorTask=continuousMonitor.Run(briefStop.Token);
var executionsTask=brokerExecutions.Run(()=>continuousMonitor.Current().Settings.Enabled,briefStop.Token);
var universeTask=futuresUniverse.Run(briefStop.Token);
var dashboardTask=accountDashboard.Run(briefStop.Token);
try{await app.RunAsync();}finally{briefStop.Cancel();await Task.WhenAll(briefTask,monitorTask,executionsTask,universeTask,dashboardTask);}
