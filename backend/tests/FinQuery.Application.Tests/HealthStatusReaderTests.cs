using FinQuery.Application.Health;

namespace FinQuery.Application.Tests;

public class HealthStatusReaderTests
{
    [Fact]
    public void DummyMode_IsReady_AndDoesNotExposeTheLlmName()
    {
        var report = new HealthStatusReader(new FinQuerySettings
        {
            ChatMode = "dummy",
            LlmModel = "openai/gpt-oss-120b",
            GroqApiKey = "secret",
            JinaApiKey = " "
        }).Read();

        Assert.Equal("ok", report.Status);
        Assert.Equal("dummy", report.ChatMode);
        Assert.True(report.PipelineReady);
        Assert.Equal(0, report.TotalChunks);
        Assert.Equal("dummy", report.LlmModel);
        Assert.True(report.GroqConfigured);
        Assert.False(report.JinaConfigured);
    }

    [Fact]
    public void RagMode_WithoutChroma_IsNotReady()
    {
        var report = new HealthStatusReader(new FinQuerySettings
        {
            ChatMode = " RAG ",
            LlmModel = "openai/gpt-oss-120b",
            JinaApiKey = "jina"
        }).Read();

        Assert.Equal("rag", report.ChatMode);
        Assert.False(report.PipelineReady);
        Assert.Equal(0, report.TotalChunks);
        Assert.Equal("openai/gpt-oss-120b", report.LlmModel);
        Assert.False(report.GroqConfigured);
        Assert.True(report.JinaConfigured);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("chroma")]
    public void UnknownMode_FallsBackToDummy(string? mode)
    {
        Assert.Equal("dummy", HealthStatusReader.NormalizeMode(mode));
    }
}
