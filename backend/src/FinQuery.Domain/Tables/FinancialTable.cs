namespace FinQuery.Domain.Tables;

public sealed class FinancialTable
{
    public long Id { get; set; }

    public int DocumentId { get; set; }

    public long? SourceChunkId { get; set; }

    public int? PageNumber { get; set; }

    public string? Title { get; set; }

    public string HeadersJson { get; set; } = "[]";

    public string? Units { get; set; }

    public string? Currency { get; set; }

    public decimal? ExtractionConfidence { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<FinancialTableRow> Rows { get; set; } = [];
}

public sealed class FinancialTableRow
{
    public long Id { get; set; }

    public long TableId { get; set; }

    public int RowIndex { get; set; }

    public string? RowLabel { get; set; }

    public string CellsJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; }
}
