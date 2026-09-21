using FluentAssertions;
using LifeSim.AI.Probe;
using Xunit;

namespace LifeSim.AI.Tests;

public class JsonContractParserTests
{
    [Fact]
    public void TrimFences_RemovesLeadingLanguageTaggedFence()
    {
        JsonContractParser.TrimFences("```json\n{\"ok\":true}\n```").Should().Be("{\"ok\":true}");
    }

    [Fact]
    public void TrimFences_RemovesUntaggedFence()
    {
        JsonContractParser.TrimFences("```\n[1,2,3]\n```").Should().Be("[1,2,3]");
    }

    [Fact]
    public void TrimFences_LeavesPlainJsonUntouched()
    {
        JsonContractParser.TrimFences("  {\"ok\":true}  ").Should().Be("{\"ok\":true}");
    }

    [Fact]
    public void TryParseDocument_ParsesValidJson()
    {
        using var document = JsonContractParser.TryParseDocument("{\"ok\":true}", out var error);

        document.Should().NotBeNull();
        error.Should().BeNull();
        document!.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void TryParseDocument_ParsesFencedJson()
    {
        using var document = JsonContractParser.TryParseDocument("```json\n{\"ok\":true}\n```", out var error);

        document.Should().NotBeNull();
        error.Should().BeNull();
    }

    [Fact]
    public void TryParseDocument_ReturnsNullAndError_OnInvalidJson()
    {
        using var document = JsonContractParser.TryParseDocument("{\"ok\": true", out var error);

        document.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryParseDocument_ReturnsNullAndError_OnTrailingProse()
    {
        using var document = JsonContractParser.TryParseDocument("{\"ok\":true} extra prose", out var error);

        document.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryParseDocument_ReturnsNullAndError_OnEmpty()
    {
        using var document = JsonContractParser.TryParseDocument("   ", out var error);

        document.Should().BeNull();
        error.Should().Contain("empty");
    }
}
