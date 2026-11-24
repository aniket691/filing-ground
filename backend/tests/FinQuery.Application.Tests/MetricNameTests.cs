using FinQuery.Application.Documents;

namespace FinQuery.Application.Tests;

public class MetricNameTests
{
    [Theory]
    [InlineData("Total Net Sales", "total_net_sales")]
    [InlineData("  EBITDA  ", "ebitda")]
    [InlineData("Free Cash Flow (FCF)", "free_cash_flow_fcf")]
    [InlineData("Debt/EBITDA", "debt_ebitda")]
    [InlineData("---", "")]
    public void Normalize_MatchesPython(string input, string expected)
    {
        Assert.Equal(expected, MetricNames.Normalize(input));
    }
}
