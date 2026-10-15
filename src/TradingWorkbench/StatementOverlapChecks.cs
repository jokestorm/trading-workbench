namespace TradingWorkbench;

public static class StatementOverlapChecks
{
    public static void Run()
    {
        static string Csv(string ledger,string fills,string holdings="") => "Account Statement for SAMPLE\n\nFutures Statements\nTrade Date,Exec Date,Exec Time,Type,Ref #,Description,Misc Fees,Commissions & Fees,Amount,Balance\n"+ledger+"\n\nAccount Trade History\n,Exec Time,Spread,Side,Qty,Pos Effect,Symbol,Exp,Strike,Type,Price,Net Price,Order Type\n"+fills+(holdings.Length==0?"":"\n\nFutures\nSymbol,Description,SPC,Exp,Qty,Trade Price,Mark,P/L Day\n"+holdings);
        const string earlyLedger="""
            9/1/26,9/1/26,01:00:00,BAL,--,Starting balance,--,--,--,0.00
            9/1/26,9/1/26,09:00:00,TRD,1,SOLD -1 /MESZ26:XCME @100.00,--,-1.00,--,-1.00
            9/1/26,9/1/26,17:00:00,ADJ,--,/MESZ26:XCME mark to market at 100.00 official settlement price,--,--,0.00,-1.00
            """;
        const string sharedLedger="""
            9/2/26,9/2/26,01:00:00,BAL,--,Starting balance,--,--,--,-1.00
            9/2/26,9/2/26,09:00:00,TRD,2,BOT +1 /MESZ26:XCME @99.00,--,-1.00,5.00,3.00
            9/3/26,9/3/26,01:00:00,BAL,--,Starting balance,--,--,--,3.00
            9/3/26,9/3/26,09:00:00,TRD,3,BOT +2 /MESZ26:XCME @100.00,--,-2.00,--,1.00
            9/3/26,9/3/26,17:00:00,ADJ,--,/MESZ26:XCME mark to market at 101.00 official settlement price,--,--,10.00,11.00
            """;
        const string finalLedger="""
            9/4/26,9/4/26,01:00:00,BAL,--,Starting balance,--,--,--,11.00
            9/4/26,9/4/26,09:00:00,TRD,4,SOLD -2 /MESZ26:XCME @103.00,--,-2.00,20.00,29.00
            """;
        const string firstFill=",9/1/26 09:00:00,FUTURE,SELL,-1,TO OPEN,/MESZ26,DEC 26,,FUTURE,100.00,100.00,LMT";
        const string sharedFills="""
            ,9/2/26 09:00:00,FUTURE,BUY,+1,TO CLOSE,/MESZ26,DEC 26,,FUTURE,99.00,99.00,LMT
            ,9/3/26 09:00:00,FUTURE,BUY,+2,TO OPEN,/MESZ26,DEC 26,,FUTURE,100.00,100.00,LMT
            """;
        const string finalFill=",9/4/26 09:00:00,FUTURE,SELL,-2,TO CLOSE,/MESZ26,DEC 26,,FUTURE,103.00,103.00,LMT";
        var early=new ImportRequest("early.csv",Csv(earlyLedger+"\n"+sharedLedger,firstFill+"\n"+sharedFills,"/MESZ26,Sample,1/5,DEC 26,+2,100.00,101.00,0.00"),new(),"Schwab futures","Futures",1,"Eastern");
        var later=early with{FileName="later.csv",Csv=Csv(sharedLedger+"\n"+finalLedger,sharedFills+"\n"+finalFill)};
        var directory=Path.Combine(Path.GetTempPath(),"rolling-statement-check-"+Guid.NewGuid().ToString("N"));
        var store=new TradeStore(directory);
        var imports=new ImportService(store);
        void Check(bool value,string label){if(!value)throw new Exception("FAIL "+label);Console.WriteLine("PASS "+label);}
        bool Rejected(ImportRequest request){try{var preview=imports.Preview(request);return preview.Errors.Length>0;}catch(FormatException){return true;}}
        Check(Rejected(later),"rolling window with unknown opening fills still requires history");
        var first=imports.Preview(early);Check(first.Errors.Length==0&&first.Reconciliation!.Reconciled,"synthetic earlier statement reconciles with an open position");
        imports.Commit(first.Token);var existing=store.List().Single();store.Save(existing with{Notes="Preserve",Risk=10m});
        var next=imports.Preview(later);
        Check(next.Errors.Length==0&&next.Reconciliation!.Reconciled&&next.Duplicates==1&&next.Trades.Length==1&&next.OpenPositions.Length==0,"saved opening fills bridge a rolling window and close the pending position");
        Check(store.List().Count==1&&store.Positions().Length==1,"rolling preview does not change saved trades or positions");
        imports.Commit(next.Token);
        Check(store.List().Count==2&&store.List().Sum(t=>t.NetPnl)==29m&&store.Positions().Length==0,"combined net P/L conserves fees and positions clear on close");
        Check(store.List().Single(t=>t.Id==existing.Id).Notes=="Preserve","rolling imports preserve existing trade metadata");
        var repeated=imports.Preview(later);Check(repeated.Errors.Length==0&&repeated.Duplicates==2&&repeated.Trades.Length==0,"rolling statement reimport is duplicate safe");
        imports.Commit(repeated.Token);
        Check(Rejected(later with{Account="Other"}),"saved history never crosses account labels");
        Check(Rejected(later with{Csv=later.Csv.Replace("--,--,--,-1.00","--,--,--,-2.00")}),"mismatched opening balance blocks saved-history reconstruction");
        Check(Rejected(later with{Csv=later.Csv.Replace("@99.00","@98.00").Replace("99.00,99.00","98.00,98.00")}),"changed overlapping executions cannot overwrite imported accounting");
        var reopened=new ImportService(new TradeStore(directory));
        var chained=reopened.Preview(later with{FileName="next.csv",Csv=Csv(finalLedger,finalFill)});
        Check(chained.Errors.Length==0&&chained.Reconciliation!.Reconciled&&chained.Duplicates==2&&chained.Trades.Length==0,"rolling history bridges multiple saved statements after reopening storage");
        Check(store.List().Count==2&&store.Positions().Length==0,"rejected imports leave the journal unchanged");
    }
}
