using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace TradingWorkbench;

public record SchwabSettings(string AppKey,string AppSecret,string CallbackUrl);
public record SchwabCallback(string Url);
public record SchwabTokens(string AccessToken,string RefreshToken,DateTimeOffset AccessExpiresAt,DateTimeOffset AuthorizedAt);
public record SchwabVault(SchwabSettings Settings,SchwabTokens? Tokens);
public record MarketQuote(string Symbol,string AssetType,decimal? Last,decimal? Bid,decimal? Ask,decimal? Change,decimal? ChangePercent,DateTimeOffset? QuoteTime,bool? Realtime,DateTimeOffset FetchedAt)
{
    public string? ActiveContract {get;init;}
    public long? TotalVolume {get;init;}
    public DateTimeOffset? Expiration {get;init;}
}

// OAuth and market data are separate from journal persistence. No trading endpoints are used.
public sealed class SchwabConnection
{
    private readonly string path;
    private readonly IDataProtector protector;
    private readonly HttpClient http;
    private readonly SemaphoreSlim gate=new(1,1);
    private SchwabVault? vault;
    private string? state;
    private DateTimeOffset stateExpires;
    public SchwabConnection(string directory,IDataProtectionProvider protection,HttpClient http)
    {
        Directory.CreateDirectory(directory);path=Path.Combine(directory,"schwab.protected");
        protector=protection.CreateProtector("TradingWorkbench.Schwab.v1");this.http=http;
        if(File.Exists(path))
        {
            try{vault=JsonSerializer.Deserialize<SchwabVault>(protector.Unprotect(File.ReadAllText(path)));}
            catch(Exception error) when(error is CryptographicException or JsonException){throw new InvalidOperationException("Cannot unlock Schwab settings for this Windows user. Keep the file for recovery or remove schwab.protected to reconnect.");}
        }
    }
    private void Persist(SchwabVault next)
    {
        var temporary=path+".tmp";File.WriteAllText(temporary,protector.Protect(JsonSerializer.Serialize(next)));File.Move(temporary,path,true);vault=next;
    }
    public async Task<object> Status()
    {
        await gate.WaitAsync();try{return new{configured=vault is not null,connected=vault?.Tokens is not null,appKeySuffix=vault?.Settings.AppKey.Length>=4?vault.Settings.AppKey[^4..]:"",callbackUrl=vault?.Settings.CallbackUrl??"https://127.0.0.1:8182",accessExpiresAt=vault?.Tokens?.AccessExpiresAt,authorizedAt=vault?.Tokens?.AuthorizedAt,authorizationPending=state is not null&&stateExpires>DateTimeOffset.UtcNow};}finally{gate.Release();}
    }
    public async Task SaveSettings(SchwabSettings input)
    {
        await gate.WaitAsync();try{
            var key=input.AppKey?.Trim()??"";var secret=input.AppSecret?.Trim()??"";var callback=input.CallbackUrl?.Trim()??"";
            if(key.Length==0&&vault is not null)key=vault.Settings.AppKey;
            if(key.Length is <8 or >256||key.Any(char.IsWhiteSpace))throw new FormatException("Enter your Schwab Client ID.");
            if(!Uri.TryCreate(callback,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.IsLoopback||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0)throw new FormatException("Use the exact registered HTTPS loopback callback URL (127.0.0.1 or localhost), without query parameters.");
            if(secret.Length==0&&key==vault?.Settings.AppKey)secret=vault.Settings.AppSecret;
            if(secret.Length is <8 or >256||secret.Any(char.IsWhiteSpace))throw new FormatException("Enter your Schwab Client Secret. Leave it blank only when keeping the existing Client ID.");
            var settings=new SchwabSettings(key,secret,callback);Persist(new(settings,settings==vault?.Settings?vault.Tokens:null));state=null;
        }finally{gate.Release();}
    }
    public async Task<object> BeginAuthorization()
    {
        await gate.WaitAsync();try{
            if(vault is null)throw new FormatException("Save your app settings first.");
            state=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));stateExpires=DateTimeOffset.UtcNow.AddMinutes(10);
            var url=QueryHelpers.AddQueryString("https://api.schwabapi.com/v1/oauth/authorize",new Dictionary<string,string?>{{"client_id",vault.Settings.AppKey},{"redirect_uri",vault.Settings.CallbackUrl},{"response_type","code"},{"state",state}});
            return new{url,expiresAt=stateExpires};
        }finally{gate.Release();}
    }
    public async Task CompleteAuthorization(string returnedUrl)
    {
        await gate.WaitAsync();try{
            if(vault is null||state is null||DateTimeOffset.UtcNow>=stateExpires)throw new FormatException("Authorization has expired. Start a new authorization from this app.");
            if(string.IsNullOrWhiteSpace(returnedUrl)||returnedUrl.Length>8192||!Uri.TryCreate(returnedUrl.Trim(),UriKind.Absolute,out var uri))throw new FormatException("Paste the complete callback URL from the browser address bar.");
            var expected=new Uri(vault.Settings.CallbackUrl);
            if(uri.GetLeftPart(UriPartial.Path)!=expected.GetLeftPart(UriPartial.Path)||uri.Fragment.Length>0)throw new FormatException("The returned URL does not match the saved callback URL.");
            var query=QueryHelpers.ParseQuery(uri.Query);
            if(query["state"].Count!=1||!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(query["state"].ToString()),Encoding.UTF8.GetBytes(state)))throw new FormatException("Authorization state does not match. Use the link generated by this app.");
            if(query.ContainsKey("error")){state=null;throw new FormatException("Schwab authorization was declined. Start again when ready.");}
            if(query["code"].Count!=1||string.IsNullOrWhiteSpace(query["code"]))throw new FormatException("The callback URL has no authorization code.");
            // Consume the attempt before exchanging the one-time code, including failed exchanges.
            state=null;
            var tokens=await Exchange(new(){{"grant_type","authorization_code"},{"code",query["code"].ToString()},{"redirect_uri",vault.Settings.CallbackUrl}},null);
            Persist(vault with{Tokens=tokens});
        }finally{gate.Release();}
    }
    private async Task<SchwabTokens> Exchange(Dictionary<string,string> form,SchwabTokens? previous,CancellationToken token=default)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,"https://api.schwabapi.com/v1/oauth/token");
        request.Headers.Authorization=new("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(vault!.Settings.AppKey+":"+vault.Settings.AppSecret)));
        request.Content=new FormUrlEncodedContent(form);
        using var response=await http.SendAsync(request,token);
        if(!response.IsSuccessStatusCode)
        {
            if(previous is not null&&response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized){Persist(vault with{Tokens=null});throw new FormatException("Schwab authorization needs renewal. Authorize again.");}
            throw new FormatException($"Schwab token exchange failed (HTTP {(int)response.StatusCode}). Check app approval, credentials and callback URL, then authorize again.");
        }
        try{
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));var root=json.RootElement;
            var access=root.GetProperty("access_token").GetString();var refresh=root.TryGetProperty("refresh_token",out var r)?r.GetString():previous?.RefreshToken;
            var seconds=root.GetProperty("expires_in").GetInt32();
            if(string.IsNullOrWhiteSpace(access)||string.IsNullOrWhiteSpace(refresh)||seconds<=0)throw new JsonException();
            return new(access,refresh,DateTimeOffset.UtcNow.AddSeconds(seconds),previous?.AuthorizedAt??DateTimeOffset.UtcNow);
        }catch(Exception error) when(error is JsonException or InvalidOperationException or KeyNotFoundException){throw new FormatException("Schwab returned an unexpected token response. Authorize again.");}
    }
    public async Task Disconnect()
    {
        await gate.WaitAsync();try{if(vault is not null)Persist(vault with{Tokens=null});state=null;}finally{gate.Release();}
    }
    public async Task<MarketQuote> Quote(string symbol,CancellationToken token=default)
    {
        symbol=symbol?.Trim().ToUpperInvariant()??"";
        if(symbol.Length is <1 or >32||symbol.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not ('/' or '.' or '$' or '-' or ':')))throw new FormatException("Enter one equity or futures symbol.");
        await gate.WaitAsync(token);try{
            if(vault?.Tokens is null)throw new FormatException("Authorize Schwab first.");
            if(vault.Tokens.AccessExpiresAt<=DateTimeOffset.UtcNow.AddSeconds(60))Persist(vault with{Tokens=await Exchange(new(){{"grant_type","refresh_token"},{"refresh_token",vault.Tokens.RefreshToken}},vault.Tokens,token)});
            using var request=new HttpRequestMessage(HttpMethod.Get,QueryHelpers.AddQueryString("https://api.schwabapi.com/marketdata/v1/quotes","symbols",symbol));
            request.Headers.Authorization=new("Bearer",vault.Tokens!.AccessToken);
            using var response=await http.SendAsync(request,token);
            if(response.StatusCode==HttpStatusCode.Unauthorized){Persist(vault with{Tokens=null});throw new FormatException("Schwab rejected the access token. Authorize again.");}
            if(!response.IsSuccessStatusCode)throw new FormatException($"Schwab quote request failed (HTTP {(int)response.StatusCode}). Check Market Data access and the symbol.");
            try {using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));return ParseQuote(json.RootElement,symbol,DateTimeOffset.UtcNow);}
            catch(Exception error) when(error is JsonException or InvalidOperationException or ArgumentOutOfRangeException){throw new FormatException("Schwab returned an unexpected quote response.");}
        }finally{gate.Release();}
    }
    public async Task<PriceBar[]> PriceHistory(string symbol,CancellationToken token=default)
    {
        symbol=MarketStore.Symbol(symbol);await gate.WaitAsync(token);try{
            if(vault?.Tokens is null)throw new FormatException("Authorize Schwab first.");
            if(vault.Tokens.AccessExpiresAt<=DateTimeOffset.UtcNow.AddSeconds(60))Persist(vault with{Tokens=await Exchange(new(){{"grant_type","refresh_token"},{"refresh_token",vault.Tokens.RefreshToken}},vault.Tokens,token)});
            var now=DateTimeOffset.UtcNow;
            var url=QueryHelpers.AddQueryString("https://api.schwabapi.com/marketdata/v1/pricehistory",new Dictionary<string,string?>{{"symbol",symbol},{"periodType","day"},{"frequencyType","minute"},{"frequency","1"},{"startDate",now.AddDays(-7).ToUnixTimeMilliseconds().ToString()},{"endDate",now.ToUnixTimeMilliseconds().ToString()},{"needExtendedHoursData","true"}});
            using var request=new HttpRequestMessage(HttpMethod.Get,url);request.Headers.Authorization=new("Bearer",vault.Tokens!.AccessToken);using var response=await http.SendAsync(request,token);
            if(response.StatusCode==HttpStatusCode.Unauthorized){Persist(vault with{Tokens=null});throw new FormatException("Schwab authorization needs renewal. Authorize again.");}
            if(!response.IsSuccessStatusCode)throw new FormatException($"Schwab history request failed (HTTP {(int)response.StatusCode}). Historical data may be unsupported for this symbol; use quote snapshots instead.");
            try {using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));return ParseHistory(json.RootElement,symbol,now);}catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentOutOfRangeException){throw new FormatException("Unexpected historical data from Schwab. No bars saved.");}
        }finally{gate.Release();}
    }
    public static PriceBar[] ParseHistory(JsonElement root,string symbol,DateTimeOffset fetched)
    {
        if(!root.TryGetProperty("candles",out var candles)||candles.ValueKind!=JsonValueKind.Array)throw new FormatException("Schwab returned no candle array for this symbol.");
        return candles.EnumerateArray().Select(c=>new PriceBar(symbol,DateTimeOffset.FromUnixTimeMilliseconds(c.GetProperty("datetime").GetInt64()),c.GetProperty("open").GetDecimal(),c.GetProperty("high").GetDecimal(),c.GetProperty("low").GetDecimal(),c.GetProperty("close").GetDecimal(),c.GetProperty("volume").GetInt64())).Where(b=>b.Time.AddMinutes(1)<=fetched).OrderBy(b=>b.Time).ToArray();
    }
    public static MarketQuote ParseQuote(JsonElement root,string symbol,DateTimeOffset fetched)
    {
        if(!root.TryGetProperty(symbol,out var item)||!item.TryGetProperty("quote",out var quote))throw new FormatException("No quote returned for that symbol. Futures symbols and entitlements must be verified with Schwab.");
        decimal? Number(string key)=>quote.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetDecimal(out var n)?n:null;
        DateTimeOffset? time=quote.TryGetProperty("quoteTime",out var timestamp)&&timestamp.TryGetInt64(out var ms)?DateTimeOffset.FromUnixTimeMilliseconds(ms):null;
        bool? realtime=item.TryGetProperty("realtime",out var flag)&&flag.ValueKind is JsonValueKind.True or JsonValueKind.False?flag.GetBoolean():null;
        var reference=item.TryGetProperty("reference",out var refValue)?refValue:default;
        string? active=reference.ValueKind==JsonValueKind.Object&&reference.TryGetProperty("futureActiveSymbol",out var activeValue)&&activeValue.ValueKind==JsonValueKind.String?activeValue.GetString():null;
        DateTimeOffset? expiration=reference.ValueKind==JsonValueKind.Object&&reference.TryGetProperty("futureExpirationDate",out var expiryValue)&&expiryValue.TryGetInt64(out var expiryMs)&&expiryMs>0?DateTimeOffset.FromUnixTimeMilliseconds(expiryMs):null;
        return new(symbol,item.TryGetProperty("assetMainType",out var type)?type.GetString()??"Unknown":"Unknown",Number("lastPrice"),Number("bidPrice"),Number("askPrice"),Number("netChange"),Number("netPercentChange"),time,realtime,fetched){ActiveContract=active,Expiration=expiration,TotalVolume=quote.TryGetProperty("totalVolume",out var volumeValue)&&volumeValue.TryGetInt64(out var volume)?volume:null};
    }
}

public static class SchwabRoutes
{
    public static void Map(WebApplication app,SchwabConnection connection)
    {
        async Task<IResult> Run(Func<Task<object?>> action){try{return Results.Ok(await action());}catch(FormatException error){return Results.BadRequest(new{message=error.Message});}catch(Exception error) when(error is HttpRequestException or TaskCanceledException){return Results.Json(new{message="Cannot reach Schwab right now. Check connectivity and try again."},statusCode:502);}}
        app.MapGet("/api/schwab/status",()=>connection.Status());
        app.MapPost("/api/schwab/settings",(SchwabSettings settings)=>Run(async()=>{await connection.SaveSettings(settings);return await connection.Status();}));
        app.MapPost("/api/schwab/authorize",()=>Run(async()=>await connection.BeginAuthorization()));
        app.MapPost("/api/schwab/callback",(SchwabCallback callback)=>Run(async()=>{await connection.CompleteAuthorization(callback.Url);return await connection.Status();}));
        app.MapPost("/api/schwab/disconnect",()=>Run(async()=>{await connection.Disconnect();return await connection.Status();}));
        app.MapPost("/api/schwab/quote",(QuoteRequest input)=>Run(async()=>await connection.Quote(input.Symbol)));
    }
}
public record QuoteRequest(string Symbol);
