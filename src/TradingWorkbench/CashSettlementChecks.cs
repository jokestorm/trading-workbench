namespace TradingWorkbench;

public static class CashSettlementChecks
{
    public static void Run()
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
        const string csv="""
            Account Statement for SYNTHETIC settlement
            Futures Statements
            Trade Date,Exec Date,Exec Time,Type,Ref #,Description,Misc Fees,Commissions & Fees,Amount,Balance
            1/28/27,1/28/27,08:00:00,BAL,--,Starting futures balance,--,--,--,1000.00
            1/28/27,1/28/27,09:00:00,TRD,1,BOT +2 /METF27:XCME @100.00,0,-0.20,--,999.80
            1/28/27,1/28/27,17:00:00,ADJ,--,/METF27:XCME mark to market at 110.00 official settlement price,--,--,2.00,1001.80
            1/29/27,1/29/27,17:02:00,TRD,2,cash settle future,0,-0.30,1.00,1002.50
            Account Trade History
            ,Exec Time,Spread,Side,Qty,Pos Effect,Symbol,Exp,Strike,Type,Price,Net Price,Order Type
            ,1/28/27 09:00:00,FUTURE,BUY,+2,TO OPEN,/METF27,JAN 27,,FUTURE,100.00,100.00,LMT
            """;
        var directory=Path.Combine(Path.GetTempPath(),"settlement-check-"+Guid.NewGuid().ToString("N"));
        var store=new TradeStore(directory);var imports=new ImportService(store);
        var request=new ImportRequest("synthetic-settlement.csv",csv,new(),"Settlement fixture","Futures",1);
        var preview=imports.Preview(request);
        Check(preview.Errors.Length==0&&preview.Reconciliation!.Reconciled&&preview.Trades.Single().Trade.Exit==115m&&preview.Trades[0].Trade.GrossPnl==3m&&preview.NetPnl==2.5m,"cash-settled Micro Ether closes at the ledger-implied price and reconciles fees and P/L");
        imports.Commit(preview.Token);var repeat=imports.Preview(request);
        Check(repeat.Errors.Length==0&&repeat.Duplicates==1&&store.Positions().Length==0,"cash-settled expiration reimport is deduplicated and leaves no open position");
        var shortCsv=csv.Replace("BOT +2","SOLD -2").Replace("FUTURE,BUY,+2","FUTURE,SELL,-2").Replace("110.00 official","90.00 official");
        var shortPreview=imports.Preview(request with{Csv=shortCsv,Account="Short fixture"});
        Check(shortPreview.Errors.Length==0&&shortPreview.Reconciliation!.Reconciled&&shortPreview.Trades.Single().Trade.Exit==85m&&shortPreview.NetPnl==2.5m,"cash settlement closes short positions with the correct price direction");
        var rejected=false;
        try{imports.Preview(request with{Account="Missing basis",Csv=csv.ReplaceLineEndings("\n").Replace("1/28/27,1/28/27,17:00:00,ADJ,--,/METF27:XCME mark to market at 110.00 official settlement price,--,--,2.00,1001.80\n","").Replace(",-0.30,1.00,1002.50",",-0.30,3.00,1002.50")});}catch(FormatException){rejected=true;}
        Check(rejected,"cash settlement without a verified preceding mark cannot invent an exit");
        rejected=false;try{imports.Preview(request with{Account="Wrong expiry",Csv=csv.Replace("METF27","METG27")});}catch(FormatException){rejected=true;}
        Check(rejected,"cash settlement outside the contract expiration month remains blocked");
        rejected=false;try{imports.Preview(request with{Account="Off tick",Csv=csv.Replace(",-0.30,1.00,1002.50",",-0.30,1.01,1002.51")});}catch(FormatException){rejected=true;}
        Check(rejected,"cash settlement with no valid implied price tick remains blocked");
        Check(preview.Warnings.Any(w=>w.Contains("not an exchange execution"))&&preview.Trades[0].Executions[^1].ExternalId.StartsWith("tos:cash-settlement:"),"settlement-derived exits retain explicit provenance in the preview and saved fills");
        var adjustedCsv=csv.Replace("Account Trade History","1/30/27,1/30/27,01:00:00,BAL,--,Starting futures balance,--,--,--,1002.73\n1/31/27,1/31/27,01:00:00,BAL,--,Starting futures balance,--,--,--,1002.56\nAccount Trade History");
        var adjusted=imports.Preview(request with{Csv=adjustedCsv,Account="Cash change fixture"});
        Check(adjusted.Errors.Length==0&&adjusted.Reconciliation!.Reconciled&&adjusted.NetPnl==2.5m&&adjusted.Reconciliation.Sweeps==0&&adjusted.Reconciliation.CashBalanceChanges.Sum(c=>c.Amount)==.06m,"unitemized daily cash changes reconcile separately without changing trade P/L or reported sweeps");
        Check(adjusted.Reconciliation!.CashBalanceChanges.Length==2&&adjusted.Warnings.Any(w=>w.Contains("unitemized")),"positive and negative opening-balance changes stay explicit and unclassified");
        var sweptCsv=csv.Replace("Account Trade History","1/29/27,1/29/27,23:59:59,FSWP,--,Cash sweep,0,0,0.23,1002.73\n1/30/27,1/30/27,01:00:00,BAL,--,Starting futures balance,--,--,--,1002.73\nAccount Trade History");
        var swept=imports.Preview(request with{Csv=sweptCsv,Account="Sweep fixture"});
        Check(swept.Errors.Length==0&&swept.Reconciliation!.Reconciled&&swept.Reconciliation.CashBalanceChanges.Length==0&&swept.Reconciliation.Sweeps==.23m&&swept.NetPnl==2.5m,"explicit account sweeps are not duplicated as unitemized adjustments or trade income");
        rejected=false;try{imports.Preview(request with{Account="Invalid running balance",Csv=csv.Replace(",-0.30,1.00,1002.50",",-0.30,1.00,1002.51")});}catch(FormatException){rejected=true;}
        Check(rejected,"transaction-row cash discrepancies still block import");
    }
}
