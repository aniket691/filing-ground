using FinQuery.Application.Ingestion;

namespace FinQuery.Application.Tests;

public class DocumentCleanerTests
{
    [Fact]
    public void RemovesPageNumbers_AndJoinsHyphenatedWords()
    {
        var cleaned = DocumentCleaner.Clean(new Dictionary<int, string>
        {
            [1] = "Revenue grew year over year.\n12\nincrea-\nsed demand"
        });

        Assert.Equal("Revenue grew year over year.\nincreased demand", cleaned[1].Text);
        Assert.Equal(1, cleaned[1].Report.RemovedPageNumbers);
        Assert.Equal(1, cleaned[1].Report.DehyphenatedJoins);
    }
}
