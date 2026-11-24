using System.Globalization;
using System.Text.RegularExpressions;

namespace FinQuery.Application.Ingestion;

public sealed class DetectedMetric
{
    public required string RawLabel { get; init; }

    public required string CanonicalName { get; init; }

    public required string NormalizedMetricName { get; init; }

    public required string MetricType { get; init; }

    public decimal? Value { get; init; }

    public bool Percent { get; init; }

    public string? ScaleWord { get; init; }

    public string? CurrencySymbol { get; init; }

    public string? PeriodHint { get; init; }

    public int PageNumber { get; init; }

    public int? ChunkIndex { get; init; }

    public decimal Confidence { get; init; }

    public decimal? ResolvedValue()
    {
        if (Value is null)
        {
            return null;
        }

        if (Percent)
        {
            return Value;
        }

        var multiplier = (ScaleWord ?? "") switch
        {
            "billion" or "billions" or "bn" => 1_000_000_000m,
            "million" or "millions" or "mn" => 1_000_000m,
            "thousand" or "thousands" or "k" => 1_000m,
            _ => 1m
        };
        return Value.Value * multiplier;
    }
}

public sealed class MetricDetector
{
    private static readonly (string Canonical, string Alternation, string MetricType)[] Synonyms =
    [
        ("revenue", @"(?:total\s+)?(?:net\s+)?(?:revenues?|net\s+sales|total\s+net\s+sales|sales)", "income_statement"),
        ("gross_profit", @"gross\s+(?:profit|margin\b(?!\s*\())", "income_statement"),
        ("operating_income", @"operating\s+(?:income|profit)", "income_statement"),
        ("net_income", @"net\s+(?:income|earnings?|profit)(?:\s+after\s+taxes?)?", "income_statement"),
        ("ebitda", "ebitda", "income_statement"),
        ("eps_basic", @"(?:basic\s+)?(?:earnings|income)\s+per\s+share", "per_share"),
        ("eps_diluted", @"diluted\s+(?:earnings|income)\s+per\s+share", "per_share"),
        ("free_cash_flow", @"free\s+cash\s+flow", "cash_flow"),
        ("total_assets", @"total\s+assets", "balance_sheet"),
        ("total_liabilities", @"total\s+liabilities", "balance_sheet"),
        ("total_debt", @"total\s+(?:debt|borrowings?|liabilies)", "balance_sheet"),
        ("cash_and_equivalents", @"cash\s+and\s+cash\s+equivalents", "balance_sheet"),
        ("operating_margin", @"operating\s+margin", "margin"),
        ("gross_margin", @"gross\s+margin", "margin"),
        ("rd_expense", @"(?:research\s+and\s+development|r&d)\s+(?:expenses?|costs?)", "income_statement")
    ];

    private static readonly Regex ValuePattern = new(
        """(?<currency>[$€£¥₹]|USD|EUR|GBP|INR|JPY)?\s*\(?(?<value>-?\d{1,3}(?:,\d{3})*(?:\.\d+)?|-?\d+(?:\.\d+)?)\)?\s*(?<percent>%)?""",
        RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace);
    private static readonly Regex ScalePattern = new(@"\b(millions?|billions?|thousands?|mn|bn|k)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PeriodFy = new(@"\b(?:fiscal\s*year|fy)\s*['’]?\s*(20\d{2})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PeriodQ = new(@"\bQ([1-4])[\s'’]*(20\d{2})\b|\b(20\d{2})\s*Q([1-4])\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly (Regex Pattern, string Canonical, string MetricType)[] _compiled = Synonyms
        .Select(item => (
            new Regex($@"\b({item.Alternation})\b[^:\n%$€£¥0-9]{{0,40}}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            item.Canonical,
            item.MetricType))
        .ToArray();

    public List<DetectedMetric> DetectInText(string text, int pageNumber, int? chunkIndex = null)
    {
        var found = new List<DetectedMetric>();
        var seen = new HashSet<(string Line, string Canonical)>();
        foreach (var line in text.Split('\n'))
        {
            foreach (var (pattern, canonical, metricType) in _compiled)
            {
                var match = pattern.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                var labelEnd = match.Index + match.Length;
                var rest = line[labelEnd..];
                if (rest.Length > 80)
                {
                    rest = rest[..80];
                }

                var valueMatch = ValuePattern.Match(rest);
                if (!valueMatch.Success)
                {
                    continue;
                }

                var key = (line.Trim().Length > 60 ? line.Trim()[..60] : line.Trim(), canonical);
                if (!seen.Add(key))
                {
                    break;
                }

                if (!decimal.TryParse(valueMatch.Groups["value"].Value.Replace(",", "", StringComparison.Ordinal), NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    continue;
                }

                var percent = valueMatch.Groups["percent"].Success && valueMatch.Groups["percent"].Value.Length > 0;
                var afterValue = rest[(valueMatch.Index + valueMatch.Length)..];
                if (afterValue.Length > 24)
                {
                    afterValue = afterValue[..24];
                }

                var scaleMatch = ScalePattern.Match(afterValue);
                var scaleWord = scaleMatch.Success ? scaleMatch.Groups[1].Value.ToLowerInvariant() : null;
                if (percent)
                {
                    scaleWord = null;
                    if (number == 0 && rest.Contains("of", StringComparison.Ordinal))
                    {
                        continue;
                    }
                }

                var confidence = 0.55m;
                if (scaleMatch.Success || percent)
                {
                    confidence += 0.15m;
                }

                if (valueMatch.Groups["currency"].Success && valueMatch.Groups["currency"].Value.Length > 0)
                {
                    confidence += 0.10m;
                }

                var period = PeriodAfter(line, labelEnd);
                if (period is not null)
                {
                    confidence += 0.10m;
                }

                confidence = Math.Min(confidence, 0.95m);
                found.Add(new DetectedMetric
                {
                    RawLabel = match.Groups[1].Value,
                    CanonicalName = canonical,
                    NormalizedMetricName = canonical,
                    MetricType = metricType,
                    Value = number,
                    Percent = percent,
                    ScaleWord = scaleWord,
                    CurrencySymbol = valueMatch.Groups["currency"].Success && valueMatch.Groups["currency"].Value.Length > 0
                        ? valueMatch.Groups["currency"].Value
                        : null,
                    PeriodHint = period,
                    PageNumber = pageNumber,
                    ChunkIndex = chunkIndex,
                    Confidence = decimal.Round(confidence, 2, MidpointRounding.ToEven)
                });
                break;
            }
        }

        return found;
    }

    public List<DetectedMetric> DetectInChunks(IEnumerable<PlannedChunk> chunks) =>
        chunks.Where(chunk => chunk.ChunkType == "text")
            .SelectMany(chunk => DetectInText(chunk.Text, chunk.PageStart, chunk.ChunkIndex))
            .ToList();

    public static string? ParseCurrency(string? symbol, string? documentDefault)
    {
        if (symbol is null)
        {
            return documentDefault;
        }

        var upper = symbol.ToUpperInvariant();
        if (upper is "USD" or "EUR" or "GBP" or "INR" or "JPY")
        {
            return upper;
        }

        return symbol switch
        {
            "$" => "USD",
            "€" => "EUR",
            "£" => "GBP",
            "¥" => "JPY",
            "₹" => "INR",
            _ => documentDefault
        };
    }

    private static string? PeriodAfter(string line, int fromIndex)
    {
        var tail = line[fromIndex..];
        if (tail.Length > 120)
        {
            tail = tail[..120];
        }

        var fiscal = PeriodFy.Match(tail);
        if (fiscal.Success)
        {
            return $"FY{fiscal.Groups[1].Value}";
        }

        var quarter = PeriodQ.Match(tail);
        if (quarter.Success)
        {
            var q = quarter.Groups[1].Success && quarter.Groups[1].Value.Length > 0 ? quarter.Groups[1].Value : quarter.Groups[4].Value;
            var year = quarter.Groups[2].Success && quarter.Groups[2].Value.Length > 0 ? quarter.Groups[2].Value : quarter.Groups[3].Value;
            return $"Q{q} {year}";
        }

        return null;
    }
}
