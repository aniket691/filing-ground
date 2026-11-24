using System.Globalization;
using System.Text.RegularExpressions;

namespace FinQuery.Application.Ingestion;

public sealed class FieldConfidence
{
    public required object Value { get; init; }

    public required double Confidence { get; init; }
}

public sealed class DetectedMetadata
{
    public FieldConfidence? Ticker { get; set; }

    public FieldConfidence? DocumentType { get; set; }

    public FieldConfidence? FiscalYear { get; set; }

    public FieldConfidence? FiscalQuarter { get; set; }

    public FieldConfidence? PeriodStart { get; set; }

    public FieldConfidence? PeriodEnd { get; set; }

    public FieldConfidence? Currency { get; set; }

    public FieldConfidence? Units { get; set; }
}

public sealed class MetadataExtractor
{
    private const string Months = "january|february|march|april|may|june|july|august|september|october|november|december";

    private static readonly Regex Ticker = new(@"\b(?:NASDAQ|NYSE|AMEX|NSE|BSE)\s*[:\s]\s*([A-Z]{2,6})\b", RegexOptions.CultureInvariant);
    private static readonly Regex TickerLabel = new(@"\b(?:ticker|symbol)\s*[:\-]?\s*([A-Z]{2,6})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly (Regex Pattern, string Type, double Score)[] DocTypes =
    [
        (new(@"\bform\s*10-?k\b|\b10-?k\b(?:\s+annual)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "10-K", 0.9),
        (new(@"\bform\s*10-?q\b|\b10-?q\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "10-Q", 0.85),
        (new(@"\bform\s*8-?k\b|\b8-?k\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "8-K", 0.85),
        (new(@"\bannual report\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Annual Report", 0.7),
        (new(@"\bquarterly report\b|\binterim results?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Quarterly Report", 0.7),
        (new(@"\binvestor presentation\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Investor Presentation", 0.8),
        (new(@"\bearnings release\b|\bearnings call\b|\bresults announcement\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Earnings Release", 0.75),
        (new(@"\bresearch report\b|\binitiating coverage\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Research Report", 0.75),
        (new(@"\bfinancial statements?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Financial Statement", 0.4)
    ];

    private static readonly Regex FiscalYear = new(@"\bf(?:iscal)?\s*(?:year)?\s*['’]?\s*(20\d{2})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PeriodEnd = new($@"\b(?:fiscal\s+year\s+(?:ended|ending)|year\s+(?:ended|ending)|as\s+of)\s+({Months})\s+(\d{{1,2}}),?\s+(20\d{{2}})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Quarter = new(@"\bQ([1-4])[\s'’]*(20\d{2})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex QuarterText = new($@"\b(three|six|nine|twelve)\s+months\s+(?:ended|ending)\s+({Months})\s+(\d{{1,2}}),?\s+(20\d{{2}})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Currency = new(@"\b(USD|EUR|GBP|INR|JPY)\b", RegexOptions.CultureInvariant);
    private static readonly Regex CurrencySymbol = new(@"[$€£¥₹]", RegexOptions.CultureInvariant);
    private static readonly Regex UnitsPattern = new(@"\bin\s+(millions|billions|thousands)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public DetectedMetadata Extract(IReadOnlyDictionary<int, string> pages, string filename)
    {
        var result = new DetectedMetadata();
        var ordered = pages.Keys.OrderBy(number => number).ToList();
        var headText = string.Join('\n', ordered.Take(3).Select(number => pages[number]));
        var tail = ordered.Count == 0 ? "" : pages[ordered[^1]];
        var headAndTail = string.Join('\n', ordered.Take(3).Select(number => pages[number]).Append(tail));

        var ticker = Ticker.Match(headText);
        if (ticker.Success)
        {
            result.Ticker = new FieldConfidence { Value = ticker.Groups[1].Value.ToUpperInvariant(), Confidence = 0.9 };
        }
        else if ((ticker = TickerLabel.Match(headText)).Success)
        {
            result.Ticker = new FieldConfidence { Value = ticker.Groups[1].Value.ToUpperInvariant(), Confidence = 0.7 };
        }

        string? bestType = null;
        var bestScore = 0.0;
        foreach (var (pattern, docType, score) in DocTypes)
        {
            if (score > bestScore && (pattern.IsMatch(headText) || pattern.IsMatch(filename)))
            {
                bestType = docType;
                bestScore = score;
            }
        }

        if (bestType is not null)
        {
            result.DocumentType = new FieldConfidence { Value = bestType, Confidence = bestScore };
        }

        var years = FiscalYear.Matches(headAndTail).Select(match => match.Groups[1].Value).ToList();
        if (years.Count > 0)
        {
            var top = years.GroupBy(year => year).MaxBy(group => group.Count())!;
            result.FiscalYear = new FieldConfidence { Value = int.Parse(top.Key, CultureInfo.InvariantCulture), Confidence = Math.Min(0.5 + 0.1 * top.Count(), 0.9) };
        }

        var quarterMatch = Quarter.Match(headAndTail);
        var textQuarter = QuarterText.Match(headAndTail);
        if (quarterMatch.Success && !textQuarter.Success)
        {
            var quarter = int.Parse(quarterMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var year = int.Parse(quarterMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            var recency = Recency(headAndTail, quarterMatch.Value);
            result.FiscalQuarter = new FieldConfidence { Value = $"Q{quarter}", Confidence = Math.Min(0.5 + recency * 0.3, 0.85) };
            result.FiscalYear ??= new FieldConfidence { Value = year, Confidence = 0.75 };
        }
        else if (textQuarter.Success)
        {
            var span = textQuarter.Groups[1].Value.ToLowerInvariant() switch
            {
                "three" => 1,
                "six" => 2,
                "nine" => 3,
                _ => 4
            };
            if (span < 4)
            {
                result.FiscalQuarter = new FieldConfidence { Value = $"Q{span}", Confidence = 0.8 };
            }

            var endDate = ParseDate(textQuarter.Groups[2].Value, textQuarter.Groups[3].Value, textQuarter.Groups[4].Value);
            if (endDate is not null)
            {
                result.PeriodEnd = new FieldConfidence { Value = endDate.Value, Confidence = 0.85 };
                if (span is 1 or 2 or 3)
                {
                    var startMonthIndex = endDate.Value.Month - (span == 1 ? 3 : span == 2 ? 6 : 9);
                    var (yearOffset, monthIndex) = FloorDivMod(startMonthIndex - 1, 12);
                    result.PeriodStart = new FieldConfidence { Value = new DateOnly(endDate.Value.Year + yearOffset, monthIndex + 1, 1), Confidence = 0.6 };
                }
            }
        }

        var periodMatch = PeriodEnd.Match(headAndTail);
        if (periodMatch.Success && result.PeriodEnd is null)
        {
            var parsed = ParseDate(periodMatch.Groups[1].Value, periodMatch.Groups[2].Value, periodMatch.Groups[3].Value);
            if (parsed is not null)
            {
                result.PeriodEnd = new FieldConfidence { Value = parsed.Value, Confidence = 0.8 };
            }
        }

        var currency = Currency.Match(headAndTail);
        if (currency.Success)
        {
            result.Currency = new FieldConfidence { Value = currency.Groups[1].Value, Confidence = 0.8 };
        }
        else if ((currency = CurrencySymbol.Match(headAndTail)).Success)
        {
            var mapped = currency.Value switch
            {
                "$" => "USD",
                "€" => "EUR",
                "£" => "GBP",
                "¥" => "JPY",
                "₹" => "INR",
                _ => ""
            };
            if (mapped.Length > 0)
            {
                result.Currency = new FieldConfidence { Value = mapped, Confidence = 0.55 };
            }
        }

        var unitMatches = UnitsPattern.Matches(headAndTail).Select(match => match.Groups[1].Value.ToLowerInvariant()).ToList();
        if (unitMatches.Count > 0)
        {
            var top = unitMatches.GroupBy(unit => unit).MaxBy(group => group.Count())!;
            result.Units = new FieldConfidence { Value = top.Key, Confidence = Math.Min(0.6 + 0.05 * top.Count(), 0.9) };
        }

        return result;
    }

    private static DateOnly? ParseDate(string monthName, string day, string year)
    {
        var text = $"{monthName} {day} {year}";
        return DateOnly.TryParseExact(text, "MMMM d yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    private static double Recency(string text, string needle)
    {
        var index = text.IndexOf(needle, StringComparison.Ordinal);
        return index < 0 ? 0 : index / (double)Math.Max(text.Length, 1);
    }

    private static (int Quotient, int Remainder) FloorDivMod(int value, int divisor)
    {
        var quotient = Math.DivRem(value, divisor, out var remainder);
        if (remainder < 0)
        {
            remainder += divisor;
            quotient--;
        }

        return (quotient, remainder);
    }
}

public static class IngestionAnalysis
{
    private static readonly SectionDetector Sections = new();
    private static readonly TableExtractor Tables = new();
    private static readonly FinanceAwareChunker Chunker = new();
    private static readonly MetricDetector Metrics = new();
    private static readonly MetadataExtractor Metadata = new();

    public static IngestionDraft Draft(IReadOnlyDictionary<int, string> cleanedPages, string filename)
    {
        var roots = Sections.Detect(cleanedPages);
        var tables = cleanedPages.Keys.OrderBy(number => number).SelectMany(page => Tables.ExtractFromPage(page, cleanedPages[page])).ToList();
        var payloads = SectionPayloads(roots, cleanedPages);
        var chunks = Chunker.BuildChunks(cleanedPages, payloads, tables);
        var metrics = Metrics.DetectInChunks(chunks);
        var metadata = Metadata.Extract(cleanedPages, filename);
        var tablesUnreliable = tables.Count > 0 && tables.All(table => !table.ExtractionOk);
        return new IngestionDraft(roots, tables, chunks, metrics, metadata, tablesUnreliable);
    }

    private static List<SectionPayload> SectionPayloads(List<DetectedSection> roots, IReadOnlyDictionary<int, string> cleanedPages)
    {
        var nodes = roots.SelectMany(root => root.Flat()).ToList();
        var payloads = new List<SectionPayload>();
        foreach (var node in nodes)
        {
            var end = node.PageEnd ?? node.PageStart;
            var lines = new List<(int Page, string Line)>();
            for (var pageNumber = node.PageStart; pageNumber <= end; pageNumber++)
            {
                if (!cleanedPages.TryGetValue(pageNumber, out var pageText))
                {
                    continue;
                }

                foreach (var line in pageText.Split('\n'))
                {
                    if (line.Trim().Length > 0 && line.Trim() != node.Title)
                    {
                        lines.Add((pageNumber, line));
                    }
                }
            }

            payloads.Add(new SectionPayload(
                node.Title,
                node.SectionType,
                node.PageStart,
                end,
                node.PathToRoot().Select(ancestor => ancestor.Title).ToList(),
                lines));
        }

        return payloads;
    }
}

public sealed record IngestionDraft(
    IReadOnlyList<DetectedSection> Sections,
    IReadOnlyList<DetectedTable> Tables,
    IReadOnlyList<PlannedChunk> Chunks,
    IReadOnlyList<DetectedMetric> Metrics,
    DetectedMetadata Metadata,
    bool TablesUnreliable);
