using System.Text.Json;
namespace TradingWorkbench;

public static class BrokerJournalChecks
{
    public static async Task Run()
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
        var now=DateTimeOffset.UtcNow;var start=DateTimeOffset.FromUnixTimeSeconds(now.AddDays(-2).ToUnixTimeSeconds()/60*60);var sequence=0;
        BrokerFill Fill(decimal amount,decimal price,int minute,decimal? fees=1.5m,string? effect=null,string? order=null)=>new((++sequence).ToString(),order??sequence.ToString(),"/MESZ26",start.AddMinutes(minute),amount,price,effect??(amount>0?"OPENING":"CLOSING"),fees);
        var fills=new[]{Fill(1,100,0),Fill(-1,102,1),Fill(2,103,2,3),Fill(-1,104,3),Fill(-1,105,4)};
        BrokerHistory History(BrokerFill[] rows)=>new(now.AddDays(-59),now,[new("fixture-account","Account 1",rows)]);
        var built=BrokerJournalAdapter.Build("fixture-account","Schwab futures",fills);
        Check(built.Length==2&&built[0].Trade.GrossPnl==10&&built[0].Trade.Fees==3&&built[1].Trade.GrossPnl==15&&built[1].Trade.Fees==6&&built[1].Trade.NetPnl==9,"API journal reuses decimal flat-to-flat P/L and preserves partial-exit fees");
        Check(built.All(t=>t.Trade.Source=="Schwab API"&&t.Trade.Id.StartsWith("api-"))&&built[0].Trade.Symbol=="MESZ26","API journal has stable source identities and normalizes symbols to the existing journal");
        var pending=BrokerJournalAdapter.Build("fixture-account","Schwab futures",fills.Take(4));
        Check(pending.Length==1,"a partly exited open cycle stays pending while earlier completed trades remain importable");
        var reversal=new[]{Fill(2,100,10,3),Fill(-2,99,11,3,"OPENING","reversal"),Fill(-1,99,11,1.5m,"CLOSING","reversal"),Fill(-1,99,11,1.5m,"CLOSING","reversal"),Fill(2,98,12,3,"CLOSING")};
        var reversed=BrokerJournalAdapter.Build("fixture-account","Schwab futures",reversal);
        Check(reversed.Length==2&&reversed[0].Trade.GrossPnl== -10&&reversed[1].Trade.GrossPnl==10&&reversed.Sum(t=>t.Trade.Fees)==12,"API reversal closes and reopens through the shared accounting without losing fees");
        bool Rejected(Action action){try{action();return false;}catch(FormatException){return true;}}
        Check(Rejected(()=>BrokerJournalAdapter.Build("fixture-account","Schwab futures",fills.Select(f=>f with{Fees=null}))),"missing API fees block journal writes instead of inventing zero commissions");
        using var feeDocument=JsonDocument.Parse("""[{"activityId":1,"orderId":1,"time":"2026-10-22T12:00:00+0000","status":"VALID","transferItems":[{"instrument":{"assetType":"CURRENCY","symbol":"CURRENCY_USD"},"cost":-1.25,"feeType":"COMMISSION"},{"instrument":{"assetType":"CURRENCY","symbol":"CURRENCY_USD"},"cost":-0.25,"feeType":"EXCHANGE_FEE"},{"instrument":{"assetType":"FUTURE","symbol":"/MESZ26:XCME"},"amount":1,"price":100,"positionEffect":"OPENING"}]}]""");
        Check(BrokerPositionCalculator.Parse(feeDocument.RootElement).Single().Fees==1.5m,"explicit USD commission and fee costs map to positive execution costs");
        using var additional=JsonDocument.Parse(feeDocument.RootElement.GetRawText().Replace("\"feeType\":\"EXCHANGE_FEE\"","\"amount\":0.25"));
        var unclassified=BrokerPositionCalculator.Parse(additional.RootElement).Single();
        Check(unclassified is {Fees:1.5m,UnclassifiedFees:0.25m},"USD trade debit charges are included even when Schwab omits feeType");
        var oldFee=unclassified with{Fees=1.25m,UnclassifiedFees=0};
        Check(unclassified.CanReplace(oldFee)&&!unclassified.CanReplace(oldFee with{Price=101})&&!(unclassified with{Fees=1.6m}).CanReplace(oldFee),"fee enrichment adds only newly classified cash charges while rejecting changed prices and commissions");
        using var invalidCharge=JsonDocument.Parse(additional.RootElement.GetRawText().Replace("\"amount\":0.25","\"amount\":0.5"));
        Check(BrokerPositionCalculator.Parse(invalidCharge.RootElement).Single().Fees is null,"unclassified trade charges must reconcile their amount with the cash debit cost");
        using var unknownFee=JsonDocument.Parse(feeDocument.RootElement.GetRawText().Replace("-1.25","1.25"));
        Check(BrokerPositionCalculator.Parse(unknownFee.RootElement).Single().Fees is null,"unrecognized fee credits remain unknown for journal accounting");
        var directory=Path.Combine(Path.GetTempPath(),"broker-journal-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var store=new TradeStore(directory);var current=History(fills);var service=new BrokerJournalService(store,_=>Task.FromResult(current));
            var first=await service.Sync(CancellationToken.None);
            Check(first.Imported==0&&first.NeedsReconciliation.SequenceEqual(new[]{"MESZ26"})&&store.List().Count==0,"unmatched contracts are archived but do not enter the journal without a statement anchor");
            var reference=built[0].Trade with{Id="csv-reference",Source="CSV",Notes="Preserve journal notes",Risk=25};store.Save(reference);
            var synced=await service.Sync(CancellationToken.None);
            Check(synced.Imported==1&&synced.Matched==1&&store.List().Count==2&&store.List().Single(t=>t.Id==reference.Id).Notes==reference.Notes,"first sync matches existing CSV economics, preserves notes and imports only new trades");
            Check(store.Fills(built[1].Trade.Id).Length==3&&store.List().Sum(t=>t.NetPnl)==16,"persisted API fills and net P/L reconcile with independent expected totals");
            Check((await service.Sync(CancellationToken.None)).Imported==0&&store.List().Count==2,"repeated API journal sync cannot duplicate CSV or API trades");
            Check(store.ImportStatus(built[1] with{Trade=built[1].Trade with{Id="csv-later",Source="CSV"}})=="duplicate","a later CSV import recognizes the same API trade instead of duplicating it");
            current=History(fills.Select((f,i)=>i==2?f with{Fees=4}:f).ToArray());
            var correctionBlocked=false;try{await service.Sync(CancellationToken.None);}catch(FormatException){correctionBlocked=true;}
            Check(correctionBlocked&&store.List().Sum(t=>t.NetPnl)==16,"changed archived API fees stop sync without rewriting saved P/L");
            current=new(start.AddMinutes(2),now,[new("fixture-account","Account 1",fills.Skip(2).ToArray())]);
            Check((await service.Sync(CancellationToken.None)).Imported==0&&store.List().Count==2,"archived older executions survive a moving API query window");
            current=History(fills.Where((_,i)=>i!=2).ToArray());var disappeared=false;
            try{await service.Sync(CancellationToken.None);}catch(FormatException){disappeared=true;}
            Check(disappeared&&store.List().Count==2,"disappearing overlapping transactions cannot silently erase or regroup journal trades");
            var reopened=new BrokerJournalService(new TradeStore(directory),_=>Task.FromResult(History(fills)));
            Check((await reopened.Sync(CancellationToken.None)).Imported==0,"journal archives, source links and verified contracts survive restart");
            var other=History(fills) with{Accounts=[new("different-account","Account 1",fills)]};
            Check(Rejected(()=>store.ArchiveBrokerHistory(other)),"a different linked account cannot reuse an existing journal mapping");
            var enrichmentStore=new TradeStore(Path.Combine(directory,"fee-enrichment"));enrichmentStore.EnsureBrokerJournal();
            enrichmentStore.ArchiveBrokerHistory(History(fills.Select(f=>f with{Fees=null}).ToArray()));
            Check(enrichmentStore.ArchiveBrokerHistory(History(fills)).All(f=>f.Fees is not null),"archived unknown fees can be enriched without changing execution identity");
            var enriched=fills.Select(f=>f with{Fees=f.Fees+0.02m,UnclassifiedFees=0.02m}).ToArray();
            var enrichedArchive=enrichmentStore.ArchiveBrokerHistory(History(enriched));
            Check(enrichedArchive.Sum(f=>f.Fees)==fills.Sum(f=>f.Fees)+fills.Length*0.02m&&enrichmentStore.ArchiveBrokerHistory(History(enriched)).Length==fills.Length,"cash charge enrichment updates archived costs exactly once without duplicating executions");
            var overlapDirectory=Path.Combine(directory,"overlap");var overlapStore=new TradeStore(overlapDirectory);var overlapService=new BrokerJournalService(overlapStore,_=>Task.FromResult(History(fills)));
            overlapStore.Save(reference);overlapStore.Save(built[1].Trade with{Id="csv-different-cost",Source="CSV",Fees=7});
            var blocked=false;try{await overlapService.Sync(CancellationToken.None);}catch(FormatException){blocked=true;}
            Check(blocked&&overlapStore.List().Count==2&&overlapStore.List().Any(t=>t.Fees==7),"overlapping trades with different fees roll back the entire journal commit");
            var builder=WebApplication.CreateSlimBuilder(new WebApplicationOptions{Args=[]});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
            await using var app=builder.Build();BrokerJournalService.Map(app,reopened);await app.StartAsync();
            try
            {
                var address=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.Single();
                using var client=new HttpClient();using var response=await client.PostAsync(address+"/api/schwab/journal/sync",null);
                using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Check(response.IsSuccessStatusCode&&json.RootElement.GetProperty("imported").GetInt32()==0,"journal sync route returns durable reconciliation results as JSON");
            }
            finally{await app.StopAsync();}
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(directory,true);}
    }
}
