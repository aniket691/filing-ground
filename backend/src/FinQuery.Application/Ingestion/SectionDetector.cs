using System.Text.RegularExpressions;

namespace FinQuery.Application.Ingestion;

public sealed class DetectedSection
{
    public required string Title { get; init; }

    public required string SectionType { get; init; }

    public required int PageStart { get; init; }

    public int? PageEnd { get; set; }

    public DetectedSection? Parent { get; set; }

    public List<DetectedSection> Children { get; } = [];

    public bool IsKnownHeading { get; init; }

    public IEnumerable<DetectedSection> Flat()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var node in child.Flat())
            {
                yield return node;
            }
        }
    }

    public List<DetectedSection> PathToRoot()
    {
        var path = new List<DetectedSection>();
        DetectedSection? node = this;
        while (node is not null)
        {
            path.Add(node);
            node = node.Parent;
        }

        return path;
    }
}

public sealed class SectionDetector
{
    private const int MaxHeadingWords = 14;

    private static readonly Regex TitleCaseLine = new(@"^[A-Z0-9][A-Za-z0-9,&/()'’\-\s]{2,120}$", RegexOptions.CultureInvariant);
    private static readonly Regex AllCapsLine = new(@"^[A-Z0-9][A-Z0-9,&/()\-\s]{2,120}$", RegexOptions.CultureInvariant);
    private static readonly Regex NumberPrefix = new(@"^\d+(\.\d+)*[.)]?\s+\S", RegexOptions.CultureInvariant);

    private static readonly (Regex Pattern, string Title, string SectionType, bool TopLevel)[] Patterns =
    [
        Compile(@"^item\s*1[a-z]?[.\s:–-]*business\b.*$", "Business Overview", "Business Overview", true),
        Compile(@"^business overview$", "Business Overview", "Business Overview", true),
        Compile(@"^item\s*1a[.\s:–-]*risk factors?\b.*$", "Risk Factors", "Risk Factors", true),
        Compile(@"^risk factors?$", "Risk Factors", "Risk Factors", true),
        Compile(@"^(risks? (and|&) uncertainties?)$", "Risks and Uncertainties", "Risk Factors", false),
        Compile(@"^item\s*7[.\s:–-]*management'?s? (discussion and analysis|discussion).*md&a.*$", "Management Discussion and Analysis", "Management Discussion", true),
        Compile(@"^(management'?s? )?(discussion and analysis|md&a).*(and|&)?\b(liquidity)?\b.*$", "Management Discussion and Analysis", "Management Discussion", false),
        Compile(@"^item\s*7a[.\s:–-]*(quantitative and qualitative disclosures? about )?market risk\b.*$", "Market Risk", "Management Discussion", true),
        Compile(@"^market risk$", "Market Risk", "Management Discussion", false),
        Compile(@"^item\s*8[.\s:–-]*financial statements?.*$", "Financial Statements", "Financial Statements", true),
        Compile(@"^financial statements?( and supplementary data)?$", "Financial Statements", "Financial Statements", true),
        Compile(@"^(consolidated |condensed )?statements? of operations$", "Consolidated Statements of Operations", "Income Statement", false),
        Compile(@"^(consolidated |condensed )?income statements?$", "Income Statement", "Income Statement", false),
        Compile(@"^income statement$", "Income Statement", "Income Statement", false),
        Compile(@"^(consolidated |condensed )?balance sheets?$", "Balance Sheet", "Balance Sheet", false),
        Compile(@"^balance sheet$", "Balance Sheet", "Balance Sheet", false),
        Compile(@"^(consolidated |condensed )?statements? of cash flows?$", "Cash Flow Statement", "Cash Flow Statement", false),
        Compile(@"^cash flow statement$", "Cash Flow Statement", "Cash Flow Statement", false),
        Compile(@"^notes? to (the )?(consolidated )?financial statements?$", "Notes to Financial Statements", "Notes", false),
        Compile(@"^notes?$", "Notes", "Notes", false),
        Compile(@"^segment information$", "Segment Information", "Segment Information", false),
        Compile(@"^(reportable )?segments?$", "Segments", "Segment Information", false),
        Compile(@"^revenue$", "Revenue", "Other", false),
        Compile(@"^operating expenses$", "Operating Expenses", "Other", false),
        Compile(@"^(liquidity)( and capital resources)?$", "Liquidity and Capital Resources", "Management Discussion", false),
        Compile(@"^capital resources$", "Capital Resources", "Management Discussion", false)
    ];

    public List<DetectedSection> Detect(IReadOnlyDictionary<int, string> cleanedPages)
    {
        var roots = new List<DetectedSection>();
        DetectedSection? currentRoot = null;
        DetectedSection? currentChild = null;
        var open = new List<DetectedSection>();

        foreach (var pageNumber in cleanedPages.Keys.OrderBy(number => number))
        {
            var lines = cleanedPages[pageNumber].Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var prevBlank = index == 0 || string.IsNullOrWhiteSpace(lines[index - 1]);
                var nextBlank = index == lines.Length - 1 || string.IsNullOrWhiteSpace(lines[index + 1]);
                var match = MatchKnown(line);
                var generic = match is null && LooksLikeGenericHeading(prevBlank, line, nextBlank);
                if (match is null && !generic)
                {
                    continue;
                }

                string title;
                string sectionType;
                bool topLevel;
                bool known;
                if (match is not null)
                {
                    (title, sectionType, topLevel) = match.Value;
                    known = true;
                }
                else
                {
                    title = line.Trim();
                    sectionType = "Other";
                    topLevel = false;
                    known = false;
                }

                foreach (var section in open)
                {
                    section.PageEnd ??= pageNumber;
                }

                var created = new DetectedSection
                {
                    Title = title,
                    SectionType = sectionType,
                    PageStart = pageNumber,
                    IsKnownHeading = known
                };
                if (topLevel || currentRoot is null)
                {
                    roots.Add(created);
                    currentRoot = created;
                    currentChild = null;
                }
                else
                {
                    var target = currentChild ?? currentRoot;
                    created.Parent = target;
                    target.Children.Add(created);
                    currentChild = created;
                }

                open.Add(created);
            }
        }

        var lastPage = cleanedPages.Count == 0 ? 0 : cleanedPages.Keys.Max();
        foreach (var section in open)
        {
            section.PageEnd ??= lastPage;
        }

        return roots;
    }

    private static (string Title, string SectionType, bool TopLevel)? MatchKnown(string line)
    {
        var stripped = line.Trim();
        if (stripped.Length == 0 || stripped.Length > 120)
        {
            return null;
        }

        foreach (var (pattern, title, sectionType, topLevel) in Patterns)
        {
            if (pattern.IsMatch(stripped))
            {
                return (title, sectionType, topLevel);
            }
        }

        return null;
    }

    private static bool LooksLikeGenericHeading(bool prevBlank, string line, bool nextBlank)
    {
        if (!prevBlank || !nextBlank)
        {
            return false;
        }

        var stripped = line.Trim();
        var words = stripped.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is < 2 or > MaxHeadingWords)
        {
            return false;
        }

        if (NumberPrefix.IsMatch(stripped))
        {
            return true;
        }

        if (AllCapsLine.IsMatch(stripped) && stripped.Any(char.IsLetter))
        {
            return true;
        }

        if (TitleCaseLine.IsMatch(stripped) && char.IsUpper(stripped[0]))
        {
            var capitalized = words.Count(word => word.Length > 0 && char.IsUpper(word[0]));
            return capitalized / (double)words.Length >= 0.6;
        }

        return false;
    }

    private static (Regex Pattern, string Title, string SectionType, bool TopLevel) Compile(string pattern, string title, string sectionType, bool topLevel) =>
        (new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), title, sectionType, topLevel);
}
