namespace TradingWorkbench;

public static class ImportChecks
{
    public static void Run()
    {
        void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);}
        var columns=new Dictionary<string,int>{{"executionId",0},{"timestamp",1},{"symbol",2},{"side",3},{"quantity",4},{"price",5},{"fees",6}};
        var csv="id,time,symbol,side,quantity,price,fees\n1,2026-10-06 09:30:00,MESZ26,BUY,2,6000,2\n2,2026-10-06 09:32:00,MESZ26,BUY,1,6003,1\n3,2026-10-06 09:34:00,MESZ26,SELL,1,6004,1\n4,2026-10-06 09:36:00,MESZ26,SELL,3,6006,3\n5,2026-10-06 09:38:00,MESZ26,BUY,1,6002,1\n";
        var request=new ImportRequest("fixture.csv",csv,columns,"Test","Futures",5m);
        var built=CsvImporter.Build(request);
        Check(built.Errors.Count==0 && built.Trades.Count==2,"scaling, partial exits and reversal create two closed trades");
        Check(built.Trades[0].Trade.NetPnl==59m && built.Trades[1].Trade.NetPnl==18m,"exact fill cashflows and proportional reversal fees reconcile");
        Check(built.Trades.Sum(t=>t.Trade.Fees)==8m,"reversal fee allocation conserves total fees");
        Check(CsvReader.Parse("a,b\r\n\"two, values\",\"line 1\nline \"\"2\"\"\"\r\n")[1][1]=="line 1\nline \"2\"","CSV quotes, commas, CRLF and embedded newlines");
        var directory=Path.Combine(Path.GetTempPath(),"trading-import-check-"+Guid.NewGuid().ToString("N"));
        var store=new TradeStore(directory);var service=new ImportService(store);
        var preview=service.Preview(request);Check(store.List().Count==0 && preview.NetPnl==77m,"preview is read-only");
        service.Commit(preview.Token);var first=store.List()[0];store.Save(first with{Notes="Keep this note",Risk=50m});
        var repeated=service.Preview(request);Check(repeated.Duplicates==2 && repeated.Trades.Length==0,"repeat import is deduplicated");service.Commit(repeated.Token);
        Check(store.List().Count==2 && store.List().First(t=>t.Id==first.Id).Notes=="Keep this note","repeat imports preserve notes and metadata");
        var changed=service.Preview(request with{Csv=csv.Replace("6000,2","6000,3")});Check(changed.Errors.Length>0,"changed source execution rejected");
        var open=service.Preview(request with{Csv="id,time,symbol,side,quantity,price,fees\n6,2026-10-07 09:30:00,MESZ26,BUY,1,6000,1"});Check(open.Errors.Length>0,"unclosed positions block commit");
        try{service.Commit(open.Token);throw new Exception("Invalid import committed");}catch(FormatException){}
        Check(store.List().Count==2,"invalid batch writes no trades");
        Check(CsvImporter.Build(request with{Csv=csv.Replace(",1,6003,",",1.00,6003,")}).Trades[0].Trade.Id==built.Trades[0].Trade.Id,"decimal formatting does not change execution identity");
        Console.WriteLine("All import checks passed.");
    }
}
