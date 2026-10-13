namespace TradingWorkbench;

public static class StatementChecks
{
    public static void Run()
    {
        void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);}
        var csv="""
            Account Statement for DEMO since 9/7/26 through 9/8/26

            Futures Statements
            Trade Date,Exec Date,Exec Time,Type,Ref #,Description,Misc Fees,Commissions & Fees,Amount,Balance
            9/7/26,9/7/26,01:00:00,BAL,--,Starting futures balance,--,--,--,0.00
            9/8/26,9/8/26,09:00:00,TRD,="42",BOT +1 /MESZ26:XCME @6000.00,-0.36,-1.90,--,-2.26
            9/8/26,9/8/26,09:00:00,TRD,="42",BOT +1 /MESZ26:XCME @6000.00,-0.36,-1.90,--,-4.52
            9/8/26,9/8/26,10:00:00,TRD,="43",SOLD -2 /MESZ26:XCME @6001.00,-0.72,-3.80,10.00,0.96
            9/8/26,9/8/26,11:00:00,TRD,="44",BOT +2 /MESZ26:XCME @6010.00,-0.72,-3.80,--,-3.56
            9/8/26,9/8/26,17:00:00,ADJ,--,/MESZ26:XCME mark to market at 6009.00 official settlement price,--,--,-10.00,-13.56
            9/8/26,9/8/26,23:59:59,FSWP,--,Cash sweep from equities account,--,--,20.00,6.44

            "Total Cash $6.44"

            Account Order History
            Notes,Status
            canceled,CANCELED

            Account Trade History
            ,Exec Time,Spread,Side,Qty,Pos Effect,Symbol,Exp,Strike,Type,Price,Net Price,Order Type
            ,9/8/26 11:00:00,FUTURE,BUY,+2,TO OPEN,/MESZ26,DEC 26,,FUTURE,6010.00,6010.00,LMT
            ,9/8/26 10:00:00,FUTURE,SELL,-2,TO CLOSE,/MESZ26,DEC 26,,FUTURE,6001.00,6001.00,LMT
            ,9/8/26 09:00:00,FUTURE,BUY,+1,TO OPEN,/MESZ26,DEC 26,,FUTURE,6000.00,6000.00,LMT
            ,9/8/26 09:00:00,FUTURE,BUY,+1,TO OPEN,/MESZ26,DEC 26,,FUTURE,6000.00,6000.00,LMT

            Futures
            Symbol,Description,SPC,Exp,Qty,Trade Price,Mark,P/L Day
            /MESZ26,Sample,1/5,DEC 26,+2,6010.00,6009.00,0.00
            """;
        var request=new ImportRequest("demo-statement.csv",csv,new(),"Demo","Futures",1,"Eastern");
        var directory=Path.Combine(Path.GetTempPath(),"statement-check-"+Guid.NewGuid().ToString("N"));var store=new TradeStore(directory);var imports=new ImportService(store);
        var preview=imports.Preview(request);
        Check(preview.Errors.Length==0&&preview.Trades.Length==1&&preview.OpenPositions.Length==1,"statement keeps closed trades separate from open inventory");
        Check(preview.Trades[0].Executions.Length==3&&preview.Trades[0].Executions.Select(e=>e.Id).Distinct().Count()==3,"shared order references and identical split fills remain distinct");
        Check(preview.Reconciliation!.Reconciled&&preview.NetPnl==0.96m&&preview.Reconciliation.AllFees==13.56m,"statement fees and settlement cash reconcile");
        imports.Commit(preview.Token);Check(store.List().Count==1&&store.Positions()[0].Quantity==2,"statement commit persists open snapshot without counting it as closed P/L");
        var repeat=imports.Preview(request);Check(repeat.Duplicates==1&&repeat.Errors.Length==0,"statement reimport remains stable");imports.Commit(repeat.Token);Check(store.List().Count==1&&store.Positions().Length==1,"statement reimport does not duplicate positions or trades");
        var central=ThinkorswimStatement.Normalize(request with{SourceTimeZone="Central"});Check(central.Normalized.Csv.Contains("2026-09-08T10:00:00"),"Central executions convert to Eastern");
        var rejected=false;
        try{imports.Preview(request with{Csv=csv.Replace("9/8/26 09:00:00,FUTURE,BUY,+1,TO OPEN","9/8/26 09:00:00,FUTURE,BUY,+1,TO CLOSE")});}catch(FormatException){rejected=true;}
        Check(rejected,"unknown starting positions are rejected using position effect");
        var mismatch=imports.Preview(request with{Csv=csv.Replace("DEC 26,+2,6010.00","DEC 26,+3,6010.00")});Check(mismatch.Errors.Length>0&&!mismatch.Reconciliation!.Reconciled,"holdings mismatch blocks import");
        Console.WriteLine("All statement checks passed.");
    }
}
