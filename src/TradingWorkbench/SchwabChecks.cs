using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace TradingWorkbench;

public static class SchwabChecks
{
    public static async Task Run()
    {
        void Check(bool result,string message){if(!result)throw new Exception(message);Console.WriteLine("PASS "+message);}
        var directory=Path.Combine(Path.GetTempPath(),"schwab-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var provider=DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(directory,"keys")),builder=>{builder.SetApplicationName("SchwabChecks");if(OperatingSystem.IsWindows())builder.ProtectKeysWithDpapi();});
        var transport=new SimulatedSchwab();var http=new HttpClient(transport);var connection=new SchwabConnection(directory,provider,http);
        await connection.SaveSettings(new("fixture-app-key","fixture-app-secret","https://127.0.0.1:8182"));
        var authorization=JsonSerializer.SerializeToElement(await connection.BeginAuthorization());var authUri=new Uri(authorization.GetProperty("url").GetString()!);var query=QueryHelpers.ParseQuery(authUri.Query);
        Check(query["client_id"]=="fixture-app-key"&&query["redirect_uri"]=="https://127.0.0.1:8182"&&query["state"].ToString().Length==64,"OAuth link contains registered callback and random state");
        var rejected=false;try{await connection.CompleteAuthorization("https://127.0.0.1:8182?code=fixture&state=wrong");}catch(FormatException){rejected=true;}Check(rejected&&transport.Calls==0,"wrong OAuth state cannot exchange a code");
        var callback=QueryHelpers.AddQueryString("https://127.0.0.1:8182",new Dictionary<string,string?>{{"code","fixture-code+encoded"},{"state",query["state"]}});
        await connection.CompleteAuthorization(callback);Check(transport.Calls==1,"authorization code is decoded once and exchanged server-side");
        rejected=false;try{await connection.CompleteAuthorization(callback);}catch(FormatException){rejected=true;}Check(rejected&&transport.Calls==1,"authorization callbacks cannot be replayed");
        var stored=File.ReadAllText(Path.Combine(directory,"schwab.protected"));var status=JsonSerializer.Serialize(await connection.Status());Check(!stored.Contains("fixture-app-secret")&&!stored.Contains("fixture-access")&&!status.Contains("fixture-app-secret")&&!status.Contains("fixture-refresh"),"stored secrets are protected and status does not expose tokens");
        var reopened=new SchwabConnection(directory,provider,http);var quote=await reopened.Quote("aapl");Check(transport.Calls==3&&quote.Last==225.12m&&quote.Realtime==false&&quote.QuoteTime.HasValue,"reopened connection refreshes expiring token and preserves delayed quote timestamps");
        using var futures=JsonDocument.Parse("""{"/MESZ26":{"assetMainType":"FUTURE","quote":{"lastPrice":6000.25,"netChange":null,"quoteTime":1791302400000}}} """);var future=SchwabConnection.ParseQuote(futures.RootElement,"/MESZ26",DateTimeOffset.UtcNow);Check(future.AssetType=="FUTURE"&&future.Last==6000.25m&&future.Realtime is null&&future.Change is null,"futures quote parsing leaves missing realtime/change values unknown");
        rejected=false;try{await reopened.SaveSettings(new("fixture-app-key","","https://example.com/callback"));}catch(FormatException){rejected=true;}Check(rejected,"callback settings only accept HTTPS loopback URLs");
        await reopened.SaveSettings(new("fixture-app-key","","https://127.0.0.1:8182"));Check(JsonSerializer.SerializeToElement(await reopened.Status()).GetProperty("connected").GetBoolean(),"unchanged settings preserve authorization with a blank secret field");
        await reopened.SaveSettings(new("","","https://127.0.0.1:8182"));Check(JsonSerializer.SerializeToElement(await reopened.Status()).GetProperty("connected").GetBoolean(),"blank app key and secret keep existing encrypted settings");
        await reopened.Disconnect();Check(!JsonSerializer.SerializeToElement(await reopened.Status()).GetProperty("connected").GetBoolean(),"disconnect removes local tokens while keeping app settings");
        rejected=false;try{await reopened.Quote("AAPL");}catch(FormatException){rejected=true;}Check(rejected&&transport.Calls==3,"disconnected quote requests never reach Schwab");
        Console.WriteLine("All Schwab checks passed using simulated responses; live API access is unverified.");
    }
    private sealed class SimulatedSchwab:HttpMessageHandler
    {
        public int Calls {get;private set;}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;string json;
            if(Calls==1){var body=await request.Content!.ReadAsStringAsync(token);var form=QueryHelpers.ParseQuery(body);if(form["code"]!="fixture-code+encoded"||request.Headers.Authorization?.Scheme!="Basic")throw new Exception("Incorrect token request.");json="""{"access_token":"fixture-access","refresh_token":"fixture-refresh","expires_in":1} """;}
            else if(Calls==2){var form=QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(token));if(form["grant_type"]!="refresh_token"||form["refresh_token"]!="fixture-refresh")throw new Exception("Incorrect refresh request.");json="""{"access_token":"fixture-access-new","expires_in":1800} """;}
            else {if(request.RequestUri!.AbsolutePath!="/marketdata/v1/quotes"||request.Headers.Authorization?.Parameter!="fixture-access-new")throw new Exception("Incorrect quote request.");json="""{"AAPL":{"assetMainType":"EQUITY","realtime":false,"quote":{"lastPrice":225.12,"bidPrice":225.1,"askPrice":225.15,"netChange":1.2,"netPercentChange":0.54,"quoteTime":1791302400000}}} """;}
            return new(HttpStatusCode.OK){Content=new StringContent(json)};
        }
    }
}
