namespace TradingWorkbench;

public static class StatementExecutionChecks
{
    public static void Run()
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
        const string csv="""
            Account Statement for SYNTHETIC reversal
            Futures Statements
            Trade Date,Exec Date,Exec Time,Type,Ref #,Description,Misc Fees,Commissions & Fees,Amount,Balance
            1/6/27,1/6/27,08:00:00,BAL,--,Starting futures balance,--,--,--,1000.00
            1/6/27,1/6/27,09:00:00,TRD,1,BOT +1 /MESH27:XCME @100.00,0,-1,--,999.00
            1/6/27,1/6/27,09:01:00,TRD,2,SOLD -1 /MESH27:XCME @101.00,0,-1,5.00,1003.00
            1/6/27,1/6/27,09:01:01,TRD,2,SOLD -1 /MESH27:XCME @101.00,0,-1,--,1002.00
            1/6/27,1/6/27,09:02:00,TRD,3,BOT +1 /MESH27:XCME @100.00,0,-1,5.00,1006.00
            Account Trade History
            ,Exec Time,Spread,Side,Qty,Pos Effect,Symbol,Exp,Strike,Type,Price,Net Price,Order Type
            ,1/6/27 09:00:00,FUTURE,BUY,+1,TO OPEN,/MESH27,MAR 27,,FUTURE,100.00,100.00,LMT
            ,1/6/27 09:01:00,FUTURE,SELL,-1,TO CLOSE,/MESH27,MAR 27,,FUTURE,101.00,101.00,LMT
            ,1/6/27 09:01:01,FUTURE,SELL,-1,TO CLOSE,/MESH27,MAR 27,,FUTURE,101.00,101.00,LMT
            ,1/6/27 09:02:00,FUTURE,BUY,+1,TO CLOSE,/MESH27,MAR 27,,FUTURE,100.00,100.00,LMT
            """;
        var store=new TradeStore(Path.Combine(Path.GetTempPath(),"execution-check-"+Guid.NewGuid().ToString("N")));var imports=new ImportService(store);
        var request=new ImportRequest("synthetic-reversal.csv",csv,new(),"Execution fixture","Futures",1);
        var preview=imports.Preview(request);
        Check(preview.Errors.Length==0&&preview.Reconciliation!.Reconciled&&preview.Trades.Length==2&&preview.NetPnl==6m&&preview.Trades.Select(t=>t.Trade.Direction).ToHashSet().SetEquals(["LONG","SHORT"]),"split closing-order fills can cross flat and reverse while preserving cashflows and fees");
        imports.Commit(preview.Token);Check(imports.Preview(request).Duplicates==2,"split reversal reimport preserves trade identities");
        var rejected=false;try{imports.Preview(request with{Account="Different order",Csv=csv.Replace("09:01:01,TRD,2,","09:01:01,TRD,99,")});}catch(FormatException){rejected=true;}
        Check(rejected,"an unrelated closing order cannot open an unknown position");
        var near=imports.Preview(request with{Account="Near time",Csv=csv.Replace("1/6/27 09:00:00,FUTURE","1/6/27 08:59:59,FUTURE")});
        Check(near.Errors.Length==0&&near.Reconciliation!.Reconciled&&near.Trades.First().Trade.OpenedAt.EndsWith("08:59:59")&&near.Warnings.Any(w=>w.Contains("within one second")),"unique one-second posting differences use verified Trade History execution times");
        var ambiguous=csv.Replace("1/6/27 09:00:00,FUTURE","1/6/27 08:59:59,FUTURE")+"\n,1/6/27 09:00:01,FUTURE,BUY,+1,TO OPEN,/MESH27,MAR 27,,FUTURE,100.00,100.00,LMT";
        rejected=false;try{imports.Preview(request with{Account="Ambiguous time",Csv=ambiguous});}catch(FormatException){rejected=true;}
        Check(rejected,"multiple possible nearby fills remain blocked rather than arbitrarily matched");
    }
}
