using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FinQuery.Api.Auth;
using FinQuery.Domain.Documents;

namespace FinQuery.Api.Documents;

public static class GraphRequestReader
{
    private static readonly Regex Quarter = new("^Q[1-4]$", RegexOptions.CultureInvariant);
    private static readonly Regex Currency = new("^[A-Z]{3}$", RegexOptions.CultureInvariant);
    private static readonly string[] DocumentTypes = ["10-K", "10-Q", "8-K", "Annual Report", "Earnings Release", "Financial Statement", "Investor Presentation", "Other", "Quarterly Report", "Research Report"];
    private static readonly string[] Statuses = ["completed", "failed", "partially_processed", "pending", "processed", "processing", "queued", "uploaded"];
    private static readonly string[] ExtractionMethods = ["mixed", "ocr", "table", "text"];
    private static readonly string[] SectionTypes = ["Balance Sheet", "Business Overview", "Cash Flow Statement", "Financial Statements", "Income Statement", "Management Discussion", "Notes", "Other", "Risk Factors", "Segment Information"];
    private static readonly string[] ChunkTypes = ["financial_metric", "section_summary", "table", "table_cell", "table_row", "text"];
    private static readonly string[] MetricTypes = ["balance_sheet", "cash_flow", "income_statement", "margin", "other", "per_share", "ratio", "valuation"];

    public static Document ReadDocument(JsonElement body)
    {
        var fields = ReadObject(body, ["company_id", "document_type", "title", "source_url", "source_name", "filing_date", "reporting_period_start", "reporting_period_end", "fiscal_year", "fiscal_quarter", "currency", "language", "file_path", "file_hash", "page_count", "processing_status"]);
        var documentType = RequiredString(fields, "document_type", 1, 64);
        if (!DocumentTypes.Contains(documentType))
        {
            throw new BodyValidationException($"Unsupported document_type '{documentType}'. Supported: {List(DocumentTypes)}", "document_type");
        }

        var status = OptionalString(fields, "processing_status", 32) ?? "uploaded";
        if (!Statuses.Contains(status))
        {
            throw new BodyValidationException($"Unsupported processing_status '{status}'. Supported: {List(Statuses)}", "processing_status");
        }

        var start = OptionalDate(fields, "reporting_period_start");
        var end = OptionalDate(fields, "reporting_period_end");
        if (start is not null && end is not null && end < start)
        {
            throw new BodyValidationException("reporting_period_end must be >= reporting_period_start", "reporting_period_end");
        }

        var quarter = OptionalString(fields, "fiscal_quarter", 2);
        if (quarter is not null && !Quarter.IsMatch(quarter))
        {
            throw new BodyValidationException("String should match pattern '^Q[1-4]$'", "fiscal_quarter");
        }

        var currency = OptionalString(fields, "currency", 3);
        if (currency is not null && !Currency.IsMatch(currency))
        {
            throw new BodyValidationException("String should match pattern '^[A-Z]{3}$'", "currency");
        }

        return new Document
        {
            CompanyId = OptionalInt(fields, "company_id"),
            DocumentType = documentType,
            Title = RequiredString(fields, "title", 1, 512),
            SourceUrl = OptionalString(fields, "source_url", 2048),
            SourceName = OptionalString(fields, "source_name", 255),
            FilingDate = OptionalDate(fields, "filing_date"),
            ReportingPeriodStart = start,
            ReportingPeriodEnd = end,
            FiscalYear = OptionalBoundedInt(fields, "fiscal_year", 1900, 2100),
            FiscalQuarter = quarter,
            Currency = currency,
            Language = OptionalString(fields, "language", 8) ?? "en",
            FilePath = OptionalString(fields, "file_path", 1024),
            FileHash = OptionalString(fields, "file_hash", 64),
            PageCount = OptionalMinInt(fields, "page_count", 1),
            ProcessingStatus = status
        };
    }

    public static Application.Documents.PageWrite ReadPage(JsonElement body)
    {
        var fields = ReadObject(body, ["page_number", "raw_text", "cleaned_text", "extraction_method", "extraction_metadata"]);
        var raw = OptionalString(fields, "raw_text", int.MaxValue);
        var cleaned = OptionalString(fields, "cleaned_text", int.MaxValue);
        if (raw is null && cleaned is null)
        {
            throw new BodyValidationException("Provide raw_text and/or cleaned_text", "raw_text");
        }

        var method = OptionalString(fields, "extraction_method", 16) ?? "text";
        if (!ExtractionMethods.Contains(method))
        {
            throw new BodyValidationException($"Unsupported extraction_method '{method}'. Supported: {List(ExtractionMethods)}", "extraction_method");
        }

        return new Application.Documents.PageWrite(
            RequiredMinInt(fields, "page_number", 1),
            raw,
            cleaned,
            method,
            OptionalJson(fields, "extraction_metadata", JsonValueKind.Object) ?? "{}");
    }

    public static Application.Documents.SectionWrite ReadSection(JsonElement body)
    {
        var fields = ReadObject(body, ["section_title", "section_type", "page_start", "page_end", "text", "parent_section_id"]);
        var type = RequiredString(fields, "section_type", 1, 64);
        if (!SectionTypes.Contains(type))
        {
            throw new BodyValidationException($"Unsupported section_type '{type}'. Supported: {List(SectionTypes)}", "section_type");
        }

        var pageStart = OptionalMinInt(fields, "page_start", 1);
        var pageEnd = OptionalMinInt(fields, "page_end", 1);
        if (pageStart is not null && pageEnd is not null && pageEnd < pageStart)
        {
            throw new BodyValidationException("page_end must be >= page_start", "page_end");
        }

        return new Application.Documents.SectionWrite(
            RequiredString(fields, "section_title", 1, 512),
            type,
            pageStart,
            pageEnd,
            OptionalString(fields, "text", int.MaxValue),
            OptionalLong(fields, "parent_section_id"));
    }

    public static Application.Documents.ChunkWrite ReadChunk(JsonElement body)
    {
        var fields = ReadObject(body, ["chunk_index", "text", "section_id", "chunk_type", "token_count", "page_start", "page_end", "metadata"]);
        var type = OptionalString(fields, "chunk_type", 32) ?? "text";
        if (!ChunkTypes.Contains(type))
        {
            throw new BodyValidationException($"Unsupported chunk_type '{type}'. Supported: {List(ChunkTypes)}", "chunk_type");
        }

        var pageStart = OptionalMinInt(fields, "page_start", 1);
        var pageEnd = OptionalMinInt(fields, "page_end", 1);
        if (pageStart is not null && pageEnd is not null && pageEnd < pageStart)
        {
            throw new BodyValidationException("page_end must be >= page_start", "page_end");
        }

        return new Application.Documents.ChunkWrite(
            RequiredMinInt(fields, "chunk_index", 0),
            RequiredString(fields, "text", 1, int.MaxValue),
            OptionalLong(fields, "section_id"),
            type,
            OptionalMinInt(fields, "token_count", 0),
            pageStart,
            pageEnd,
            OptionalJson(fields, "metadata", JsonValueKind.Object) ?? "{}");
    }

    public static Application.Documents.TableWrite ReadTable(JsonElement body)
    {
        var fields = ReadObject(body, ["title", "page_number", "headers", "rows", "units", "currency", "source_chunk_id", "extraction_confidence"]);
        var headers = OptionalJson(fields, "headers", JsonValueKind.Array) ?? "[]";
        var rows = ReadRows(fields);
        if (headers == "[]" && rows.Count == 0)
        {
            throw new BodyValidationException("Table must define headers and/or rows", "headers");
        }

        if (rows.Select(row => row.RowIndex).Distinct().Count() != rows.Count)
        {
            throw new BodyValidationException("Duplicate row_index values in table rows", "rows");
        }

        var currency = OptionalString(fields, "currency", 3);
        if (currency is not null && !Currency.IsMatch(currency))
        {
            throw new BodyValidationException("String should match pattern '^[A-Z]{3}$'", "currency");
        }

        return new Application.Documents.TableWrite(
            OptionalString(fields, "title", 512),
            OptionalMinInt(fields, "page_number", 1),
            headers,
            OptionalString(fields, "units", 32),
            currency,
            OptionalLong(fields, "source_chunk_id"),
            OptionalDecimal(fields, "extraction_confidence", 0m, 1m),
            rows);
    }

    public static Application.Documents.MetricWrite ReadMetric(JsonElement body)
    {
        var fields = ReadObject(body, ["document_id", "metric_name", "value", "unit", "currency", "period_start", "period_end", "fiscal_year", "fiscal_quarter", "metric_type", "source_page", "source_chunk_id", "confidence"]);
        var metricType = OptionalString(fields, "metric_type", 32);
        if (metricType is not null && !MetricTypes.Contains(metricType))
        {
            throw new BodyValidationException($"Unsupported metric_type '{metricType}'. Supported: {List(MetricTypes)}", "metric_type");
        }

        var quarter = OptionalString(fields, "fiscal_quarter", 2);
        if (quarter is not null && !Quarter.IsMatch(quarter))
        {
            throw new BodyValidationException("String should match pattern '^Q[1-4]$'", "fiscal_quarter");
        }

        var currency = OptionalString(fields, "currency", 3);
        if (currency is not null && !Currency.IsMatch(currency))
        {
            throw new BodyValidationException("String should match pattern '^[A-Z]{3}$'", "currency");
        }

        var sourcePage = OptionalMinInt(fields, "source_page", 1);
        var sourceChunk = OptionalLong(fields, "source_chunk_id");
        if (sourcePage is null && sourceChunk is null)
        {
            throw new BodyValidationException("Missing provenance: provide source_chunk_id and/or source_page", "source_page");
        }

        var start = OptionalDate(fields, "period_start");
        var end = OptionalDate(fields, "period_end");
        if (start is not null && end is not null && end < start)
        {
            throw new BodyValidationException("period_end must be >= period_start", "period_end");
        }

        if (!fields.TryGetValue("value", out var valueNode))
        {
            throw new BodyValidationException("Field required", "value");
        }

        return new Application.Documents.MetricWrite(
            RequiredInt(fields, "document_id"),
            RequiredString(fields, "metric_name", 1, 255),
            ReadDecimal(valueNode, "value"),
            OptionalString(fields, "unit", 32),
            currency,
            start,
            end,
            OptionalBoundedInt(fields, "fiscal_year", 1900, 2100),
            quarter,
            metricType,
            sourcePage,
            sourceChunk,
            OptionalDecimal(fields, "confidence", 0m, 1m));
    }

    private static List<Application.Documents.TableRowWrite> ReadRows(Dictionary<string, JsonElement> fields)
    {
        if (!fields.TryGetValue("rows", out var node) || node.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (node.ValueKind != JsonValueKind.Array)
        {
            throw new BodyValidationException("Input should be a valid list", "rows");
        }

        var rows = new List<Application.Documents.TableRowWrite>();
        foreach (var item in node.EnumerateArray())
        {
            var rowFields = ReadObject(item, ["row_index", "row_label", "cells"]);
            rows.Add(new Application.Documents.TableRowWrite(
                RequiredMinInt(rowFields, "row_index", 0),
                OptionalString(rowFields, "row_label", 512),
                OptionalJson(rowFields, "cells", JsonValueKind.Array) ?? "[]"));
        }

        return rows;
    }

    private static Dictionary<string, JsonElement> ReadObject(JsonElement body, string[] allowed)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            throw new BodyValidationException("Request body must be an object.", "body");
        }

        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in body.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                throw new BodyValidationException("Extra inputs are not permitted", property.Name);
            }

            fields[property.Name] = property.Value;
        }

        return fields;
    }

    private static string RequiredString(Dictionary<string, JsonElement> fields, string name, int min, int max)
    {
        var value = OptionalString(fields, name, max) ?? throw new BodyValidationException("Field required", name);
        if (value.Length < min)
        {
            throw new BodyValidationException("String should have at least 1 character", name);
        }

        return value;
    }

    private static string? OptionalString(Dictionary<string, JsonElement> fields, string name, int max)
    {
        if (!fields.TryGetValue(name, out var node) || node.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (node.ValueKind != JsonValueKind.String)
        {
            throw new BodyValidationException("Input should be a valid string", name);
        }

        var value = node.GetString() ?? "";
        if (value.Length > max)
        {
            throw new BodyValidationException($"String should have at most {max} characters", name);
        }

        return value;
    }

    private static int RequiredInt(Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var node) || node.ValueKind != JsonValueKind.Number || !node.TryGetInt32(out var value))
        {
            throw new BodyValidationException("Field required", name);
        }

        return value;
    }

    private static int RequiredMinInt(Dictionary<string, JsonElement> fields, string name, int min)
    {
        var value = RequiredInt(fields, name);
        if (value < min)
        {
            throw new BodyValidationException($"Input should be greater than or equal to {min}", name);
        }

        return value;
    }

    private static int? OptionalInt(Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var node) || node.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (node.ValueKind != JsonValueKind.Number || !node.TryGetInt32(out var value))
        {
            throw new BodyValidationException("Input should be a valid integer", name);
        }

        return value;
    }

    private static int? OptionalMinInt(Dictionary<string, JsonElement> fields, string name, int min)
    {
        var value = OptionalInt(fields, name);
        if (value is not null && value < min)
        {
            throw new BodyValidationException($"Input should be greater than or equal to {min}", name);
        }

        return value;
    }

    private static int? OptionalBoundedInt(Dictionary<string, JsonElement> fields, string name, int min, int max)
    {
        var value = OptionalInt(fields, name);
        if (value is not null && (value < min || value > max))
        {
            throw new BodyValidationException($"Input should be greater than or equal to {min}", name);
        }

        return value;
    }

    private static long? OptionalLong(Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var node) || node.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (node.ValueKind != JsonValueKind.Number || !node.TryGetInt64(out var value))
        {
            throw new BodyValidationException("Input should be a valid integer", name);
        }

        return value;
    }

    private static DateOnly? OptionalDate(Dictionary<string, JsonElement> fields, string name)
    {
        var raw = OptionalString(fields, name, 32);
        if (raw is null)
        {
            return null;
        }

        if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new BodyValidationException("Input should be a valid date", name);
        }

        return date;
    }

    private static decimal? OptionalDecimal(Dictionary<string, JsonElement> fields, string name, decimal min, decimal max)
    {
        if (!fields.TryGetValue(name, out var node) || node.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var value = ReadDecimal(node, name);
        if (value < min || value > max)
        {
            throw new BodyValidationException($"Input should be greater than or equal to {min}", name);
        }

        return value;
    }

    private static decimal ReadDecimal(JsonElement node, string name)
    {
        if (node.ValueKind == JsonValueKind.String)
        {
            var text = node.GetString() ?? "";
            if (text is "NaN" or "Infinity" or "-Infinity"
                || !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                throw new BodyValidationException("Input should be a finite number", name);
            }

            return parsed;
        }

        if (node.ValueKind == JsonValueKind.Number && node.TryGetDecimal(out var number))
        {
            return number;
        }

        throw new BodyValidationException("Input should be a finite number", name);
    }

    private static string? OptionalJson(Dictionary<string, JsonElement> fields, string name, JsonValueKind kind)
    {
        if (!fields.TryGetValue(name, out var node) || node.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (node.ValueKind != kind)
        {
            throw new BodyValidationException("Input should be a valid JSON value", name);
        }

        if (name == "headers")
        {
            foreach (var item in node.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    throw new BodyValidationException("Input should be a valid string", name);
                }
            }
        }

        return node.GetRawText();
    }

    private static string List(IEnumerable<string> values) =>
        "[" + string.Join(", ", values.Select(value => $"'{value}'")) + "]";
}
