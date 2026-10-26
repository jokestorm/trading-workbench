using System.Text.Json;

namespace TradingWorkbench;

public static class BrokerExecutionChecks
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
    public static async Task Run()
    {
        var now=DateTimeOffset.UtcNow;var time=now.AddMinutes(-5);
        BrokerFill Fill(string id,int amount,decimal price,string effect,int seconds)=>new(id,id,"/MESZ26",time.AddSeconds(seconds),amount,price,effect);
        var opened=BrokerExecutions.Apply(new("FLAT"),[Fill("one",1,100,"OPENING",0),Fill("two",1,102,"OPENING",1)]);
        Check(opened.Position is {State:"LONG",Quantity:2,AverageEntry:101},"transaction fills calculate the caller's weighted entry using shared futures accounting");
        var averaged=BrokerExecutions.Apply(new("FLAT"),[Fill("average",2,100.125m,"OPENING",0)]);
        Check(averaged.Position.AverageEntry==100.125m,"broker-reported average transaction prices are preserved even between individual contract ticks");
        var reduced=BrokerExecutions.Apply(opened.Position,[Fill("partial",-1,110,"CLOSING",2)]);
        Check(reduced.Position is {State:"LONG",Quantity:1,AverageEntry:101}&&!reduced.CrossedFlat,"partial transaction exits preserve the remaining entry");
        var reversed=BrokerExecutions.Apply(reduced.Position,[Fill("close",-1,99,"CLOSING",3),Fill("short",-2,99,"OPENING",3)]);
        Check(reversed.Position is {State:"SHORT",Quantity:2,AverageEntry:99}&&reversed.CrossedFlat,"closing and opening transaction legs carry a reversal into a new trade scope");
        var closed=BrokerExecutions.Apply(reversed.Position,[Fill("cover",2,98,"CLOSING",4)]);
        Check(closed.Position is {State:"FLAT",Quantity:null,AverageEntry:null}&&closed.CrossedFlat,"covering transaction fills return the caller to flat");
        var rejected=false;try{BrokerExecutions.Apply(new("LONG",1,100),[Fill("add",1,110,"OPENING",5),Fill("exit",-1,105,"CLOSING",5)]);}catch(FormatException){rejected=true;}
        Check(rejected,"ambiguous simultaneous transactions reuse the shared position reconciliation checks");
        var directory=Path.Combine(Path.GetTempPath(),"broker-execution-check-"+Guid.NewGuid().ToString("N"));
        var history=new BrokerHistory(now.AddDays(-59),now,[new("fixture","Account 1",[Fill("one",1,100,"OPENING",0)])]);var calls=0;
        var service=new BrokerExecutionService(directory,_=>{calls++;return Task.FromResult(history);});
        var report=await service.Refresh(CancellationToken.None);await service.Refresh(CancellationToken.None);
        Check(calls==1&&report.Status=="ready"&&report.History!.Accounts.Single().Fills.Single().Price==100,"the transaction feed persists actual fills and bounds repeated polling");
        File.WriteAllText(Path.Combine(directory,"current.json"),JsonSerializer.Serialize(report with{LastAttempt=now.AddMinutes(-1)},CodexAnalysisProvider.Json));
        var corrected=new BrokerExecutionService(directory,_=>Task.FromResult(history with{Accounts=[new("fixture","Account 1",[])]}));
        var failed=await corrected.Refresh(CancellationToken.None);
        Check(failed.Status=="failed"&&failed.LastSynced==report.LastSynced&&failed.History!.Accounts.Single().Fills.Length==1,"a transaction correction retains the previous fill history without switching data sources");
    }
}
