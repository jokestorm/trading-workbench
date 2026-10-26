using System.Text.RegularExpressions;

namespace TradingWorkbench;

public record FuturesProduct(string Root,string Name,string Group,decimal PointValue,decimal TickSize,string Months);
public record FuturesUniverseState(DateTimeOffset? CheckedAt,MonitorInstrument[] Instruments,string[] Unavailable,string Message);

public static class FuturesProducts
{
    // Outright contract specifications. Source links are recorded in docs/continuous-monitor.md.
    public static readonly FuturesProduct[] All=
    [
        new("MES","Micro S&P 500","Equity indices",5,.25m,"HMUZ"),new("MNQ","Micro Nasdaq-100","Equity indices",2,.25m,"HMUZ"),
        new("M2K","Micro Russell 2000","Equity indices",5,.1m,"HMUZ"),new("MYM","Micro Dow","Equity indices",.5m,1,"HMUZ"),
        new("ES","E-mini S&P 500","Equity indices",50,.25m,"HMUZ"),new("NQ","E-mini Nasdaq-100","Equity indices",20,.25m,"HMUZ"),
        new("MGC","Micro Gold","Metals",10,.1m,"GJMQVZ"),new("GC","Gold","Metals",100,.1m,"GJMQVZ"),
        new("MCL","Micro Crude Oil","Energy",100,.01m,"FGHJKMNQUVXZ"),new("CL","Crude Oil","Energy",1000,.01m,"FGHJKMNQUVXZ"),
        new("NG","Natural Gas","Energy",10000,.001m,"FGHJKMNQUVXZ"),
        new("M6E","Micro EUR/USD","Currencies",12500,.0001m,"HMUZ"),new("6E","Euro FX","Currencies",125000,.00005m,"HMUZ"),
        new("ZN","10-Year Treasury Note","Interest rates",1000,1m/64,"HMUZ"),new("ZB","Treasury Bond","Interest rates",1000,1m/32,"HMUZ"),
        new("ZC","Corn","Agriculture",50,.25m,"HKNUZ"),new("ZW","Wheat","Agriculture",50,.25m,"HKNUZ"),new("ZS","Soybeans","Agriculture",50,.25m,"FHKNQUX"),
        new("MET","Micro Ether","Crypto",.1m,.5m,"FGHJKMNQUVXZ"),new("MBT","Micro Bitcoin","Crypto",.1m,5,"FGHJKMNQUVXZ")
    ];
    public static readonly string[] TradingRoots=["MES","MNQ","M2K","MYM","MGC","MCL","M6E","MET","MBT"];
    public static IEnumerable<FuturesProduct> Trading=>All.Where(p=>TradingRoots.Contains(p.Root));
    public static bool IsTradingProduct(string symbol)=>Find(symbol) is {} product&&TradingRoots.Contains(product.Root);
    public static FuturesProduct? Find(string symbol)
    {
        var normalized=symbol.Trim().TrimStart('/').Split(':')[0];
        return All.FirstOrDefault(p=>normalized==p.Root||Regex.IsMatch(normalized,"^"+Regex.Escape(p.Root)+"[FGHJKMNQUVXZ][0-9]{2}$"));
    }
    public static MonitorInstrument? Instrument(string symbol)
    {
        var product=Find(symbol);return product is null?null:new(MarketStore.Symbol(symbol.Split(':')[0]),product.PointValue,product.TickSize);
    }
}

public sealed class FuturesUniverse
{
    private readonly Func<string[],CancellationToken,Task<MarketQuote[]>> quotes;
    private readonly Func<string[]> watchlist;
    private readonly Func<CancellationToken,Task<BrokerTransactionReport>> transactions;
    private readonly Action<FuturesUniverseState> apply;
    private readonly ILogger<FuturesUniverse> log;
    private readonly SemaphoreSlim gate=new(1,1);
    public FuturesUniverse(Func<string[],CancellationToken,Task<MarketQuote[]>> quotes,Func<string[]> watchlist,Func<CancellationToken,Task<BrokerTransactionReport>> transactions,Action<FuturesUniverseState> apply,ILogger<FuturesUniverse>? logger=null)
    {
        this.quotes=quotes;this.watchlist=watchlist;this.transactions=transactions;this.apply=apply;
        log=logger??Microsoft.Extensions.Logging.Abstractions.NullLogger<FuturesUniverse>.Instance;
    }
    public async Task Refresh(CancellationToken token)
    {
        if(!await gate.WaitAsync(0,token))return;
        try
        {
            var now=DateTimeOffset.UtcNow;
            var transactionReport=await transactions(token);
            var recent=transactionReport.History?.Accounts.SelectMany(a=>a.Fills).Select(f=>f.Symbol)??[];
            var symbols=FuturesProducts.Trading.SelectMany(p=>FutureContracts.Candidates("/"+p.Root,now)).Concat(watchlist().Where(s=>!FutureContracts.IsRoot(s))).Concat(recent)
                .Where(s=>FuturesProducts.IsTradingProduct(s)&&!FutureContracts.IsRoot(s)).Distinct().Take(100).ToArray();
            var observed=await quotes(symbols,token);token.ThrowIfCancellationRequested();
            var selected=new List<MonitorInstrument>();var unavailable=new List<string>();
            foreach(var product in FuturesProducts.Trading)
            {
                var root="/"+product.Root;
                var best=FutureContracts.MostActive(root,observed,now);
                if(best is null){unavailable.Add(root);continue;}
                selected.Add(new(best.Symbol,product.PointValue,product.TickSize));
            }
            apply(new(now,selected.ToArray(),unavailable.ToArray(),$"Automatically watching {selected.Count} micro futures products."));
        }
        finally{gate.Release();}
    }
    public async Task Run(CancellationToken token)
    {
        while(!token.IsCancellationRequested)
        {
            try{await Refresh(token);await Task.Delay(TimeSpan.FromMinutes(15),token);}
            catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
            catch(Exception e)
            {
                log.LogWarning("Futures discovery failed errorType={ErrorType} reason={Reason}",e.GetType().Name,e is FormatException?e.Message:"Market discovery request did not finish.");
                try{await Task.Delay(TimeSpan.FromSeconds(30),token);}catch(OperationCanceledException){break;}
            }
        }
    }
}
