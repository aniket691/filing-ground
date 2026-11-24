using ChromaDB.Client.Models;
using ChromaDotNet.Local;

namespace FinQuery.Api.Tests;

public class ChromaLocalServerTests
{
    [Fact]
    public async Task Server_OpensAPersistentDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "fq-chroma-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(path);
        await using var server = await ChromaLocalServer.StartAsync(new ChromaLocalOptions { DataPath = path });
        using var client = server.CreateClient();
        var collection = await client.GetOrCreateCollectionClientAsync(new ChromaCollectionDefinition("financial_docs"));
        Assert.Equal(0, await collection.CountAsync());
        Assert.True(File.Exists(Path.Combine(path, "chroma.sqlite3")));
    }
}
