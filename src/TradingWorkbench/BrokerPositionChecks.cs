using System.Text.Json;
namespace TradingWorkbench;

public static class BrokerPositionChecks
{
    public static async Task Run()
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
        var now=DateTimeOffset.UtcNow;var from=now.AddDays(-59);var time=now.AddHours(-2);var sequence=0;
        BrokerFill Fill(decimal amount,decimal price,string effect,int minute,string? order=null)=>new((++sequence).ToString(),order??sequence.ToString(),"/MESZ26",time.AddMinutes(minute),amount,price,effect);
        DerivedPosition Position(params BrokerFill[] fills)=>BrokerPositionCalculator.Calculate(new(from,now,[new("fixture","Account 1",fills)])).Single();
        var scaled=Position(Fill(1,100,"OPENING",0),Fill(2,103,"OPENING",1),Fill(-1,105,"CLOSING",2));
        Check(scaled.State=="LONG"&&scaled.Quantity==2&&scaled.AverageEntry==102,"derived position preserves weighted entry through scaling and partial exits");
        var reversed=Position(Fill(2,100,"OPENING",0),Fill(-2,99,"OPENING",1,"reversal"),Fill(-1,99,"CLOSING",1,"reversal"),Fill(-1,99,"CLOSING",1,"reversal"));
        Check(reversed.State=="SHORT"&&reversed.Quantity==2&&reversed.AverageEntry==99,"same-order same-time reversal closes the prior position before opening the new side");
        Check(Position(Fill(-2,100,"OPENING",0),Fill(2,99,"CLOSING",1)).State=="FLAT","short covering returns a derived position to flat");
        Check(Position(Fill(-1,100,"CLOSING",0)).State=="UNKNOWN","history starting mid-position cannot invent a flat baseline");
        var ambiguous=Position(Fill(1,100,"OPENING",0),Fill(1,110,"OPENING",1),Fill(-1,105,"CLOSING",1));
        Check(ambiguous.State=="UNKNOWN","ambiguous simultaneous orders cannot invent an average entry");
        using var fixture=JsonDocument.Parse("""[{"activityId":1,"orderId":9,"time":"2026-10-21T20:14:50+0000","status":"VALID","transferItems":[{"instrument":{"assetType":"FUTURE","symbol":"/MESZ26:XCME"},"amount":1,"price":100.25,"positionEffect":"OPENING"}]}]""");
        var parsed=BrokerPositionCalculator.Parse(fixture.RootElement);Check(parsed.Single().Symbol=="/MESZ26"&&parsed[0].Time.Offset==TimeSpan.Zero,"broker transaction parsing normalizes exchange suffixes and Schwab timezone offsets");
        using var repeated=JsonDocument.Parse("["+fixture.RootElement[0].GetRawText()+","+fixture.RootElement[0].GetRawText()+"]");
        Check(BrokerPositionCalculator.Parse(repeated.RootElement).Length==1,"repeated transaction identifiers do not multiply contract quantities");
        using var corrected=JsonDocument.Parse(fixture.RootElement.GetRawText().Replace("VALID","CORRECTED"));var blocked=false;
        try{BrokerPositionCalculator.Parse(corrected.RootElement);}catch(FormatException){blocked=true;}
        Check(blocked,"unresolved transaction corrections block updates rather than silently dropping fills");
        var separated=BrokerPositionCalculator.Calculate(new(from,now,[new("one","Account 1",parsed),new("two","Account 2",parsed.Select(f=>f with{Amount=-1}).ToArray())]));
        Check(separated.Length==2&&separated[0].State=="LONG"&&separated[1].State=="SHORT","positions in separate accounts never net against each other");
        var directory=Path.Combine(Path.GetTempPath(),"broker-position-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var calls=0;var service=new BrokerPositionService(directory,_=>{calls++;return Task.FromResult(new BrokerHistory(from,now,[new("fixture","Account 1",parsed)]));});
            var report=await service.Refresh(CancellationToken.None);await service.Refresh(CancellationToken.None);
            Check(calls==1&&report.Positions.Single().Quantity==1,"repeated syncs share a bounded polling interval without duplicate fills");
            var reopened=new BrokerPositionService(directory,_=>throw new Exception("No inference or API request on status reads"));
            Check(reopened.Current().LastSynced==report.LastSynced&&reopened.Current().Positions.Single().Quantity==1,"derived positions persist across restarts separately from the trade journal");
            var stored=report with{LastAttempt=now.AddMinutes(-2)};File.WriteAllText(Path.Combine(directory,"positions.json"),JsonSerializer.Serialize(stored,CodexAnalysisProvider.Json));
            var failed=new BrokerPositionService(directory,_=>throw new HttpRequestException("PRIVATE transport error"));var old=await failed.Refresh(CancellationToken.None);
            Check(old.Status=="failed"&&old.LastSynced==report.LastSynced&&old.Positions.Single().Quantity==1&&!old.Message.Contains("PRIVATE"),"failed sync retains historical positions without claiming they were refreshed");
            File.WriteAllText(Path.Combine(directory,"positions.json"),JsonSerializer.Serialize(stored,CodexAnalysisProvider.Json));
            var replacement=new BrokerPositionService(directory,_=>Task.FromResult(new BrokerHistory(from,now,[new("fixture","Account 1",parsed.Select(f=>f with{Amount=2}).ToArray())])));
            Check((await replacement.Refresh(CancellationToken.None)).Positions.Single().Quantity==2,"full-window resync replaces corrected transactions instead of accumulating old versions");
            var builder=WebApplication.CreateSlimBuilder(new WebApplicationOptions{Args=[]});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
            await using var app=builder.Build();BrokerPositionService.Map(app,replacement);await app.StartAsync();
            try
            {
                var address=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.Single();
                using var client=new HttpClient();using var response=await client.PostAsync(address+"/api/schwab/positions/sync",null);
                Check(response.IsSuccessStatusCode&&response.Content.Headers.ContentType?.MediaType=="application/json","broker sync HTTP route returns JSON instead of discarding its result");
                using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Check(json.RootElement.GetProperty("positions")[0].GetProperty("quantity").GetInt32()==2,"broker sync HTTP payload contains the calculated position");
            }
            finally{await app.StopAsync();}
        }
        finally{Directory.Delete(directory,true);}
    }
}
