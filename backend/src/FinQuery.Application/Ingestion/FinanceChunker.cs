namespace FinQuery.Application.Ingestion;

public sealed class PlannedChunk
{
    public required string Text { get; init; }

    public required string ChunkType { get; init; }

    public required int PageStart { get; init; }

    public required int PageEnd { get; init; }

    public required int TokenCount { get; init; }

    public List<string> SectionPath { get; init; } = [];

    public Dictionary<string, object?> Metadata { get; init; } = [];

    public int ChunkIndex { get; set; }
}

public static class TokenCounter
{
    public const int ChunkSize = 400;

    /// <summary>
    /// Python uses tiktoken cl100k_base and falls back to len/4 only when the
    /// encoder cannot load. Short filing text stays in one chunk either way.
    /// </summary>
    public static int Count(string text) => Math.Max(1, text.Length / 4);
}

public sealed class FinanceAwareChunker
{
    private readonly int _target;
    private readonly int _overlap;

    public FinanceAwareChunker(int? targetTokens = null, int overlapSentences = 1)
    {
        _target = targetTokens ?? TokenCounter.ChunkSize;
        _overlap = overlapSentences;
    }

    public List<PlannedChunk> BuildChunks(
        IReadOnlyDictionary<int, string> cleanedPages,
        IReadOnlyList<SectionPayload> sections,
        IReadOnlyList<DetectedTable> tables)
    {
        var tablesByPage = tables.GroupBy(table => table.PageNumber).ToDictionary(group => group.Key, group => group.ToList());
        var chunks = new List<PlannedChunk>();
        foreach (var section in sections)
        {
            chunks.AddRange(ChunkBlocks(BlocksFromLines(section.Lines, tablesByPage), section));
        }

        var covered = new HashSet<int>();
        foreach (var section in sections)
        {
            var end = section.PageEnd;
            for (var page = section.PageStart; page <= end; page++)
            {
                covered.Add(page);
            }
        }

        var uncovered = cleanedPages.Keys.Where(page => !covered.Contains(page)).OrderBy(page => page).ToList();
        if (uncovered.Count > 0)
        {
            var lines = new List<(int Page, string Line)>();
            foreach (var pageNumber in uncovered)
            {
                foreach (var line in cleanedPages[pageNumber].Split('\n'))
                {
                    if (line.Trim().Length > 0)
                    {
                        lines.Add((pageNumber, line));
                    }
                }
            }

            if (lines.Count > 0)
            {
                var fallback = new SectionPayload("(unsectioned content)", "Other", uncovered[0], uncovered[^1], [], lines);
                chunks.AddRange(ChunkBlocks(BlocksFromLines(lines, tablesByPage), fallback));
            }
        }

        for (var index = 0; index < chunks.Count; index++)
        {
            chunks[index].ChunkIndex = index;
        }

        return chunks;
    }

    private List<ParagraphBlock> BlocksFromLines(IReadOnlyList<(int Page, string Line)> lines, Dictionary<int, List<DetectedTable>> tablesByPage)
    {
        var blocks = new List<ParagraphBlock>();
        var buffer = new List<string>();
        var bufferPages = new HashSet<int>();
        void Flush()
        {
            if (buffer.Count > 0)
            {
                var text = string.Join('\n', buffer).Trim();
                if (text.Length > 0)
                {
                    blocks.Add(new ParagraphBlock(text, [.. bufferPages], false, null));
                }
            }

            buffer.Clear();
            bufferPages.Clear();
        }

        foreach (var (pageNumber, line) in lines)
        {
            if (line.Trim().Length == 0)
            {
                Flush();
                continue;
            }

            buffer.Add(line);
            bufferPages.Add(pageNumber);
            if (string.Join(' ', buffer).Length >= 1200)
            {
                Flush();
            }
        }

        Flush();
        var result = new List<ParagraphBlock>();
        var emitted = new HashSet<(int Page, int Position)>();
        foreach (var block in blocks)
        {
            result.Add(block);
            foreach (var page in block.Pages)
            {
                if (!tablesByPage.TryGetValue(page, out var tablesOnPage))
                {
                    continue;
                }

                for (var position = 0; position < tablesOnPage.Count; position++)
                {
                    var key = (page, position);
                    if (!emitted.Add(key))
                    {
                        continue;
                    }

                    result.Add(new ParagraphBlock(RenderTable(tablesOnPage[position]), [page], true, key));
                }
            }
        }

        foreach (var page in tablesByPage.Keys.OrderBy(page => page))
        {
            var tablesOnPage = tablesByPage[page];
            for (var position = 0; position < tablesOnPage.Count; position++)
            {
                if (emitted.Contains((page, position)))
                {
                    continue;
                }

                result.Add(new ParagraphBlock(RenderTable(tablesOnPage[position]), [page], true, (page, position)));
                emitted.Add((page, position));
            }
        }

        return result;
    }

    private static string RenderTable(DetectedTable table)
    {
        var lines = new List<string> { $"[TABLE] {table.Title ?? "Table"}" };
        if (table.Headers.Count > 0)
        {
            lines.Add(string.Join(" | ", table.Headers));
        }

        foreach (var row in table.Rows.Take(40))
        {
            lines.Add(string.Join(" | ", row));
        }

        return string.Join('\n', lines);
    }

    private List<PlannedChunk> ChunkBlocks(List<ParagraphBlock> blocks, SectionPayload section)
    {
        var chunks = new List<PlannedChunk>();
        var pack = new List<ParagraphBlock>();
        var packTokens = 0;
        void FlushPack()
        {
            if (pack.Count == 0)
            {
                return;
            }

            var text = string.Join("\n\n", pack.Select(block => block.Text));
            var pages = pack.SelectMany(block => block.Pages).ToHashSet();
            chunks.Add(MakeChunk(text, "text", pages.Min(), pages.Max(), section, null));
            pack.Clear();
            packTokens = 0;
        }

        foreach (var block in blocks)
        {
            var blockTokens = TokenCounter.Count(block.Text);
            if (block.IsTable)
            {
                FlushPack();
                chunks.Add(MakeChunk(block.Text, "table", block.Pages.Min(), block.Pages.Max(), section, block.TableRef));
                continue;
            }

            if (blockTokens > _target)
            {
                FlushPack();
                chunks.AddRange(SplitLargeBlock(block, section));
                continue;
            }

            if (packTokens + blockTokens > _target)
            {
                FlushPack();
            }

            pack.Add(block);
            packTokens += blockTokens;
        }

        FlushPack();
        return chunks;
    }

    private List<PlannedChunk> SplitLargeBlock(ParagraphBlock block, SectionPayload section)
    {
        var sentences = SplitSentences(block.Text);
        var output = new List<PlannedChunk>();
        var current = new List<string>();
        var currentTokens = 0;
        foreach (var sentence in sentences)
        {
            var tokens = TokenCounter.Count(sentence);
            if (current.Count > 0 && currentTokens + tokens > _target)
            {
                output.Add(SentenceChunk(current, block, section));
                var tail = _overlap > 0 ? current.TakeLast(_overlap).ToList() : [];
                current = [.. tail, sentence];
                currentTokens = current.Sum(TokenCounter.Count);
            }
            else
            {
                current.Add(sentence);
                currentTokens += tokens;
            }
        }

        if (current.Count > 0)
        {
            output.Add(SentenceChunk(current, block, section));
        }

        return output;
    }

    private static PlannedChunk SentenceChunk(List<string> sentences, ParagraphBlock block, SectionPayload section) =>
        MakeChunk(string.Join(' ', sentences), block.IsTable ? "table" : "text", block.Pages.Min(), block.Pages.Max(), section, null);

    private static PlannedChunk MakeChunk(string text, string chunkType, int pageStart, int pageEnd, SectionPayload section, (int Page, int Position)? tableRef)
    {
        var path = section.Path.Count > 0
            ? section.Path
            : section.Title.Length > 0 ? [section.Title] : new List<string>();
        var metadata = new Dictionary<string, object?>
        {
            ["section_title"] = section.Title,
            ["section_type"] = section.SectionType
        };
        if (tableRef is not null)
        {
            metadata["table_page"] = tableRef.Value.Page;
            metadata["table_pos"] = tableRef.Value.Position;
        }

        return new PlannedChunk
        {
            Text = text,
            ChunkType = chunkType,
            PageStart = pageStart,
            PageEnd = pageEnd,
            TokenCount = TokenCounter.Count(text),
            SectionPath = path.Select(item => item).ToList(),
            Metadata = metadata
        };
    }

    private static List<string> SplitSentences(string text)
    {
        var parts = new List<string>();
        var current = new List<string>();
        foreach (var token in text.Replace("! ", "!|").Replace("? ", "?|").Split('|'))
        {
            var sub = token.Split(". ");
            for (var index = 0; index < sub.Length; index++)
            {
                var piece = sub[index];
                if (index < sub.Length - 1)
                {
                    current.Add(piece.TrimEnd('.') + ".");
                    parts.Add(string.Join(' ', current));
                    current = [];
                }
                else if (piece.Trim().Length > 0)
                {
                    current.Add(piece);
                }
            }
        }

        if (current.Count > 0)
        {
            parts.Add(string.Join(' ', current));
        }

        return parts.Select(part => part.Trim()).Where(part => part.Length > 0).ToList();
    }

    private sealed record ParagraphBlock(string Text, HashSet<int> Pages, bool IsTable, (int Page, int Position)? TableRef);
}

public sealed record SectionPayload(
    string Title,
    string SectionType,
    int PageStart,
    int PageEnd,
    List<string> Path,
    List<(int Page, string Line)> Lines);
