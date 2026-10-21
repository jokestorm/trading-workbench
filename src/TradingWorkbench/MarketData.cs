using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TradingWorkbench;
public record PriceBar(string Symbol,DateTimeOffset Time,decimal Open,decimal High,decimal Low,decimal Close,long Volume);
public record WatchlistRequest(string[] Symbols);
public record HistoryRequest(string Symbol);
public sealed class MarketStore
{
    private readonly string connectionString;
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public MarketStore(string directory){Directory.CreateDirectory(directory);connectionString=new SqliteConnectionStringBuilder{DataSource=Path.Combine(directory,"market.db")}.ToString();using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="CREATE TABLE IF NOT EXISTS settings(id TEXT PRIMARY KEY,document TEXT NOT NULL); CREATE TABLE IF NOT EXISTS quotes(symbol TEXT NOT NULL,id TEXT NOT NULL,time TEXT NOT NULL,document TEXT NOT NULL,PRIMARY KEY(symbol,id)); CREATE TABLE IF NOT EXISTS bars(symbol TEXT NOT NULL,time TEXT NOT NULL,document TEXT NOT NULL,PRIMARY KEY(symbol,time));";cmd.ExecuteNonQuery();}
    private SqliteConnection Open(){var db=new SqliteConnection(connectionString);db.Open();return db;}
    public static string Symbol(string? symbol){var value=symbol?.Trim().ToUpperInvariant()??"";if(value.Length is <1 or >32||value.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not ('/' or '.' or '$' or '-' or ':')))throw new FormatException("Enter one valid equity or futures symbol.");return value;}
    public string[] Watchlist(){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT document FROM settings WHERE id='watchlist'";return cmd.ExecuteScalar() is string json?JsonSerializer.Deserialize<string[]>(json)!:["/MES","/MGC","/MCL"];}
    public void SaveWatchlist(string[] symbols){if(symbols is null||symbols.Length>10)throw new FormatException("Use at most 10 watchlist symbols.");var normalized=symbols.Select(Symbol).Select(s=>s is "MES" or "MGC" or "MCL"?"/"+s:s).Distinct().ToArray();using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO settings VALUES('watchlist',$json) ON CONFLICT(id) DO UPDATE SET document=excluded.document";cmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(normalized));cmd.ExecuteNonQuery();}
    public void SaveQuote(MarketQuote quote){var identity=JsonSerializer.Serialize(quote with{FetchedAt=DateTimeOffset.UnixEpoch},Json);var id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO quotes VALUES($symbol,$id,$time,$json) ON CONFLICT(symbol,id) DO UPDATE SET document=excluded.document";cmd.Parameters.AddWithValue("$symbol",quote.Symbol);cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$time",(quote.QuoteTime??quote.FetchedAt).ToUniversalTime().ToString("O"));cmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(quote,Json));cmd.ExecuteNonQuery();}
    public void SaveBars(PriceBar[] bars){using var db=Open();using var tx=db.BeginTransaction();foreach(var bar in bars){if(bar.High<Math.Max(bar.Open,bar.Close)||bar.Low>Math.Min(bar.Open,bar.Close)||bar.High<bar.Low||bar.Volume<0)throw new FormatException("Invalid OHLCV bar returned by Schwab.");using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO bars VALUES($symbol,$time,$json) ON CONFLICT(symbol,time) DO UPDATE SET document=excluded.document";cmd.Parameters.AddWithValue("$symbol",bar.Symbol);cmd.Parameters.AddWithValue("$time",bar.Time.ToUniversalTime().ToString("O"));cmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(bar,Json));cmd.ExecuteNonQuery();}tx.Commit();}
    private T[] Read<T>(string table,string symbol,int days){if(days is not (1 or 3 or 7))throw new FormatException("Choose 1, 3 or 7 days.");using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText=$"SELECT document FROM {table} WHERE symbol=$symbol AND time>=$start ORDER BY time DESC LIMIT 12000";cmd.Parameters.AddWithValue("$symbol",Symbol(symbol));cmd.Parameters.AddWithValue("$start",DateTimeOffset.UtcNow.AddDays(-days).ToString("O"));using var reader=cmd.ExecuteReader();var rows=new List<T>();while(reader.Read())rows.Add(JsonSerializer.Deserialize<T>(reader.GetString(0),Json)!);rows.Reverse();return rows.ToArray();}
    public MarketQuote[] Quotes(string symbol,int days=7)=>Read<MarketQuote>("quotes",symbol,days);
    public PriceBar[] Bars(string symbol,int days=7)=>Read<PriceBar>("bars",symbol,days);
    public FollowupPoint[] Followup(string symbol,string source,DateTimeOffset from)
    {
        // Bounded historical window remains queryable even after the dashboard's rolling range advances.
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText=$"SELECT document FROM {(source=="bars"?"bars":"quotes")} WHERE symbol=$symbol AND time>=$start AND time<=$end ORDER BY time";cmd.Parameters.AddWithValue("$symbol",Symbol(symbol));cmd.Parameters.AddWithValue("$start",from.AddMinutes(-1).ToUniversalTime().ToString("O"));cmd.Parameters.AddWithValue("$end",from.AddMinutes(40).ToUniversalTime().ToString("O"));using var reader=cmd.ExecuteReader();var points=new List<FollowupPoint>();while(reader.Read()){if(source=="bars"){var b=JsonSerializer.Deserialize<PriceBar>(reader.GetString(0),Json)!;points.Add(new(b.Time.AddMinutes(1),b.Close,b.High,b.Low,b.Time));}else{var q=JsonSerializer.Deserialize<MarketQuote>(reader.GetString(0),Json)!;if(q.Last.HasValue&&q.QuoteTime.HasValue)points.Add(new(q.QuoteTime.Value,q.Last.Value,Realtime:q.Realtime));}}return points.OrderBy(p=>p.Time).ToArray();
    }
    public object Snapshot(string symbol,string source,int days=7){symbol=Symbol(symbol);if(source=="bars"){var bars=Bars(symbol,days).TakeLast(180).ToArray();if(bars.Length<5)throw new FormatException("Load at least five historical bars for this symbol first.");return new{kind="market",symbol,provenance="Schwab historical OHLCV bars, closed one-minute bars only. Gaps are not filled; history is not a guarantee of realtime entitlement.",windowDays=days,limit="Latest 180 bars in the selected rolling window",timeframe="1 minute",asOf=bars[^1].Time.AddMinutes(1),observationAgeSeconds=Math.Max(0,(DateTimeOffset.UtcNow-bars[^1].Time.AddMinutes(1)).TotalSeconds),createdAt=DateTimeOffset.UtcNow,bars,technicals=TechnicalIndicators.Calculate(bars) with{Series=[]},features=new{count=bars.Length,changeFromFirstClose=bars[^1].Close-bars[0].Close,lastClose=bars[^1].Close,fiveBarMean=bars.TakeLast(5).Average(b=>b.Close),rangeHigh=bars.Max(b=>b.High),rangeLow=bars.Min(b=>b.Low)}};}
        if(source!="quotes")throw new FormatException("Choose bars or quotes.");var quotes=Quotes(symbol,days).Where(q=>q.Last.HasValue).TakeLast(180).ToArray();if(quotes.Length<5)throw new FormatException("Capture at least five distinct priced quote snapshots first.");return new{kind="market",symbol,provenance="Schwab sampled last-price quotes, NOT OHLCV bars. Irregular sampling; no observed path between samples. Delayed/unknown flags and quote times must be respected.",windowDays=days,limit="Latest 180 priced samples in the selected rolling window",timeframe="Irregular quote snapshots",asOf=quotes[^1].QuoteTime??quotes[^1].FetchedAt,observationAgeSeconds=Math.Max(0,(DateTimeOffset.UtcNow-(quotes[^1].QuoteTime??quotes[^1].FetchedAt)).TotalSeconds),createdAt=DateTimeOffset.UtcNow,quotes,features=new{count=quotes.Length,lastPrice=quotes[^1].Last,changeFromFirstSample=quotes[^1].Last-quotes[0].Last}};
    }
}
public static class MarketRoutes
{
    public static async Task<MarketQuote> RefreshForAnalysis(MarketStore store,SchwabConnection connection,AnalysisRequest request,CancellationToken token)
    {
        if(request.Source=="bars")
        {
            var bars=await connection.PriceHistory(request.Symbol!,token);token.ThrowIfCancellationRequested();store.SaveBars(bars);
        }
        var quote=await connection.Quote(request.Symbol!,token);token.ThrowIfCancellationRequested();store.SaveQuote(quote);return quote;
    }
    public static void Map(WebApplication app,MarketStore store,SchwabConnection connection,TimeframeService? timeframes=null){
        var contracts=new FutureContracts(connection);
        async Task<IResult> Run(Func<Task<object>> action){try{return Results.Ok(await action());}catch(FormatException e){return Results.BadRequest(new{message=e.Message});}catch(Exception e) when(e is HttpRequestException or TaskCanceledException){return Results.Json(new{message="Schwab request failed. Saved data remains available; try again."},statusCode:502);}}
        app.MapGet("/api/market/watchlist",()=>store.Watchlist());
        app.MapGet("/api/market/contracts",()=>Run(async()=>{var selections=new List<ContractSelection>();foreach(var symbol in store.Watchlist()){try{selections.Add(new(symbol,await contracts.Resolve(symbol),FutureContracts.IsRoot(symbol)));}catch(Exception e) when(e is FormatException or HttpRequestException or TaskCanceledException){selections.Add(new(symbol,symbol,FutureContracts.IsRoot(symbol),e is FormatException?e.Message:"Could not fetch the active contract."));}}return selections;}));
        app.MapPost("/api/market/capture",(QuoteRequest request)=>Run(async()=>{var quote=await connection.Quote(MarketStore.Symbol(request.Symbol));store.SaveQuote(quote);return quote;}));
        app.MapPost("/api/market/watchlist",(WatchlistRequest request)=>Run(()=>{store.SaveWatchlist(request.Symbols);return Task.FromResult<object>(store.Watchlist());}));
        app.MapGet("/api/market/history",(string symbol,int? days)=>Run(()=>{symbol=MarketStore.Symbol(symbol);var bars=store.Bars(symbol,days??1);return Task.FromResult<object>(new{symbol,quotes=store.Quotes(symbol,days??1),bars,technicals=TechnicalIndicators.Calculate(bars),timeframes=timeframes?.Get(symbol)});}));
        app.MapPost("/api/market/refresh",()=>Run(async()=>{var results=new List<object>();foreach(var requested in store.Watchlist()){try{var symbol=await contracts.Resolve(requested);var quote=await connection.Quote(symbol);store.SaveQuote(quote);results.Add(new{symbol,quote,error=(string?)null});}catch(Exception e) when(e is FormatException or HttpRequestException or TaskCanceledException){results.Add(new{symbol=requested,quote=(MarketQuote?)null,error=e is FormatException?e.Message:"Cannot reach Schwab."});}}return results;}));
        app.MapPost("/api/market/backfill",(HistoryRequest request)=>Run(async()=>{var bars=await connection.PriceHistory(MarketStore.Symbol(request.Symbol));store.SaveBars(bars);return new{count=bars.Length,symbol=request.Symbol,message=bars.Length>0?"Closed one-minute bars saved.":"Schwab returned no bars. This symbol may not support historical data; sampled quotes remain available."};}));
    }
}
