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
        // Two contracts moving $50/ether at 0.1 ether each earn $10 gross.
        // The open position marks $50 lower: -$10 settlement P/L.
        var ether=csv.Replace("MESZ26","METF26").Replace("DEC 26","JAN 26")
            .Replace("9/7/26","1/5/26").Replace("9/8/26","1/6/26")
            .Replace("6000.00","3000.00").Replace("6001.00","3050.00")
            .Replace("6010.00","3500.00").Replace("6009.00","3450.00");
        var etherPreview=imports.Preview(request with{Csv=ether});
        Check(etherPreview.Errors.Length==0&&etherPreview.Reconciliation!.Reconciled&&etherPreview.Trades.Single().Trade.Multiplier==.1m&&etherPreview.Trades.Single().Trade.GrossPnl==10m&&etherPreview.NetPnl==.96m,"METF26 statement uses the Micro Ether point value and reconciles closed/open P/L");
        imports.Commit(etherPreview.Token);var etherRepeat=imports.Preview(request with{Csv=ether});
        Check(etherRepeat.Errors.Length==0&&etherRepeat.Duplicates==1&&etherRepeat.Trades.Length==0,"reimporting historical METF26 executions skips the saved closed trade");
        var instruments=new[]{("CLF27",70m,70.02m,20m),("MCLG27",70m,70.10m,10m),("MESH27",6000m,6002m,10m),("METJ27",3000m,3050m,5m),("MGCZ27",2500m,2501m,10m)};
        var ledger=new List<string>{"1/6/27,1/6/27,08:00:00,BAL,--,Starting futures balance,--,--,--,1000.00"};
        var history=new List<string>();decimal cash=1000;
        string Number(decimal value)=>value.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture);
        for(var i=0;i<instruments.Length;i++)
        {
            var (symbol,entry,exit,gross)=instruments[i];var expiration=new[]{"JAN 27","FEB 27","MAR 27","APR 27","DEC 27"}[i];var minute=$"09:{i:00}";
            cash-=1;ledger.Add($"1/6/27,1/6/27,{minute}:00,TRD,{i*2+1},BOT +1 /{symbol}:XCME @{Number(entry)},0,-1,--,{Number(cash)}");
            cash+=gross-1;ledger.Add($"1/6/27,1/6/27,{minute}:30,TRD,{i*2+2},SOLD -1 /{symbol}:XCME @{Number(exit)},0,-1,{Number(gross)},{Number(cash)}");
            history.Add($",1/6/27 {minute}:00,FUTURE,BUY,+1,TO OPEN,/{symbol},{expiration},,FUTURE,{Number(entry)},{Number(entry)},LMT");
            history.Add($",1/6/27 {minute}:30,FUTURE,SELL,-1,TO CLOSE,/{symbol},{expiration},,FUTURE,{Number(exit)},{Number(exit)},LMT");
        }
        var mixed="Account Statement for SYNTHETIC\nFutures Statements\nTrade Date,Exec Date,Exec Time,Type,Ref #,Description,Misc Fees,Commissions & Fees,Amount,Balance\n"+string.Join('\n',ledger)+"\nAccount Trade History\n,Exec Time,Spread,Side,Qty,Pos Effect,Symbol,Exp,Strike,Type,Price,Net Price,Order Type\n"+string.Join('\n',history);
        var mixedPreview=imports.Preview(request with{Csv=mixed,Account="Mixed fixture"});
        Check(mixedPreview.Errors.Length==0&&mixedPreview.Trades.Length==5&&mixedPreview.Reconciliation!.Reconciled&&mixedPreview.Reconciliation.ClosedGross==55m&&mixedPreview.NetPnl==45m,"mixed CL, MCL, MES, MET and MGC statement uses distinct multipliers and reconciles $45 net after fees");
        imports.Commit(mixedPreview.Token);var mixedRepeat=imports.Preview(request with{Csv=mixed,Account="Mixed fixture"});
        Check(mixedRepeat.Errors.Length==0&&mixedRepeat.Duplicates==5&&mixedRepeat.Trades.Length==0,"mixed-contract statement reimport skips all saved trades");
        Console.WriteLine("All statement checks passed.");
    }
}
