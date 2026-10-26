namespace TradingWorkbench;

public static class FuturesUniverseChecks
{
    public static async Task Run()
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
        Check(FuturesProducts.Trading.Select(p=>p.Root).SequenceEqual(new[]{"MES","MNQ","M2K","MYM","MGC","MCL","M6E","MET","MBT"}),"the trader watches the nine micro products without full-size duplicates");
        Check(!FuturesProducts.IsTradingProduct("/CLZ26")&&!FuturesProducts.IsTradingProduct("/ESZ26")&&!FuturesProducts.IsTradingProduct("/6EZ26")&&FuturesProducts.IsTradingProduct("/MCLZ26"),"standard-size contracts remain outside the active trading universe");
        Check(FuturesProducts.Find("/M6EZ26") is {Root:"M6E",PointValue:12500,TickSize:.0001m}&&FuturesProducts.Find("/MNQZ26") is {Root:"MNQ",PointValue:2,TickSize:.25m}&&FuturesProducts.Find("/MESBOGUSZ26") is null,"product specifications match exact dated roots including numeric symbols without prefix guesses");
        var december=new DateTimeOffset(2026,12,20,15,0,0,TimeSpan.Zero);
        Check(FutureContracts.Candidates("/MNQ",december).Take(2).SequenceEqual(new[]{"/MNQZ26","/MNQH27"})&&FutureContracts.Candidates("/ZS",december)[0]=="/ZSF27","automatic calendars follow each product's listed months across year boundaries");
        var now=DateTimeOffset.UtcNow;var calls=0;string[] requested=[];FuturesUniverseState? selected=null;
        MarketQuote Quote(string symbol,long volume,DateTimeOffset expiration)=>new(symbol,"FUTURE",100,99,101,null,null,now,true,now){TotalVolume=volume,Expiration=expiration};
        var universe=new FuturesUniverse((symbols,_)=>
        {
            calls++;requested=symbols;
            var mes=FutureContracts.Candidates("/MES",now);var gold=FutureContracts.Candidates("/MGC",now);
            return Task.FromResult(new[]{Quote(mes[0],50,now.AddDays(10)),Quote(mes[1],100,now.AddDays(100)),Quote(gold[0],10000,now.AddDays(-1)),Quote(gold[1],10,now.AddDays(30))});
        },()=>["/MES","/CLZ26"],_=>Task.FromResult(new BrokerTransactionReport("ready","Fixture",now,now,new(now.AddDays(-59),now,[new("fixture","Account 1",[new("historical","historical","/NQZ26",now.AddDays(-1),1,100,"OPENING")])]))),state=>selected=state);
        await universe.Refresh(CancellationToken.None);
        Check(calls==1&&requested.Length<=100&&requested.Any(s=>s.StartsWith("/M6E"))&&requested.All(FuturesProducts.IsTradingProduct),"discovery batches micro quotes and excludes full-size contracts from watchlists and transaction history");
        Check(selected?.Instruments.Length==2&&selected.Instruments.Any(s=>s.Symbol==FutureContracts.Candidates("/MES",now)[1])&&selected.Instruments.Any(s=>s.Symbol==FutureContracts.Candidates("/MGC",now)[1]),"discovery chooses the most active nonexpired contract and excludes expired quotes");
        Check(selected!.Unavailable.Contains("/MNQ")&&!selected.Unavailable.Contains("/MES"),"unavailable micro products stay visible without requiring users to add or repair symbols");
    }
}
