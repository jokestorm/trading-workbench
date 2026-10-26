using System.Text.Json;

namespace TradingWorkbench;

public static class AccountDashboardChecks
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
    public static async Task Run()
    {
        var directory=Path.Combine(Path.GetTempPath(),"account-dashboard-"+Guid.NewGuid().ToString("N"));
        var store=new TradeStore(directory);var now=DateTimeOffset.UtcNow;var start=now.AddDays(-2);var sequence=0;
        BrokerFill Fill(string symbol,decimal qty,decimal price,int minute,decimal fees=1.5m,string? effect=null)=>new((++sequence).ToString(),sequence.ToString(),symbol,start.AddMinutes(minute),qty,price,effect??(qty>0?"OPENING":"CLOSING"),fees);
        var fills=new List<BrokerFill>{Fill("/MESZ26",1,100,0),Fill("/MESZ26",-1,102,1),Fill("/MESZ26",2,103,2,3),Fill("/MESZ26",-1,104,3),Fill("/MESZ26",-1,105,4)};
        BrokerTransactionReport Report()=>new("ready","Fixture transactions",now,now,new(now.AddDays(-59),now,[new("fixture","Account 1",fills.ToArray())]));
        var feed=Report();var baseline=BrokerJournalAdapter.Build("fixture","Schwab futures",fills);
        var legacy=baseline[0].Trade with{Id="csv-duplicate",Source="CSV",Notes="Preserve these notes",Risk=25};store.Save(legacy);
        store.Save(baseline[0].Trade with{Id="csv-unrelated",Symbol="MGCZ26",Source="CSV",AccountingGrossPnl=10000});
        var dashboard=new AccountDashboard(store,Path.Combine(directory,"dashboard"),_=>Task.FromResult(feed));
        var data=await dashboard.Sync(CancellationToken.None);
        Check(data.Status=="ready"&&data.Trades.Length==2&&data.Trades.Sum(t=>t.NetPnl)==16&&data.Trades.All(t=>t.Source=="Schwab API"),"account totals use only transaction-derived closed trades, excluding CSV duplicates and unrelated CSV P/L");
        Check(data.Trades.Single(t=>t.Id==baseline[0].Trade.Id) is {Notes:"Preserve these notes",Risk:25}&&store.List().Count==2,"matching CSV annotations transfer without replacing or deleting archived CSV trades");
        Check(data.Trades.Sum(t=>t.GrossPnl)==25&&data.Trades.Sum(t=>t.Fees)==9&&data.FillCount==5,"transaction dashboard gross, fees and net reconcile with independent expected amounts");
        var again=await dashboard.Sync(CancellationToken.None);
        Check(again.Revision==data.Revision&&again.Trades.Length==2,"repeated dashboard synchronization neither duplicates trades nor changes the content revision");
        fills.Add(Fill("/MNQZ26",1,100,10));fills.Add(Fill("/MNQZ26",-1,110,11));
        now=now.AddSeconds(1);feed=Report();data=await dashboard.Sync(CancellationToken.None);
        Check(data.Trades.Length==3&&data.Trades.Sum(t=>t.NetPnl)==33&&data.Trades.Any(t=>t.Symbol=="MNQZ26"),"new transaction contracts enter the dashboard without requiring any CSV anchor");
        fills.Add(Fill("/MGCZ26",2,2000,12,2));fills.Add(Fill("/MGCZ26",-1,2002,13,1));
        now=now.AddSeconds(1);feed=Report();data=await dashboard.Sync(CancellationToken.None);
        Check(data.OpenPositions is [{Quantity:1,AverageEntry:2000,EntryFees:1}]&&data.Trades.Sum(t=>t.NetPnl)==33,"open transaction cycles remain separate from completed-trade dashboard totals");
        var edited=dashboard.Annotate(data.Trades[0].Id,new(){Notes="Account note",Risk=10,Entry=999,Fees=999});
        Check(edited.Notes=="Account note"&&edited.Risk==10&&edited.Entry==data.Trades[0].Entry&&edited.Fees==data.Trades[0].Fees,"account note edits preserve authoritative broker accounting");
        var reopened=new AccountDashboard(new TradeStore(directory),Path.Combine(directory,"dashboard"),_=>Task.FromResult(feed));
        Check(reopened.Trades().Single(t=>t.Id==edited.Id).Notes=="Account note","transaction-derived dashboard and annotations survive restart");
        var legacyCount=store.List().Count;var oldNet=data.Trades.Sum(t=>t.NetPnl);
        now=now.AddSeconds(1);feed=Report() with{History=Report().History! with{Accounts=[new("fixture","Account 1",fills.Select((f,i)=>i==0?f with{Fees=null}:f).ToArray())]}};
        var failed=await dashboard.Sync(CancellationToken.None);
        Check(failed.Status=="failed"&&failed.Trades.Sum(t=>t.NetPnl)==oldNet&&store.List().Count==legacyCount,"failed transaction accounting retains the last transaction snapshot and never substitutes CSV totals");
        var provider=new PreviewOnlyProvider();
        var analysis=new AnalysisService(store,provider,Path.Combine(directory,"analysis"),accountTrades:dashboard.Trades);
        var preview=analysis.Preview(new("journal",DataSource:"transactions"));
        Check(preview.Snapshot.GetProperty("dataSource").GetString()=="transactions"&&preview.Snapshot.GetProperty("summary").GetProperty("net").GetDecimal()==oldNet,"account AI review uses the same transaction population as the dashboard");
        var builder=WebApplication.CreateSlimBuilder(new WebApplicationOptions{Args=[]});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app=builder.Build();AccountDashboard.Map(app,dashboard);await app.StartAsync();
        try
        {
            var address=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.Single();
            using var client=new HttpClient();
            using var summary=JsonDocument.Parse(await client.GetStringAsync(address+"/api/account/analytics?symbol=MNQZ26"));
            Check(summary.RootElement.GetProperty("net").GetDecimal()==17&&summary.RootElement.GetProperty("count").GetInt32()==1,"account filters calculate backend totals from transaction-derived trades");
            var exported=await client.GetStringAsync(address+"/api/account/trades/export?symbol=MNQZ26");
            Check(exported.Contains("MNQZ26")&&!exported.Contains("MGCZ26")&&exported.Contains("Schwab API"),"account CSV exports describe transaction trades with the same filters as the dashboard");
            using var allocated=JsonDocument.Parse(await client.GetStringAsync(address+$"/api/account/trades/{baseline[1].Trade.Id}/fills"));
            Check(allocated.RootElement.GetArrayLength()==3,"account trade details return the transaction's allocated entry and exit fills");
            using var invalid=await client.GetAsync(address+"/api/account/analytics?start=invalid");Check((int)invalid.StatusCode==400,"invalid account date filters are rejected before calculating totals");
        }
        finally{await app.StopAsync();}
    }
    private sealed class PreviewOnlyProvider:IAnalysisProvider
    {
        public Task<AnalysisResult> Analyze(JsonElement snapshot,string directory,CancellationToken token,AnalysisPrompt? prompt=null)=>throw new Exception("Dashboard checks must not run inference.");
    }
}
