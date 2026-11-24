using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FinQuery.Application.Documents;
using FinQuery.Application.Ingestion;
using FinQuery.Domain.Documents;
using FinQuery.Domain.Metrics;
using FinQuery.Domain.Tables;
using FinQuery.Application.Rag;
using FinQuery.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace FinQuery.Infrastructure.Ingestion;

public sealed class DocumentIngestionPipeline(
    FinQueryDbContext db,
    IDocumentFileStore files,
    IIngestionCounters counters,
    IChunkEmbeddingStore embeddings) : IIngestionPipeline
{
    public async Task RunAsync(int documentId, bool force, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var status = "failed";
        var pages = 0;
        var tables = 0;
        var metrics = 0;
        var chunks = 0;
        try
        {
            var document = await db.Documents.FirstOrDefaultAsync(row => row.Id == documentId, cancellationToken);
            if (document is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(document.FilePath))
            {
                await FinishAsync(document, "failed", $"PipelineError: Document {document.Id} has no stored file (upload first)", cancellationToken);
                return;
            }

            if (!force && document.ProcessingStatus == "processing")
            {
                await FinishAsync(document, "failed", $"PipelineError: Document {document.Id} is already being processed", cancellationToken);
                return;
            }

            document.ProcessingStatus = "processing";
            document.ProcessingStartedAt = DateTime.UtcNow;
            document.ProcessingCompletedAt = null;
            document.ProcessingError = null;
            await db.SaveChangesAsync(cancellationToken);

            var data = files.Read(document.FilePath);
            var filename = document.FilePath.Replace('\\', '/').Split('/')[^1];
            var loaded = Load(filename, data);
            if (loaded.Count == 0)
            {
                await FinishAsync(document, "failed", "load produced no pages", cancellationToken);
                return;
            }

            var cleaned = DocumentCleaner.Clean(loaded.ToDictionary(page => page.Number, page => page.Text));
            var cleanedText = cleaned.ToDictionary(pair => pair.Key, pair => pair.Value.Text);
            var draft = IngestionAnalysis.Draft(cleanedText, filename);
            var now = DateTime.UtcNow;
            var pageRows = loaded.Select(page =>
            {
                var (text, report) = cleaned[page.Number];
                return new DocumentPage
                {
                    DocumentId = document.Id,
                    PageNumber = page.Number,
                    RawText = page.Text,
                    CleanedText = text,
                    ExtractionMethod = "text",
                    ExtractionMetadataJson = JsonSerializer.Serialize(new
                    {
                        char_count = page.Text.Length,
                        needs_ocr = page.NeedsOcr,
                        cleaning = new
                        {
                            removed_header_lines = report.RemovedHeaderLines,
                            removed_footer_lines = report.RemovedFooterLines,
                            removed_page_numbers = report.RemovedPageNumbers,
                            dehyphenated_joins = report.DehyphenatedJoins
                        }
                    }),
                    CreatedAt = now
                };
            }).ToList();

            await ClearDerivedAsync(document.Id, cancellationToken);
            db.DocumentPages.AddRange(pageRows);
            var sectionRows = await SaveSectionsAsync(document.Id, draft.Sections, now, cancellationToken);
            var chunkRows = draft.Chunks.Select(chunk => new DocumentChunk
            {
                DocumentId = document.Id,
                SectionId = FindSection(sectionRows, chunk.Metadata.GetValueOrDefault("section_title") as string, chunk.PageStart)?.Id,
                ChunkIndex = chunk.ChunkIndex,
                ChunkType = chunk.ChunkType,
                Text = chunk.Text,
                TokenCount = chunk.TokenCount,
                PageStart = chunk.PageStart,
                PageEnd = chunk.PageEnd,
                MetadataJson = JsonSerializer.Serialize(ChunkMetadata(chunk)),
                CreatedAt = now
            }).ToList();
            db.DocumentChunks.AddRange(chunkRows);
            await db.SaveChangesAsync(cancellationToken);

            var chunksByIndex = chunkRows.ToDictionary(chunk => chunk.ChunkIndex);
            var tableChunks = new Dictionary<(int Page, int Position), DocumentChunk>();
            foreach (var planned in draft.Chunks.Where(chunk => chunk.ChunkType == "table"))
            {
                if (planned.Metadata.GetValueOrDefault("table_page") is int page
                    && planned.Metadata.GetValueOrDefault("table_pos") is int position)
                {
                    tableChunks[(page, position)] = chunksByIndex[planned.ChunkIndex];
                }
            }

            for (var position = 0; position < draft.Tables.Count; position++)
            {
                var table = draft.Tables[position];
                tableChunks.TryGetValue((table.PageNumber, position), out var sourceChunk);
                var tableRow = new FinancialTable
                {
                    DocumentId = document.Id,
                    PageNumber = table.PageNumber,
                    Title = (table.Title ?? "Table")[..Math.Min(512, (table.Title ?? "Table").Length)],
                    HeadersJson = table.ExtractionOk ? JsonSerializer.Serialize(table.Headers) : "[]",
                    Units = table.Units,
                    Currency = table.Currency,
                    ExtractionConfidence = table.Confidence,
                    SourceChunkId = sourceChunk?.Id,
                    CreatedAt = now
                };
                db.FinancialTables.Add(tableRow);
                await db.SaveChangesAsync(cancellationToken);
                if (table.ExtractionOk)
                {
                    var rowIndex = 0;
                    foreach (var cells in table.Rows)
                    {
                        db.FinancialTableRows.Add(new FinancialTableRow
                        {
                            TableId = tableRow.Id,
                            RowIndex = rowIndex,
                            RowLabel = cells.Count > 0 ? cells[0][..Math.Min(512, cells[0].Length)] : null,
                            CellsJson = JsonSerializer.Serialize(cells),
                            CreatedAt = now
                        });
                        rowIndex++;
                    }
                }
                else
                {
                    db.FinancialTableRows.Add(new FinancialTableRow
                    {
                        TableId = tableRow.Id,
                        RowIndex = 0,
                        CellsJson = JsonSerializer.Serialize(new[] { table.RawText }),
                        CreatedAt = now
                    });
                }
            }

            ApplyMetadata(document, draft.Metadata);
            var companyId = document.CompanyId;
            if (companyId is null && draft.Metadata.Ticker?.Value is string ticker && ticker.Length > 0)
            {
                var company = await db.Companies
                    .Where(row => row.UserId == document.UserId && row.Ticker != null && row.Ticker.ToLower() == ticker.ToLower())
                    .OrderBy(row => row.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (company is not null)
                {
                    companyId = company.Id;
                    document.CompanyId = company.Id;
                }
            }

            var metricCount = 0;
            if (companyId is not null)
            {
                var seen = new HashSet<(string Name, string Value, int Page)>();
                foreach (var metric in draft.Metrics)
                {
                    var resolved = metric.ResolvedValue();
                    if (resolved is null || !seen.Add((metric.NormalizedMetricName, resolved.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), metric.PageNumber)))
                    {
                        continue;
                    }

                    var fiscalYear = document.FiscalYear;
                    string? fiscalQuarter = null;
                    var hint = metric.PeriodHint ?? "";
                    if (hint.StartsWith("FY", StringComparison.Ordinal) && int.TryParse(hint[2..], out var fiscal) && fiscal != 0)
                    {
                        fiscalYear = fiscal;
                    }
                    else if (hint.StartsWith('Q'))
                    {
                        var parts = hint.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        fiscalQuarter = parts[0] is "Q1" or "Q2" or "Q3" or "Q4" ? parts[0] : null;
                        if (parts.Length > 1 && int.TryParse(parts[1], out var year))
                        {
                            fiscalYear = year;
                        }
                    }

                    var unit = metric.Percent ? "%" : metric.ScaleWord ?? "units";
                    db.FinancialMetrics.Add(new FinancialMetric
                    {
                        CompanyId = companyId.Value,
                        DocumentId = document.Id,
                        MetricName = metric.RawLabel[..Math.Min(255, metric.RawLabel.Length)],
                        NormalizedMetricName = metric.NormalizedMetricName[..Math.Min(255, metric.NormalizedMetricName.Length)],
                        Value = resolved.Value,
                        Unit = unit[..Math.Min(32, unit.Length)],
                        Currency = metric.Percent ? null : MetricDetector.ParseCurrency(metric.CurrencySymbol, document.Currency),
                        FiscalYear = fiscalYear,
                        FiscalQuarter = fiscalQuarter,
                        MetricType = metric.MetricType,
                        SourcePage = metric.PageNumber,
                        SourceChunkId = metric.ChunkIndex is int index && chunksByIndex.TryGetValue(index, out var source) ? source.Id : null,
                        Confidence = metric.Confidence,
                        CreatedAt = now
                    });
                    metricCount++;
                }
            }

            document.PageCount = pageRows.Count;
            var soft = draft.TablesUnreliable;
            string? hardError = null;
            try
            {
                var embedStats = await embeddings.EmbedDocumentAsync(document.Id, cancellationToken);
                if (embedStats.Embedded == 0 && embedStats.Pending > 0)
                {
                    soft = true;
                }

                var indexed = await embeddings.IndexDocumentAsync(document.Id, cancellationToken);
                if (indexed == 0 && chunkRows.Count > 0)
                {
                    soft = true;
                }
            }
            catch (Exception exception)
            {
                hardError = $"{exception.GetType().Name}: {exception.Message}";
            }

            var finished = hardError is not null ? "failed" : soft ? "partially_processed" : "processed";
            await FinishAsync(document, finished, hardError, cancellationToken);
            status = finished;
            pages = pageRows.Count;
            tables = draft.Tables.Count;
            metrics = metricCount;
            chunks = chunkRows.Count;
        }
        catch (Exception exception)
        {
            status = "failed";
            var document = await db.Documents.FirstOrDefaultAsync(row => row.Id == documentId, cancellationToken);
            if (document is not null)
            {
                await FinishAsync(document, "failed", $"{exception.GetType().Name}: {exception.Message}", cancellationToken);
            }
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            counters.Record(status, elapsed, pages, tables, metrics, chunks);
        }
    }

    private async Task FinishAsync(Document document, string status, string? error, CancellationToken cancellationToken)
    {
        document.ProcessingStatus = status;
        document.ProcessingCompletedAt = DateTime.UtcNow;
        document.ProcessingError = error;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ClearDerivedAsync(int documentId, CancellationToken cancellationToken)
    {
        var oldPages = await db.DocumentPages.Where(page => page.DocumentId == documentId).ToListAsync(cancellationToken);
        var oldSections = await db.DocumentSections.Where(section => section.DocumentId == documentId).ToListAsync(cancellationToken);
        var oldChunks = await db.DocumentChunks.Where(chunk => chunk.DocumentId == documentId).ToListAsync(cancellationToken);
        var oldTables = await db.FinancialTables.Where(table => table.DocumentId == documentId).ToListAsync(cancellationToken);
        var tableIds = oldTables.Select(table => table.Id).ToArray();
        var oldRows = tableIds.Length == 0
            ? []
            : await db.FinancialTableRows.Where(row => tableIds.Contains(row.TableId)).ToListAsync(cancellationToken);
        var oldMetrics = await db.FinancialMetrics.Where(metric => metric.DocumentId == documentId).ToListAsync(cancellationToken);
        db.FinancialTableRows.RemoveRange(oldRows);
        db.FinancialTables.RemoveRange(oldTables);
        db.DocumentChunks.RemoveRange(oldChunks);
        db.DocumentSections.RemoveRange(oldSections);
        db.DocumentPages.RemoveRange(oldPages);
        db.FinancialMetrics.RemoveRange(oldMetrics);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<DocumentSection>> SaveSectionsAsync(int documentId, IReadOnlyList<DetectedSection> roots, DateTime now, CancellationToken cancellationToken)
    {
        var saved = new List<DocumentSection>();
        var ids = new Dictionary<DetectedSection, long>();
        foreach (var node in roots.SelectMany(root => root.Flat()))
        {
            var row = new DocumentSection
            {
                DocumentId = documentId,
                ParentSectionId = node.Parent is not null && ids.TryGetValue(node.Parent, out var parentId) ? parentId : null,
                SectionTitle = node.Title[..Math.Min(512, node.Title.Length)],
                SectionType = node.SectionType,
                PageStart = node.PageStart,
                PageEnd = node.PageEnd,
                CreatedAt = now
            };
            db.DocumentSections.Add(row);
            await db.SaveChangesAsync(cancellationToken);
            ids[node] = row.Id;
            saved.Add(row);
        }

        return saved;
    }

    private static DocumentSection? FindSection(IEnumerable<DocumentSection> rows, string? title, int? pageStart)
    {
        if (title is null)
        {
            return null;
        }

        foreach (var row in rows)
        {
            if (row.SectionTitle == title && row.PageStart <= (pageStart ?? row.PageStart))
            {
                return row;
            }
        }

        return null;
    }

    private static Dictionary<string, object?> ChunkMetadata(PlannedChunk chunk)
    {
        var metadata = new Dictionary<string, object?>(chunk.Metadata)
        {
            ["section_path"] = chunk.SectionPath
        };
        return metadata;
    }

    private static void ApplyMetadata(Document document, DetectedMetadata meta)
    {
        if (meta.DocumentType is { Confidence: >= 0.6, Value: string documentType })
        {
            document.DocumentType = documentType;
        }

        if (meta.FiscalYear is { Confidence: >= 0.6, Value: int fiscalYear })
        {
            document.FiscalYear = fiscalYear;
        }

        if (meta.FiscalQuarter is { Confidence: >= 0.7, Value: string quarter } && quarter is "Q1" or "Q2" or "Q3" or "Q4")
        {
            document.FiscalQuarter = quarter;
        }

        if (meta.PeriodStart is { Confidence: >= 0.6, Value: DateOnly periodStart })
        {
            document.ReportingPeriodStart = periodStart;
        }

        DateOnly? start = meta.PeriodStart?.Value is DateOnly startValue ? startValue : null;
        if (meta.PeriodEnd is { Confidence: >= 0.6, Value: DateOnly periodEnd } && (start is null || periodEnd >= start))
        {
            document.ReportingPeriodEnd = periodEnd;
        }

        if (meta.Currency is { Confidence: >= 0.7, Value: string code } && code.Length == 3 && code.All(char.IsLetter))
        {
            document.Currency = code.ToUpperInvariant();
        }
    }

    private static List<LoadedPage> Load(string filename, byte[] data)
    {
        var extension = UploadFormats.ExtensionOf(filename);
        if (data.Length == 0)
        {
            throw new InvalidOperationException($"Empty text payload: '{filename}'");
        }

        return extension switch
        {
            "txt" or "text" or "md" => [new LoadedPage(1, Decode(data, filename))],
            "html" or "htm" => [new LoadedPage(1, HtmlText.Extract(Decode(data, filename)))],
            "pdf" => ReadPdf(data, filename),
            _ => throw new InvalidOperationException($"Unsupported file format '{extension}'. Supported: ['htm', 'html', 'md', 'pdf', 'text', 'txt']")
        };
    }

    private static List<LoadedPage> ReadPdf(byte[] data, string filename)
    {
        try
        {
            using var document = PdfDocument.Open(data);
            return document.GetPages()
                .Select(page =>
                {
                    var text = page.Text ?? "";
                    return new LoadedPage(page.Number, text, text.Trim().Length < 32);
                })
                .ToList();
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Cannot read PDF '{filename}': {exception.Message}", exception);
        }
    }

    private static string Decode(byte[] data, string filename)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        foreach (var encoding in new[] { Encoding.UTF8, Encoding.GetEncoding(1252), Encoding.Latin1 })
        {
            try
            {
                var strict = encoding == Encoding.UTF8
                    ? new UTF8Encoding(false, true)
                    : encoding;
                return strict.GetString(data);
            }
            catch (DecoderFallbackException)
            {
                continue;
            }
        }

        throw new InvalidOperationException($"Cannot decode text file: '{filename}'");
    }

    private sealed record LoadedPage(int Number, string Text, bool NeedsOcr = false);
}

internal static class HtmlText
{
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "head", "noscript", "template", "svg", "iframe"
    };

    private static readonly HashSet<string> Blocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "li", "tr", "table", "h1", "h2", "h3", "h4", "h5", "h6",
        "br", "hr", "blockquote", "figcaption", "dd", "dt", "pre"
    };

    public static string Extract(string html)
    {
        var chunks = new List<string>();
        var skip = 0;
        var inTitle = false;
        var index = 0;
        while (index < html.Length)
        {
            if (html[index] == '<')
            {
                var end = html.IndexOf('>', index);
                if (end < 0)
                {
                    break;
                }

                var tag = html[(index + 1)..end].Trim().Trim('/').Split([' ', '\t', '\r', '\n'], 2)[0].TrimStart('/');
                var closing = html[index + 1] == '/';
                if (Skipped.Contains(tag))
                {
                    skip += closing ? -1 : 1;
                    if (skip < 0)
                    {
                        skip = 0;
                    }
                }
                else if (tag.Equals("title", StringComparison.OrdinalIgnoreCase))
                {
                    inTitle = !closing;
                }
                else if (closing && Blocks.Contains(tag))
                {
                    chunks.Add("\n");
                }

                index = end + 1;
                continue;
            }

            var next = html.IndexOf('<', index);
            if (next < 0)
            {
                next = html.Length;
            }

            var data = System.Net.WebUtility.HtmlDecode(html[index..next]);
            if (!inTitle && skip == 0 && data.Trim().Length > 0)
            {
                chunks.Add(data);
            }

            index = next;
        }

        var raw = string.Join("", chunks);
        return string.Join('\n', raw.Split('\n').Select(line => string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))).Where(line => line.Length > 0));
    }
}
