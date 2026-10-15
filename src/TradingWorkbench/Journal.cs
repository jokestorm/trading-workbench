using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TradingWorkbench;

public record InstrumentDefinition(string Root, string AssetType, decimal PointValue);
public static class InstrumentCatalog
{
    public static InstrumentDefinition[] All => [new("MES", "Futures", 5m), new("MNQ", "Futures", 2m), new("MCL", "Futures", 100m), new("CL", "Futures", 1000m),new("MGC","Futures",10m), new("MET","Futures",.1m), new("ES", "Futures", 50m), new("NQ", "Futures", 20m), new("EQUITY", "Equity", 1m)];
}
public record TradeData
{
    public string Id { get; init; } = "";
    public string Account { get; init; } = "Personal";
    public string Source { get; init; } = "Manual";
    public decimal? AccountingGrossPnl { get; init; }
    public string Symbol { get; init; } = "";
    public string AssetType { get; init; } = "Equity";
    public string OpenedAt { get; init; } = "";
    public string Date { get; init; } = "";
    public string Time { get; init; } = "";
    public string Direction { get; init; } = "LONG";
    public decimal Quantity { get; init; }
    public decimal Entry { get; init; }
    public decimal Exit { get; init; }
    public decimal Multiplier { get; init; } = 1m;
    public decimal Fees { get; init; }
    public decimal? Risk { get; init; }
    public string Notes { get; init; } = "";
    public decimal GrossPnl => AccountingGrossPnl ?? (Exit - Entry) * (Direction == "LONG" ? 1m : -1m) * Quantity * Multiplier;
    public decimal NetPnl => decimal.Round(GrossPnl - Fees, 2, MidpointRounding.AwayFromZero);
    public decimal? RMultiple => Risk > 0 ? decimal.Round(NetPnl / Risk.Value, 4) : null;
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        void Error(string field, string message) => errors[field] = [message];
        if (string.IsNullOrWhiteSpace(Symbol) || Symbol.Length > 32 || Symbol.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '/' or '-'))) Error("symbol", "Use an instrument symbol of up to 32 letters, digits, dots, slashes or hyphens.");
        if (Direction is not ("LONG" or "SHORT")) Error("direction", "Choose long or short.");
        if (AssetType is not ("Equity" or "Futures")) Error("assetType", "Choose equity or futures.");
        if (Quantity <= 0 || Quantity > 1_000_000 || (AssetType == "Futures" && Quantity != decimal.Truncate(Quantity))) Error("quantity", "Quantity must be positive; futures require whole contracts.");
        if (Entry <= 0 || Exit <= 0 || Entry > 1_000_000_000 || Exit > 1_000_000_000) Error("price", "Entry and exit prices must be positive and within supported limits.");
        if (Multiplier <= 0 || Multiplier > 1_000_000 || (AssetType == "Equity" && Multiplier != 1)) Error("multiplier", "Use a positive point value; equities use 1.");
        if (Fees < 0 || Fees > 1_000_000_000) Error("fees", "Total fees must be nonnegative.");
        if (Risk is < 0.01m or > 1_000_000_000) Error("risk", "Planned risk must be at least $0.01 or left blank.");
        string[] timeFormats = ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"];
        var closeValid = DateTime.TryParseExact($"{Date}T{Time}", timeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var close);
        var openValid = DateTime.TryParseExact(OpenedAt, timeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var open);
        if (!closeValid || !openValid || open > close || open.Year < 2000 || close.Year > 2100) Error("time", "Enter valid opening and closing times between 2000 and 2100; close must follow entry.");
        if (Notes?.Length > 10_000) Error("notes", "Notes must be at most 10,000 characters.");
        return errors;
    }
}
public class TradeStore
{
    private readonly string connectionString;
    private readonly string directory;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public TradeStore(string directory)
    {
        this.directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "trading.db") }.ToString();
        using var db = Open(); using var command = db.CreateCommand();
        command.CommandText="PRAGMA user_version";
        var version=(long)command.ExecuteScalar()!;
        if(version>3)throw new InvalidOperationException("This database was created by a newer app version.");
        if(version is 1 or 2)File.WriteAllBytes(Path.Combine(directory,$"before-schema-3-{Guid.NewGuid():N}.db"),Backup());
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS trades (id TEXT PRIMARY KEY, document TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS fills (id TEXT PRIMARY KEY, trade_id TEXT NOT NULL REFERENCES trades(id), role TEXT NOT NULL, document TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS import_batches (id TEXT PRIMARY KEY, filename TEXT NOT NULL, imported_at TEXT NOT NULL, raw_csv TEXT NOT NULL, mapping TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS import_sources (id TEXT PRIMARY KEY, fingerprint TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS open_positions (account TEXT NOT NULL,symbol TEXT NOT NULL,document TEXT NOT NULL,PRIMARY KEY(account,symbol));
            PRAGMA user_version = 3;
            """;
        command.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var db = new SqliteConnection(connectionString); db.Open(); return db; }
    public List<TradeData> List()
    {
        using var db = Open(); using var command = db.CreateCommand(); command.CommandText = "SELECT document FROM trades";
        using var reader = command.ExecuteReader(); var result = new List<TradeData>();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<TradeData>(reader.GetString(0), Json)!);
        return result.OrderBy(t => t.Date).ThenBy(t => t.Time).ToList();
    }
    public void Save(TradeData trade)
    {
        using var db = Open(); using var transaction = db.BeginTransaction();
        using var command = db.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO trades(id, document) VALUES ($id, $document) ON CONFLICT(id) DO UPDATE SET document=excluded.document";
        command.Parameters.AddWithValue("$id", trade.Id); command.Parameters.AddWithValue("$document", JsonSerializer.Serialize(trade, Json)); command.ExecuteNonQuery();
        if(trade.Source=="CSV") { transaction.Commit();return; }
        foreach (var role in new[] { "ENTRY", "EXIT" })
        {
            using var fill = db.CreateCommand(); fill.Transaction = transaction;
            fill.CommandText = "INSERT INTO fills(id,trade_id,role,document) VALUES($id,$trade,$role,$document) ON CONFLICT(id) DO UPDATE SET document=excluded.document";
            fill.Parameters.AddWithValue("$id", $"{trade.Id}-{role}"); fill.Parameters.AddWithValue("$trade", trade.Id); fill.Parameters.AddWithValue("$role", role);
            fill.Parameters.AddWithValue("$document", JsonSerializer.Serialize(new { trade.Symbol, trade.AssetType, trade.Quantity, price = role == "ENTRY" ? trade.Entry : trade.Exit, side = (role == "ENTRY") == (trade.Direction == "LONG") ? "BUY" : "SELL", localTimeEastern = role == "ENTRY" ? trade.OpenedAt : $"{trade.Date}T{trade.Time}", fees = role == "EXIT" ? trade.Fees : 0m, source = "Manual", trade.Multiplier }, Json));
            fill.ExecuteNonQuery();
        }
        transaction.Commit();
    }
    public object[] Fills(string tradeId)
    {
        using var db=Open();using var command=db.CreateCommand();command.CommandText="SELECT role,document FROM fills WHERE trade_id=$id ORDER BY rowid";command.Parameters.AddWithValue("$id",tradeId);
        using var reader=command.ExecuteReader();var result=new List<object>();
        while(reader.Read())result.Add(new{role=reader.GetString(0),execution=JsonSerializer.Deserialize<JsonElement>(reader.GetString(1))});return result.ToArray();
    }
    public string ImportStatus(ImportedTrade candidate)
    {
        using var db=Open();var known=0;
        foreach(var execution in candidate.Executions.DistinctBy(f=>f.Id))
        {
            using var query=db.CreateCommand();query.CommandText="SELECT fingerprint FROM import_sources WHERE id=$id";query.Parameters.AddWithValue("$id",execution.Id);
            var existing=query.ExecuteScalar() as string;if(existing is null)continue;
            if(existing!=execution.SourceFingerprint)return "A previously imported execution changed. Reconcile its source data before importing; corrections are not overwritten.";
            known++;
        }
        using var tradeQuery=db.CreateCommand();tradeQuery.CommandText="SELECT COUNT(*) FROM trades WHERE id=$id";tradeQuery.Parameters.AddWithValue("$id",candidate.Trade.Id);
        if((long)tradeQuery.ExecuteScalar()!>0)return "duplicate";
        if(known>0)return "This trade overlaps imported executions but has different grouping. Import the original complete flat-to-flat history or reconcile it first.";
        var start=DateTime.Parse(candidate.Trade.OpenedAt,CultureInfo.InvariantCulture);var end=DateTime.Parse($"{candidate.Trade.Date}T{candidate.Trade.Time}",CultureInfo.InvariantCulture);
        if(List().Any(t=>t.Source=="CSV" && t.Account==candidate.Trade.Account && t.Symbol==candidate.Trade.Symbol && ((DateTime.Parse(t.OpenedAt,CultureInfo.InvariantCulture)<end && DateTime.Parse($"{t.Date}T{t.Time}",CultureInfo.InvariantCulture)>start) || (t.OpenedAt==candidate.Trade.OpenedAt && t.Date==candidate.Trade.Date && t.Time==candidate.Trade.Time))))return "This period overlaps an imported trade with different execution identities. Reconcile it before importing.";
        return "new";
    }
    public ImportRequest[] StatementHistory(string account)
    {
        using var db=Open();using var query=db.CreateCommand();
        query.CommandText="SELECT raw_csv,mapping FROM import_batches ORDER BY imported_at";
        using var reader=query.ExecuteReader();var result=new List<ImportRequest>();
        while(reader.Read())
        {
            var request=JsonSerializer.Deserialize<ImportRequest>(reader.GetString(1),Json)!;
            if(request.Account.Trim()==account.Trim()&&ThinkorswimStatement.IsStatement(reader.GetString(0)))result.Add(request with{Csv=reader.GetString(0)});
        }
        return result.ToArray();
    }
    public void CommitImport(ImportPreview preview,ImportRequest request)
    {
        if(preview.Trades.Length==0 && preview.Reconciliation is null)return;
        var backups=Path.Combine(directory,"backups");Directory.CreateDirectory(backups);
        File.WriteAllBytes(Path.Combine(backups,$"before-import-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db"),Backup());
        using var db=Open();using var transaction=db.BeginTransaction();
        using var batch=db.CreateCommand();batch.Transaction=transaction;
        batch.CommandText="INSERT INTO import_batches VALUES($id,$filename,$time,$csv,$mapping)";
        batch.Parameters.AddWithValue("$id",Guid.NewGuid().ToString("N"));batch.Parameters.AddWithValue("$filename",preview.FileName);batch.Parameters.AddWithValue("$time",DateTime.UtcNow.ToString("O"));batch.Parameters.AddWithValue("$csv",request.Csv);batch.Parameters.AddWithValue("$mapping",JsonSerializer.Serialize(request with{Csv=""},Json));batch.ExecuteNonQuery();
        foreach(var candidate in preview.Trades)
        {
            using var trade=db.CreateCommand();trade.Transaction=transaction;trade.CommandText="INSERT INTO trades VALUES($id,$document)";
            trade.Parameters.AddWithValue("$id",candidate.Trade.Id);trade.Parameters.AddWithValue("$document",JsonSerializer.Serialize(candidate.Trade,Json));trade.ExecuteNonQuery();
            for(var i=0;i<candidate.Executions.Length;i++)
            {
                var execution=candidate.Executions[i];
                using var source=db.CreateCommand();source.Transaction=transaction;source.CommandText="INSERT OR IGNORE INTO import_sources VALUES($id,$fingerprint)";
                source.Parameters.AddWithValue("$id",execution.Id);source.Parameters.AddWithValue("$fingerprint",execution.SourceFingerprint);source.ExecuteNonQuery();
                using var fill=db.CreateCommand();fill.Transaction=transaction;fill.CommandText="INSERT INTO fills VALUES($id,$trade,$role,$document)";
                fill.Parameters.AddWithValue("$id",$"{candidate.Trade.Id}-{i}");fill.Parameters.AddWithValue("$trade",candidate.Trade.Id);fill.Parameters.AddWithValue("$role",execution.Side==(candidate.Trade.Direction=="LONG"?"BUY":"SELL")?"ENTRY":"EXIT");fill.Parameters.AddWithValue("$document",JsonSerializer.Serialize(execution,Json));fill.ExecuteNonQuery();
            }
        }
        if(preview.Reconciliation is not null)
        {
            foreach(var symbol in preview.Reconciliation.Symbols)
            {
                using var clear=db.CreateCommand();clear.Transaction=transaction;clear.CommandText="DELETE FROM open_positions WHERE account=$account AND symbol=$symbol";clear.Parameters.AddWithValue("$account",request.Account.Trim());clear.Parameters.AddWithValue("$symbol",symbol);clear.ExecuteNonQuery();
            }
            foreach(var position in preview.OpenPositions)
            {
                using var pending=db.CreateCommand();pending.Transaction=transaction;pending.CommandText="INSERT INTO open_positions VALUES($account,$symbol,$document)";pending.Parameters.AddWithValue("$account",position.Account);pending.Parameters.AddWithValue("$symbol",position.Symbol);pending.Parameters.AddWithValue("$document",JsonSerializer.Serialize(position,Json));pending.ExecuteNonQuery();
            }
        }
        transaction.Commit();
    }
    public OpenPosition[] Positions()
    {
        using var db=Open();using var query=db.CreateCommand();query.CommandText="SELECT document FROM open_positions ORDER BY account,symbol";using var reader=query.ExecuteReader();var result=new List<OpenPosition>();while(reader.Read())result.Add(JsonSerializer.Deserialize<OpenPosition>(reader.GetString(0),Json)!);return result.ToArray();
    }
    public byte[] Backup()
    {
        using var db = Open(); using var copy = new SqliteConnection("Data Source=:memory:"); copy.Open(); db.BackupDatabase(copy);
        var temporary = Path.Combine(Path.GetDirectoryName(new SqliteConnectionStringBuilder(connectionString).DataSource)!, $"backup-{Guid.NewGuid():N}.db");
        try
        {
            using var command = copy.CreateCommand(); command.CommandText = "VACUUM INTO $path"; command.Parameters.AddWithValue("$path", temporary); command.ExecuteNonQuery();
            return File.ReadAllBytes(temporary);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
