using Microsoft.Data.Sqlite;

namespace TradingWorkbench;

public static class JournalChecks
{
    public static void Run()
    {
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine($"PASS {name}"); }
        var trade = new TradeData { Id = "fixture", Symbol = "MESZ26", AssetType = "Futures", OpenedAt = "2026-10-06T09:30", Date = "2026-10-06", Time = "10:00", Direction = "LONG", Quantity = 2m, Entry = 6000m, Exit = 6004m, Multiplier = 5m, Fees = 3.24m, Risk = 50m, Notes = "Original" };
        Check(trade.Validate().Count == 0 && trade.NetPnl == 36.76m && trade.RMultiple == 0.7352m, "long futures, fees and R");
        Check((trade with { Direction = "SHORT" }).NetPnl == -43.24m, "short futures accounting");
        Check((trade with { AssetType = "Equity", Multiplier = 1m, Quantity = 10m, Entry = 1.01m, Exit = 1.03m, Fees = 0.01m }).NetPnl == 0.19m, "decimal accounting");
        Check((trade with { Risk = null }).RMultiple is null, "missing risk stays unavailable");
        Check((trade with { Quantity = 1.5m }).Validate().ContainsKey("quantity") && (trade with { OpenedAt = "2026-10-07T10:00" }).Validate().ContainsKey("time"), "invalid quantity and chronology rejected");
        var directory = Path.Combine(Path.GetTempPath(), "trading-check-" + Guid.NewGuid().ToString("N"));
        var store = new TradeStore(directory); store.Save(trade);
        var edited = trade with { Notes = "Edited", Exit = 6005m }; store.Save(edited);
        var reopened = new TradeStore(directory);
        Check(reopened.List().Count == 1 && reopened.List()[0].Notes == "Edited" && reopened.List()[0].NetPnl == 46.76m, "edits persist after reopening without duplicates");
        var restoredDirectory = Path.Combine(directory, "restored"); Directory.CreateDirectory(restoredDirectory);
        File.WriteAllBytes(Path.Combine(restoredDirectory, "trading.db"), reopened.Backup());
        var restored = new TradeStore(restoredDirectory);
        Check(restored.List()[0].NetPnl == 46.76m && restored.List()[0].Notes == "Edited", "backup restores journal");
        using var db = new SqliteConnection($"Data Source={Path.Combine(restoredDirectory, "trading.db")}"); db.Open();
        using var command = db.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM fills";
        Check((long)command.ExecuteScalar()! == 2, "entry and exit fills preserved through edit and restore");
        Console.WriteLine("All journal checks passed. Test database: " + directory);
    }
}
