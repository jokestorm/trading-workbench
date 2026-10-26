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
        var daily=await reopened.PriceHistory("AAPL",daily:true);Check(daily.Length==1&&transport.DailyHistoryRequested,"daily history requests use daily frequency with a year window and authenticated transport");
        var broker=await reopened.FuturesTransactions();Check(broker.Accounts.Single().Fills.Single().Amount==1&&!JsonSerializer.Serialize(broker).Contains("fixture-private-hash")&&transport.AccountGets==2,"account sync uses read-only authenticated requests and keeps raw identifiers inside transport");
        var easternStart=TimeZoneInfo.ConvertTime(broker.From,TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        Check(easternStart.Year==TimeZoneInfo.ConvertTime(broker.Through,TimeZoneInfo.FindSystemTimeZoneById("America/New_York")).Year&&easternStart.Month==1&&easternStart.Day==1&&easternStart.TimeOfDay==TimeSpan.Zero&&transport.TransactionStart==broker.From,"transaction request covers the current year from January 1 midnight Eastern instead of a 59-day window");
        foreach(var operation in new Func<CancellationToken,Task>[] { token=>reopened.Quote("AAPL",token),token=>reopened.PriceHistory("/MESZ26",token),token=>reopened.FuturesTransactions(token) })
        {
            transport.Block=true;transport.Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellation=new CancellationTokenSource();var pending=operation(cancellation.Token);
            await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var waiting=new CancellationTokenSource();var queued=operation(waiting.Token);waiting.Cancel();
            var canceledWait=false;try{await queued.WaitAsync(TimeSpan.FromSeconds(5));}catch(OperationCanceledException){canceledWait=true;}
            cancellation.Cancel();var canceledHttp=false;try{await pending.WaitAsync(TimeSpan.FromSeconds(5));}catch(OperationCanceledException){canceledHttp=true;}
            transport.Block=false;
            await reopened.Status().WaitAsync(TimeSpan.FromSeconds(5));
            Check(canceledWait&&canceledHttp,"canceling market work stops queued and active HTTP requests and releases the Schwab lock");
        }
        var beforeBatch=transport.Calls;var batch=await reopened.Quotes(["AAPL","/MESZ26"]);
        Check(transport.Calls==beforeBatch+1&&batch.Length==1&&batch[0].Symbol=="AAPL","batched quotes make one authenticated request and preserve available symbols in a partial response");
        await reopened.Disconnect();Check(!JsonSerializer.SerializeToElement(await reopened.Status()).GetProperty("connected").GetBoolean(),"disconnect removes local tokens while keeping app settings");
        rejected=false;try{await reopened.Quote("AAPL");}catch(FormatException){rejected=true;}Check(rejected&&transport.Calls==7,"disconnected quote requests never reach Schwab");
        Console.WriteLine("All Schwab checks passed using simulated responses; live API access is unverified.");
    }
    private sealed class SimulatedSchwab:HttpMessageHandler
    {
        public int Calls {get;private set;}
        public bool Block {get;set;}
        public bool DailyHistoryRequested {get;private set;}
        public int AccountGets {get;private set;}
        public DateTimeOffset? TransactionStart {get;private set;}
        public TaskCompletionSource Entered {get;set;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(Block){Entered.TrySetResult();await Task.Delay(Timeout.Infinite,token);}
            Calls++;string json;
            if(Calls==1){var body=await request.Content!.ReadAsStringAsync(token);var form=QueryHelpers.ParseQuery(body);if(form["code"]!="fixture-code+encoded"||request.Headers.Authorization?.Scheme!="Basic")throw new Exception("Incorrect token request.");json="""{"access_token":"fixture-access","refresh_token":"fixture-refresh","expires_in":1} """;}
            else if(Calls==2){var form=QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(token));if(form["grant_type"]!="refresh_token"||form["refresh_token"]!="fixture-refresh")throw new Exception("Incorrect refresh request.");json="""{"access_token":"fixture-access-new","expires_in":1800} """;}
            else if(request.RequestUri!.AbsolutePath.StartsWith("/trader/v1/accounts"))
            {
                AccountGets++;if(request.Method!=HttpMethod.Get||request.Headers.Authorization?.Parameter!="fixture-access-new")throw new Exception("Account requests must be authenticated and read-only.");
                if(request.RequestUri.AbsolutePath.EndsWith("accountNumbers"))json="""[{"accountNumber":"fixture-private-number","hashValue":"fixture-private-hash"}]""";
                else
                {
                    var query=QueryHelpers.ParseQuery(request.RequestUri.Query);if(query["types"]!="TRADE"||!query.ContainsKey("startDate")||!query.ContainsKey("endDate"))throw new Exception("Bounded trade history required.");
                    TransactionStart=DateTimeOffset.Parse(query["startDate"].ToString(),System.Globalization.CultureInfo.InvariantCulture);
                    json=JsonSerializer.Serialize(new[]{new{activityId=1,orderId=2,time=DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"),status="VALID",transferItems=new[]{new{instrument=new{assetType="FUTURE",symbol="/MESZ26:XCME"},amount=1,price=100,positionEffect="OPENING"}}}});
                }
            }
            else if(request.RequestUri!.AbsolutePath=="/marketdata/v1/pricehistory")
            {
                var query=QueryHelpers.ParseQuery(request.RequestUri.Query);if(query["frequencyType"]!="daily"||query["periodType"]!="year"||query["frequency"]!="1"||request.Headers.Authorization?.Parameter!="fixture-access-new")throw new Exception("Incorrect daily request.");
                DailyHistoryRequested=true;json=JsonSerializer.Serialize(new{candles=new[]{new{datetime=DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeMilliseconds(),open=100,high=102,low=99,close=101,volume=100}}});
            }
            else {if(request.RequestUri!.AbsolutePath!="/marketdata/v1/quotes"||request.Headers.Authorization?.Parameter!="fixture-access-new")throw new Exception("Incorrect quote request.");json="""{"AAPL":{"assetMainType":"EQUITY","realtime":false,"quote":{"lastPrice":225.12,"bidPrice":225.1,"askPrice":225.15,"netChange":1.2,"netPercentChange":0.54,"quoteTime":1791302400000}}} """;}
            return new(HttpStatusCode.OK){Content=new StringContent(json)};
        }
    }
}
