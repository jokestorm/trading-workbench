using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace TradingWorkbench;

public record BrokerFill(string Id,string Order,string Symbol,DateTimeOffset Time,decimal Amount,decimal Price,string Effect,decimal? Fees=null,decimal UnclassifiedFees=0)
{
    public bool CanReplace(BrokerFill previous)=>this==previous||
        (this with{Fees=previous.Fees,UnclassifiedFees=previous.UnclassifiedFees}==previous&&
        (previous.Fees is null||(previous.UnclassifiedFees==0&&UnclassifiedFees>0&&Fees-UnclassifiedFees==previous.Fees)));
}
public record BrokerAccountHistory(string Id,string Label,BrokerFill[] Fills);
public record BrokerHistory(DateTimeOffset From,DateTimeOffset Through,BrokerAccountHistory[] Accounts);
public record DerivedPosition(string AccountId,string AccountLabel,string Symbol,string State,int? Quantity,decimal? AverageEntry,DateTimeOffset? LastFill,int FillCount,string Message);
public record BrokerPositionReport(DateTimeOffset? LastSynced,DateTimeOffset? HistoryFrom,DateTimeOffset? HistoryThrough,DerivedPosition[] Positions,string Status,string Message,DateTimeOffset? LastAttempt=null);

public sealed partial class SchwabConnection
{
    private async Task<JsonDocument> AccountGet(string path,CancellationToken token)
    {
        if(vault?.Tokens is null)throw new FormatException("Sign in to Schwab with Accounts and Trading access first.");
        if(vault.Tokens.AccessExpiresAt<=DateTimeOffset.UtcNow.AddSeconds(60))Persist(vault with{Tokens=await Exchange(new(){{"grant_type","refresh_token"},{"refresh_token",vault.Tokens.RefreshToken}},vault.Tokens,token)});
        using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.schwabapi.com"+path);
        request.Headers.Authorization=new("Bearer",vault.Tokens!.AccessToken);
        using var response=await http.SendAsync(request,token);
        if(!response.IsSuccessStatusCode)throw new FormatException($"Schwab account request failed (HTTP {(int)response.StatusCode}). Check Accounts and Trading approval and sign in again if needed.");
        try{return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));}catch(JsonException){throw new FormatException("Schwab returned an invalid account response.");}
    }
    public async Task<BrokerHistory> FuturesTransactions(CancellationToken token=default)
    {
        await gate.WaitAsync(token);
        try
        {
            var now=DateTimeOffset.UtcNow;
            var eastern=TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            var year=TimeZoneInfo.ConvertTime(now,eastern).Year;
            var from=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(new DateTime(year,1,1),eastern));
            using var linked=await AccountGet("/trader/v1/accounts/accountNumbers",token);
            if(linked.RootElement.ValueKind!=JsonValueKind.Array||linked.RootElement.GetArrayLength()>10)throw new FormatException("Unexpected linked-account response.");
            var accounts=new List<BrokerAccountHistory>();
            foreach(var item in linked.RootElement.EnumerateArray())
            {
                if(!item.TryGetProperty("hashValue",out var hashValue)||hashValue.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(hashValue.GetString()))throw new FormatException("Schwab returned no account identifier.");
                var hash=hashValue.GetString()!;
                var path=QueryHelpers.AddQueryString($"/trader/v1/accounts/{Uri.EscapeDataString(hash)}/transactions",new Dictionary<string,string?>{{"startDate",from.ToString("yyyy-MM-ddTHH:mm:ssZ")},{"endDate",now.ToString("yyyy-MM-ddTHH:mm:ssZ")},{"types","TRADE"}});
                using var transactions=await AccountGet(path,token);
                // Raw account identifiers remain inside this transport boundary.
                var id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hash)));
                accounts.Add(new(id,$"Account {accounts.Count+1}",BrokerPositionCalculator.Parse(transactions.RootElement)));
            }
            return new(from,now,accounts.ToArray());
        }
        finally{gate.Release();}
    }
}

public static class BrokerPositionCalculator
{
    public static BrokerFill[] Parse(JsonElement root)
    {
        try{return ParseCore(root);}catch(Exception error) when(error is InvalidOperationException or KeyNotFoundException or OverflowException){throw new FormatException("Unexpected futures transaction fields; positions were not updated.");}
    }
    private static BrokerFill[] ParseCore(JsonElement root)
    {
        if(root.ValueKind!=JsonValueKind.Array||root.GetArrayLength()>=3000)throw new FormatException("Transaction history may be incomplete; positions were not updated.");
        var fills=new Dictionary<string,BrokerFill>();
        string Id(JsonElement item,string name)=>item.TryGetProperty(name,out var value)&&value.ValueKind is JsonValueKind.String or JsonValueKind.Number?value.ToString():"";
        foreach(var tx in root.EnumerateArray())
        {
            if(!tx.TryGetProperty("transferItems",out var items)||items.ValueKind!=JsonValueKind.Array)throw new FormatException("Missing transaction items; positions were not updated.");
            var futures=items.EnumerateArray().Where(item=>item.TryGetProperty("instrument",out var instrument)&&instrument.TryGetProperty("assetType",out var asset)&&asset.GetString()=="FUTURE").ToArray();
            if(futures.Length==0)continue;
            var feeItems=items.EnumerateArray().Where(item=>item.TryGetProperty("feeType",out var fee)&&fee.ValueKind==JsonValueKind.String).ToArray();
            decimal? fees=feeItems.Length==0?null:0m;
            foreach(var item in feeItems)
            {
                if(item.GetProperty("instrument").GetProperty("assetType").GetString()!="CURRENCY"||item.GetProperty("instrument").GetProperty("symbol").GetString()!="CURRENCY_USD"||!item.TryGetProperty("cost",out var cost)||!cost.TryGetDecimal(out var value)||value>0){fees=null;break;}
                fees-=value;
            }
            // Schwab omits feeType on some USD trade charges. Their debit cost
            // and matching positive amount distinguish them from cash proceeds.
            decimal unclassified=0;
            foreach(var item in items.EnumerateArray().Where(item=>!item.TryGetProperty("feeType",out var fee)||fee.ValueKind==JsonValueKind.Null))
            {
                var instrument=item.GetProperty("instrument");
                if(instrument.GetProperty("assetType").GetString()!="CURRENCY")continue;
                if(!item.TryGetProperty("cost",out var cost)||!cost.TryGetDecimal(out var value)||value==0)continue;
                if(instrument.GetProperty("symbol").GetString()!="CURRENCY_USD"||value>0||!item.TryGetProperty("amount",out var charge)||!charge.TryGetDecimal(out var amount)||amount!=-value){fees=null;break;}
                unclassified-=value;
            }
            if(futures.Any(item=>!item.TryGetProperty("amount",out var value)||!value.TryGetDecimal(out var number)||number==0||Math.Abs(number)>10000))throw new FormatException("Invalid futures quantity.");
            if(futures.Any(item=>item.TryGetProperty("fee",out var inline)&&inline.ValueKind!=JsonValueKind.Null&&(!inline.TryGetDecimal(out var value)||value!=0)))fees=null;
            var totalQuantity=futures.Sum(item=>Math.Abs(item.GetProperty("amount").GetDecimal()));var remainingFees=fees;var remainingUnclassified=unclassified;
            if(!tx.TryGetProperty("status",out var status)||status.GetString()!="VALID")throw new FormatException("A futures transaction has an unresolved status; check the broker before using derived positions.");
            var activity=Id(tx,"activityId");var order=Id(tx,"orderId");
            if(activity.Length==0)throw new FormatException("A futures transaction has no stable identifier.");
            var timeText=Id(tx,"time");
            // Schwab uses ISO dates with offsets such as +0000.
            if(timeText.Length>=5&&timeText[^5] is '+' or '-'&&timeText[^3]!=':')timeText=timeText.Insert(timeText.Length-2,":");
            if(!DateTimeOffset.TryParse(timeText,CultureInfo.InvariantCulture,DateTimeStyles.None,out var time))throw new FormatException("Invalid futures transaction time.");
            for(var index=0;index<futures.Length;index++)
            {
                var item=futures[index];var instrument=item.GetProperty("instrument");
                var symbol=MarketStore.Symbol((instrument.GetProperty("symbol").GetString()??"").Split(':')[0]);
                if(!item.TryGetProperty("amount",out var amountValue)||!amountValue.TryGetDecimal(out var amount)||amount==0||amount!=decimal.Truncate(amount)||Math.Abs(amount)>10000||!item.TryGetProperty("price",out var priceValue)||!priceValue.TryGetDecimal(out var price)||price<=0)throw new FormatException("Invalid futures fill quantity or price.");
                var effect=Id(item,"positionEffect");if(effect is not ("OPENING" or "CLOSING"))throw new FormatException("Unknown futures position effect.");
                var allocated=fees is null?(decimal?)null:index==futures.Length-1?remainingFees:Math.Min(remainingFees!.Value,decimal.Round(fees.Value*Math.Abs(amount)/totalQuantity,2,MidpointRounding.AwayFromZero));remainingFees-=allocated;
                var other=index==futures.Length-1?remainingUnclassified:Math.Min(remainingUnclassified,decimal.Round(unclassified*Math.Abs(amount)/totalQuantity,2,MidpointRounding.AwayFromZero));remainingUnclassified-=other;
                var fill=new BrokerFill(activity+":"+index,order.Length>0?order:activity,symbol,time,amount,price,effect,allocated+other,other);
                if(fills.TryGetValue(fill.Id,out var existing)&&existing!=fill)throw new FormatException("Conflicting versions of a futures transaction.");
                fills[fill.Id]=fill;
            }
        }
        return fills.Values.OrderBy(f=>f.Time).ThenBy(f=>f.Id,StringComparer.Ordinal).ToArray();
    }
    public static DerivedPosition[] Calculate(BrokerHistory history)=>history.Accounts.SelectMany(account=>account.Fills.GroupBy(f=>f.Symbol).Select(group=>Calculate(account,group.Key,group.ToArray()))).OrderBy(p=>p.AccountLabel).ThenBy(p=>p.Symbol).ToArray();
    private static DerivedPosition Calculate(BrokerAccountHistory account,string symbol,BrokerFill[] fills)
    {
        decimal quantity=0,average=0;
        DerivedPosition Unknown(string message)=>new(account.Id,account.Label,symbol,"UNKNOWN",null,null,fills.Max(f=>f.Time),fills.Length,message);
        // Evaluate equal-time orders together: response order is not execution order.
        foreach(var bucket in fills.GroupBy(f=>f.Time).OrderBy(g=>g.Key))
        {
            var orders=bucket.GroupBy(f=>f.Order).Select(g=>g.OrderBy(f=>f.Effect=="CLOSING"?0:1).ThenBy(f=>f.Id,StringComparer.Ordinal).ToArray()).ToArray();
            if(orders.Length>6)return Unknown("Too many simultaneous orders to establish fill order. Enter your position manually.");
            var outcomes=new HashSet<(decimal Quantity,decimal Average)>();
            void Replay(decimal qty,decimal avg,List<BrokerFill[]> remaining)
            {
                if(remaining.Count==0){outcomes.Add((qty,avg));return;}
                for(var i=0;i<remaining.Count;i++)
                {
                    var q=qty;var a=avg;var valid=true;
                    foreach(var fill in remaining[i])
                    {
                        if(fill.Effect=="CLOSING")
                        {
                            if(q==0||Math.Sign(q)==Math.Sign(fill.Amount)||Math.Abs(fill.Amount)>Math.Abs(q)){valid=false;break;}
                        }
                        else
                        {
                            if(q!=0&&Math.Sign(q)!=Math.Sign(fill.Amount)){valid=false;break;}
                            a=(Math.Abs(q)*a+Math.Abs(fill.Amount)*fill.Price)/(Math.Abs(q)+Math.Abs(fill.Amount));
                        }
                        q+=fill.Amount;if(q==0)a=0;
                    }
                    if(valid)Replay(q,a,remaining.Where((_,index)=>index!=i).ToList());
                }
            }
            Replay(quantity,average,orders.ToList());
            if(outcomes.Count!=1)return Unknown(outcomes.Count==0?"History starts mid-position or fills do not reconcile. Enter your position manually.":"Simultaneous fills have ambiguous ordering. Enter your position manually.");
            (quantity,average)=outcomes.Single();
        }
        if(Math.Abs(quantity)>10000)return Unknown("Derived quantity is outside the supported range.");
        return new(account.Id,account.Label,symbol,quantity>0?"LONG":quantity<0?"SHORT":"FLAT",quantity==0?null:(int)Math.Abs(quantity),quantity==0?null:average,fills.Max(f=>f.Time),fills.Length,"Estimate from available transactions, assuming flat before the first fill. Verify with your broker; recent fills may not be posted yet.");
    }
}

public sealed class BrokerPositionService
{
    private readonly string path;private readonly Func<CancellationToken,Task<BrokerHistory>> load;
    private readonly SemaphoreSlim gate=new(1,1);private readonly object sync=new();
    private BrokerPositionReport report;
    public BrokerPositionService(string directory,Func<CancellationToken,Task<BrokerHistory>> load)
    {
        Directory.CreateDirectory(directory);path=Path.Combine(directory,"positions.json");this.load=load;
        report=File.Exists(path)?JsonSerializer.Deserialize<BrokerPositionReport>(File.ReadAllText(path),CodexAnalysisProvider.Json)!:new(null,null,null,[],"waiting","Sync Schwab transactions to estimate futures positions.");
    }
    public BrokerPositionReport Current(){lock(sync)return report;}
    public async Task<BrokerPositionReport> Refresh(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var previous=Current();if(previous.LastAttempt is not null&&DateTimeOffset.UtcNow-previous.LastAttempt<TimeSpan.FromSeconds(25))return previous;
            var attempt=DateTimeOffset.UtcNow;
            BrokerPositionReport next;
            try
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var history=await load(timeout.Token);token.ThrowIfCancellationRequested();
                next=new(DateTimeOffset.UtcNow,history.From,history.Through,BrokerPositionCalculator.Calculate(history),"ready","Transaction-derived estimates. Last fill time is separate from sync time; verify before using.",attempt);
            }
            catch(Exception error) when(error is FormatException or HttpRequestException or TaskCanceledException)
            {
                token.ThrowIfCancellationRequested();next=previous with{Status="failed",LastAttempt=attempt,Message=error is FormatException?error.Message:"Could not sync Schwab transactions. Previous estimates remain historical."};
            }
            token.ThrowIfCancellationRequested();
            lock(sync){File.WriteAllText(path+".tmp",JsonSerializer.Serialize(next,CodexAnalysisProvider.Json));File.Move(path+".tmp",path,true);report=next;return report;}
        }
        finally{gate.Release();}
    }
    public static void Map(WebApplication app,BrokerPositionService service)
    {
        app.MapGet("/api/schwab/positions",()=>service.Current());
        app.MapPost("/api/schwab/positions/sync",async(CancellationToken token)=>Results.Ok(await service.Refresh(token)));
    }
}
