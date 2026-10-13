namespace TradingWorkbench;
public record ContractSelection(string Requested,string Symbol,bool Automatic,string? Error=null);
public sealed class FutureContracts(SchwabConnection connection)
{
    private readonly Dictionary<string,(string Symbol,DateTimeOffset Until)> cache=new();
    private readonly SemaphoreSlim gate=new(1,1);
    public static bool IsRoot(string symbol)=>symbol is "/MES" or "/MGC" or "/MCL";
    public static string[] Candidates(string root,DateTimeOffset now)
    {
        var eastern=TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now,"Eastern Standard Time");
        var start=new DateTime(eastern.Year,eastern.Month,1);
        return Enumerable.Range(0,12).Select(i=>start.AddMonths(i)).Where(d=>root switch{"/MES"=>d.Month%3==0,"/MGC"=>d.Month%2==0,"/MCL"=>true,_=>false}).Take(4).Select(d=>$"{root}{"FGHJKMNQUVXZ"[d.Month-1]}{d.Year%100:00}").ToArray();
    }
    public static bool ValidActive(string root,string? symbol)=>symbol is not null&&symbol.StartsWith(root,StringComparison.Ordinal)&&symbol.Length==root.Length+3&&"FGHJKMNQUVXZ".Contains(symbol[root.Length])&&symbol.AsSpan(root.Length+1).ToString().All(char.IsAsciiDigit);
    public static MarketQuote? MostActive(string root,IEnumerable<MarketQuote> quotes,DateTimeOffset now)=>quotes.Where(q=>ValidActive(root,q.Symbol)&&q.Last.HasValue&&q.TotalVolume>0&&q.Expiration>now).OrderByDescending(q=>q.TotalVolume).FirstOrDefault();
    public async Task<string> Resolve(string requested)
    {
        if(!IsRoot(requested))return requested;
        await gate.WaitAsync();try{
            var now=DateTimeOffset.UtcNow;
            if(cache.TryGetValue(requested,out var saved)&&saved.Until>now)return saved.Symbol;
            var quotes=new List<MarketQuote>();
            foreach(var symbol in Candidates(requested,now)){
                MarketQuote quote;
                try{quote=await connection.Quote(symbol);}catch(FormatException e) when(e.Message.Contains("symbol",StringComparison.OrdinalIgnoreCase)){continue;}
                if(ValidActive(requested,quote.ActiveContract)){
                    var active=quote.ActiveContract==quote.Symbol?quote:await connection.Quote(quote.ActiveContract!);
                    if(active.Last.HasValue&&(active.Expiration is null||active.Expiration>now)){cache[requested]=(active.Symbol,now.AddHours(1));return active.Symbol;}
                }
                if(quote.Last.HasValue&&quote.TotalVolume>0&&quote.Expiration>now)quotes.Add(quote);
            }
            var best=MostActive(requested,quotes,now)??throw new FormatException($"Could not determine the active {requested} contract from Schwab. Enter a dated contract in watchlist options.");
            cache[requested]=(best.Symbol,now.AddHours(1));return best.Symbol;
        }finally{gate.Release();}
    }
}
