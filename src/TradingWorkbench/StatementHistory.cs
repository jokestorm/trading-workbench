using System.Globalization;

namespace TradingWorkbench;

// Rolling statements can omit the opening side of trades still present in their
// window. Prepend verified earlier statement rows, never invented opening fills.
public static class StatementHistory
{
    private sealed record Document(List<string[]> Ledger, List<string[]> History, List<string[]> Holdings)
    {
        public DateTime Start => Time(Ledger[0]);
        public DateTime End => Ledger.Max(Time);
    }
    private static DateTime ParseTime(string value) => DateTime.TryParseExact(value,
        ["M/d/yy HH:mm:ss", "M/d/yy H:mm:ss"], CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var time) ? time : throw new FormatException("Unrecognized execution date/time in saved statement history.");
    private static DateTime Time(string[] row) => ParseTime($"{row[1]} {row[2]}");
    private static Document Read(string csv)
    {
        var rows = CsvReader.Parse(csv);
        List<string[]> Section(string name)
        {
            var start = rows.FindIndex(r => r.Length == 1 && r[0] == name);
            if (start < 0) return name == "Futures" ? [] : throw new FormatException($"Statement section missing: {name}.");
            var end = rows.FindIndex(start + 1, r => r.Length == 1);
            return rows.Skip(start + 2).Take((end < 0 ? rows.Count : end) - start - 2).ToList();
        }
        var ledger = Section("Futures Statements").Where(r => r.Length == 10).ToList();
        if (ledger.Count == 0 || ledger[0][3] != "BAL") throw new FormatException("Statement must include an opening futures cash balance.");
        return new(ledger, Section("Account Trade History"), Section("Futures"));
    }
    public static ImportRequest Extend(ImportRequest request, IEnumerable<ImportRequest> saved, out bool extended)
    {
        extended = false;
        var current = Read(request.Csv);
        var history = saved.Where(s => s.Account.Trim() == request.Account.Trim()
            && s.SourceTimeZone == request.SourceTimeZone && ThinkorswimStatement.IsStatement(s.Csv))
            .Select(s => Read(s.Csv)).ToArray();
        while (true)
        {
            var prior = history.Where(p => p.Start < current.Start && p.End >= current.Start)
                .OrderBy(p => p.Start).FirstOrDefault();
            if (prior is null) break;
            var prefix = prior.Ledger.Where(r => Time(r) < current.Start).ToList();
            // The balance at the splice must agree exactly. The normalizer then
            // checks every running balance, fill, fee, holding and final total.
            if (ThinkorswimStatement.Money(prefix[^1][9]) != ThinkorswimStatement.Money(current.Ledger[0][9]))
                throw new FormatException("Saved history does not match this statement's opening cash balance. Download overlapping history that reconciles.");
            var fills = prior.History.Where(r => r.Length == 13 && ParseTime(r[1]) < current.Start).ToList();
            fills.AddRange(current.History);
            prefix.AddRange(current.Ledger);
            current = new(prefix, fills, current.Holdings);
            extended = true;
        }
        if (!extended) return request;
        List<string[]> rows = [["Account Statement for reconstructed history"], ["Futures Statements"], ["ledger", ""]];
        rows.AddRange(current.Ledger);
        rows.Add(["Account Trade History"]); rows.Add(["fills", ""]); rows.AddRange(current.History);
        rows.Add(["Futures"]); rows.Add(["holdings", ""]); rows.AddRange(current.Holdings);
        static string Escape(string value) => '"' + value.Replace("\"", "\"\"") + '"';
        return request with { Csv = string.Join('\n', rows.Select(r => string.Join(',', r.Select(Escape)))) };
    }
}
