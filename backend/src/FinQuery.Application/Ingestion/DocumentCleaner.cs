using System.Text;
using System.Text.RegularExpressions;

namespace FinQuery.Application.Ingestion;

public sealed record CleaningReport(
    int RemovedHeaderLines,
    int RemovedFooterLines,
    int RemovedPageNumbers,
    int DehyphenatedJoins);

public static class DocumentCleaner
{
    private static readonly Regex PageNumberLine = new(
        @"^\s*(?:page\s+)?[-–—|]?\s*\d{1,4}\s*(?:of\s+\d{1,5})?\s*[-–—|]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex HyphenatedLineEnd = new(@"([A-Za-z])-\s*\n\s*([a-z])", RegexOptions.CultureInvariant);
    private static readonly Regex MultipleSpaces = new(@"[ \t]{2,}", RegexOptions.CultureInvariant);
    private static readonly Regex TrailingSpaces = new(@"[ \t]+$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex ExcessBlanks = new(@"\n{3,}", RegexOptions.CultureInvariant);

    public static IReadOnlyDictionary<int, (string Text, CleaningReport Report)> Clean(IReadOnlyDictionary<int, string> rawPages)
    {
        var totalPages = rawPages.Count;
        var headerCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var footerCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var normalized = new Dictionary<int, List<string>>();
        foreach (var (pageNumber, text) in rawPages)
        {
            var lines = text.Split('\n')
                .Select(line => string.Join(' ', NormalizeUnicode(line).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
                .Where(line => line.Length > 0)
                .ToList();
            normalized[pageNumber] = lines;
            var body = lines.Where(line => !PageNumberLine.IsMatch(line)).ToList();
            if (body.Count >= 3)
            {
                headerCounts[body[0]] = headerCounts.GetValueOrDefault(body[0]) + 1;
                footerCounts[body[^1]] = footerCounts.GetValueOrDefault(body[^1]) + 1;
            }
        }

        var headers = headerCounts.Where(pair => IsRepeated(pair.Key, pair.Value, totalPages)).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        var footers = footerCounts.Where(pair => IsRepeated(pair.Key, pair.Value, totalPages) && !headers.Contains(pair.Key)).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        return normalized.ToDictionary(
            pair => pair.Key,
            pair => CleanPage(pair.Value, headers, footers));
    }

    public static string NormalizeUnicode(string text)
    {
        var builder = new StringBuilder(text);
        builder.Replace('\u00a0', ' ').Replace('\u202f', ' ').Replace('\u2009', ' ').Replace('\u2007', ' ');
        builder.Replace('\u2011', '-').Replace('\u2013', '-').Replace('\u2014', '-').Replace('\u2212', '-');
        builder.Replace('\u2018', '\'').Replace('\u2019', '\'').Replace('\u201c', '"').Replace('\u201d', '"');
        builder.Replace("\u2026", "...");
        return builder.ToString().Normalize(NormalizationForm.FormKC);
    }

    private static bool IsRepeated(string line, int occurrences, int totalPages) =>
        totalPages >= 3 && line.Trim().Length > 0 && occurrences / (double)totalPages >= 0.6;

    private static (string Text, CleaningReport Report) CleanPage(List<string> lines, HashSet<string> headers, HashSet<string> footers)
    {
        var removedHeaders = 0;
        var removedFooters = 0;
        var removedPages = 0;
        var kept = new List<string>();
        for (var index = 0; index < lines.Count; index++)
        {
            var stripped = lines[index].Trim();
            if (stripped.Length == 0)
            {
                continue;
            }

            if (PageNumberLine.IsMatch(stripped))
            {
                removedPages++;
                continue;
            }

            if (index == 0 && headers.Contains(stripped))
            {
                removedHeaders++;
                continue;
            }

            if (index == lines.Count - 1 && footers.Contains(stripped))
            {
                removedFooters++;
                continue;
            }

            kept.Add(lines[index]);
        }

        var joins = 0;
        var text = string.Join('\n', kept);
        text = HyphenatedLineEnd.Replace(text, match =>
        {
            joins++;
            return match.Groups[1].Value + match.Groups[2].Value;
        });
        text = TrailingSpaces.Replace(text, "");
        text = string.Join('\n', text.Split('\n').Select(line => MultipleSpaces.Replace(line, " ")));
        text = ExcessBlanks.Replace(text, "\n\n").Trim();
        return (text, new CleaningReport(removedHeaders, removedFooters, removedPages, joins));
    }
}
