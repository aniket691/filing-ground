using FinQuery.Application.Rag;

namespace FinQuery.Application.Tests;

public class EmbeddingIdTests
{
    [Fact]
    public void Uuid5_MatchesPython()
    {
        Assert.Equal("d84c509d-1421-5e04-99a2-fab9612c56bd", EmbeddingIds.For("null-embed-8", 42));
    }
}
