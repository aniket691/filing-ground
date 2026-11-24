using FinQuery.Application.Ingestion;

namespace FinQuery.Application.Tests;

public class IngestionDetectorTests
{
    [Fact]
    public void Sections_NestKnownHeadings()
    {
        var pages = new Dictionary<int, string>
        {
            [1] = "Item 1. Business\n\nThe company sells software.\n\nIncome Statement\n\nRevenue $10 million\n"
        };

        var roots = new SectionDetector().Detect(pages);

        Assert.Equal(["Business Overview"], roots.Select(section => section.Title));
        Assert.Equal("Income Statement", roots[0].Children[0].Title);
        Assert.Equal("Income Statement", roots[0].Children[0].SectionType);
        Assert.Equal(1, roots[0].PageEnd);
    }

    [Fact]
    public void Tables_ParseAlignedNumericRows()
    {
        const string page = "Annual results\nItem    2023    2024\nRevenue  10      12\nCosts    4       5\nProfit   6       7\n";

        var tables = new TableExtractor().ExtractFromPage(1, page);
        Assert.True(tables[0].ExtractionOk);
        Assert.Equal("Annual results", tables[0].Title);
        Assert.Equal(["Item", "2023", "2024"], tables[0].Headers);
        Assert.Equal(3, tables[0].Rows.Count);
        Assert.Equal("Revenue", tables[0].Rows[0][0]);
    }

    [Fact]
    public void Metrics_ScaleCurrencyAndFiscalYear()
    {
        var found = new MetricDetector().DetectInText("Revenue $10 million in fiscal year 2024.", 3, 1);

        Assert.Single(found);
        Assert.Equal("revenue", found[0].NormalizedMetricName);
        Assert.Equal(10_000_000m, found[0].ResolvedValue());
        Assert.Equal("USD", MetricDetector.ParseCurrency(found[0].CurrencySymbol, null));
        Assert.Equal("FY2024", found[0].PeriodHint);
        Assert.Equal("million", found[0].ScaleWord);
        Assert.Equal(3, found[0].PageNumber);
    }

    [Fact]
    public void Metadata_ReadsTickerAndPeriodEnd()
    {
        var pages = new Dictionary<int, string>
        {
            [1] = "NASDAQ: MSFT\nForm 10-K\nFiscal year 2024\nFiscal year ended September 28, 2024\nAmounts in millions of USD."
        };

        var meta = new MetadataExtractor().Extract(pages, "msft.txt");

        Assert.Equal("MSFT", meta.Ticker!.Value);
        Assert.Equal("10-K", meta.DocumentType!.Value);
        Assert.Equal(2024, meta.FiscalYear!.Value);
        Assert.Equal(new DateOnly(2024, 9, 28), meta.PeriodEnd!.Value);
        Assert.Equal("USD", meta.Currency!.Value);
        Assert.Equal("millions", meta.Units!.Value);
    }
}
