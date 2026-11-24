using System.Globalization;
using System.Text.RegularExpressions;

namespace FinQuery.Application.Ingestion;

public sealed class DetectedTable
{
    public required int PageNumber { get; init; }

    public List<string> Headers { get; init; } = [];

    public List<List<string>> Rows { get; init; } = [];

    public string? Title { get; init; }

    public string? Units { get; init; }

    public string? Currency { get; init; }

    public decimal Confidence { get; init; }

    public string RawText { get; init; } = "";

    public bool ExtractionOk { get; init; } = true;
}

public sealed class TableExtractor
{
    private const int MinLines = 3;

    private static readonly Regex ColumnGap = new(@"\s{2,}|\t+|\s*\|\s*", RegexOptions.CultureInvariant);
    private static readonly Regex LeadingPipe = new(@"^\s*\|(.*)\|\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex Numeric = new(@"^[$€£¥]?\(?-?[\d.,]+\)?%?$", RegexOptions.CultureInvariant);
    private static readonly Regex UnitHint = new(@"\b(in\s+(?:millions|billions|thousands)|usd\s*(millions|billions)?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CurrencyHint = new(@"\b(USD|EUR|GBP|INR|JPY)\b|[$€£¥₹]", RegexOptions.CultureInvariant);
    private static readonly Regex YearLike = new(@"^(19|20)\d{2}[a-z]?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public List<DetectedTable> ExtractFromPage(int pageNumber, string text)
    {
        var lines = text.Split('\n');
        var tables = new List<DetectedTable>();
        var index = 0;
        while (index < lines.Length)
        {
            var block = CollectBlock(lines, index);
            if (block is null)
            {
                index++;
                continue;
            }

            var (start, end) = block.Value;
            var raw = string.Join('\n', lines[start..end]);
            var preceding = start > 0 ? lines[start - 1] : "";
            var table = ParseBlock(pageNumber, raw, preceding);
            if (table is not null)
            {
                tables.Add(table);
            }

            index = end;
        }

        return tables;
    }

    private static (int Start, int End)? CollectBlock(string[] lines, int start)
    {
        var firstCols = SplitColumns(lines[start]).Count;
        if (firstCols < 2)
        {
            return null;
        }

        var end = start + 1;
        var widths = new HashSet<int> { firstCols };
        while (end < lines.Length)
        {
            var cols = SplitColumns(lines[end]).Count;
            if (cols < 2)
            {
                break;
            }

            widths.Add(cols);
            if (widths.Max() - widths.Min() > 1 && widths.Count > 2)
            {
                break;
            }

            end++;
        }

        return end - start < MinLines ? null : (start, end);
    }

    private static DetectedTable? ParseBlock(int pageNumber, string rawBlock, string precedingLine)
    {
        var rows = rawBlock.Split('\n').Where(line => line.Trim().Length > 0).Select(SplitColumns).ToList();
        if (rows.Count == 0)
        {
            return null;
        }

        var widthCounts = rows.GroupBy(row => row.Count).ToDictionary(group => group.Key, group => group.Count());
        var dominantWidth = widthCounts.MaxBy(pair => pair.Value).Key;
        var stable = rows.Where(row => row.Count == dominantWidth).ToList();
        var unstable = rows.Count - stable.Count;
        var header = HeaderRow(stable);
        var body = header is not null ? stable.Skip(1).ToList() : stable;
        var numericCells = body.Sum(row => row.Skip(1).Count(IsNumericCell));
        var numericSlots = Math.Max(1, body.Sum(row => Math.Max(0, row.Count - 1)));
        var numericRatio = numericCells / (double)numericSlots;
        var confidence = 0.50m + (header is not null ? 0.15m : 0m);
        confidence += 0.25m * decimal.Parse(Math.Round(numericRatio, 2, MidpointRounding.ToEven).ToString("0.00", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (unstable > 0)
        {
            confidence -= 0.10m * Math.Min(unstable, 3);
        }

        confidence = Math.Max(0.05m, Math.Min(confidence, 0.98m));
        var extractionOk = body.Count >= 2 && (unstable == 0 || confidence >= 0.55m);
        var (units, currency) = UnitsCurrency(rawBlock);
        return new DetectedTable
        {
            PageNumber = pageNumber,
            Headers = header?.ToList() ?? [],
            Rows = extractionOk ? body.Select(row => row.ToList()).ToList() : [],
            Title = TitleFrom(precedingLine),
            Units = units,
            Currency = currency,
            Confidence = decimal.Round(confidence, 2, MidpointRounding.ToEven),
            RawText = rawBlock,
            ExtractionOk = extractionOk
        };
    }

    public static List<string> SplitColumns(string line)
    {
        var pipe = LeadingPipe.Match(line);
        if (pipe.Success)
        {
            return pipe.Groups[1].Value.Split('|').Select(cell => cell.Trim()).ToList();
        }

        var parts = ColumnGap.Split(line.Trim()).Select(part => part.Trim()).Where(part => part.Length > 0).ToList();
        if (parts.Count > 1)
        {
            return parts;
        }

        return line.Trim().Length > 0 ? [line.Trim()] : [];
    }

    private static bool IsNumericCell(string cell) => Numeric.IsMatch(cell.Replace(",", "", StringComparison.Ordinal));

    private static List<string>? HeaderRow(List<List<string>> rows)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        var candidate = rows[0];
        var hasYear = candidate.Any(cell => YearLike.IsMatch(cell));
        var hasNoNumbers = !candidate.Skip(1).Any(cell => IsNumericCell(cell.Replace(",", "", StringComparison.Ordinal)));
        return hasYear || (hasNoNumbers && candidate.Count >= 2) ? candidate : null;
    }

    private static string? TitleFrom(string precedingLine)
    {
        var candidate = precedingLine.Trim().TrimEnd(':').Trim();
        if (candidate.Length == 0 || candidate.Length > 200)
        {
            return null;
        }

        if (UnitHint.IsMatch(candidate) || YearLike.IsMatch(candidate))
        {
            return candidate;
        }

        var words = candidate.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length is >= 2 and <= 20 ? candidate : null;
    }

    private static (string? Units, string? Currency) UnitsCurrency(string blockText)
    {
        string? units = null;
        var unitMatch = UnitHint.Match(blockText);
        if (unitMatch.Success)
        {
            var phrase = unitMatch.Value.ToLowerInvariant();
            units = phrase.Contains("million", StringComparison.Ordinal) ? "millions"
                : phrase.Contains("billion", StringComparison.Ordinal) ? "billions"
                : "thousands";
        }

        string? currency = null;
        var currencyMatch = CurrencyHint.Match(blockText);
        if (currencyMatch.Success)
        {
            var symbol = currencyMatch.Value;
            currency = symbol switch
            {
                "$" => "USD",
                "€" => "EUR",
                "£" => "GBP",
                "¥" => "JPY",
                "₹" => "INR",
                _ => symbol.ToUpperInvariant()
            };
        }

        return (units, currency);
    }
}
