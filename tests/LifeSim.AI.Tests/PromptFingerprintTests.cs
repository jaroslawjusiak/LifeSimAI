using FluentAssertions;
using LifeSim.AI.Diagnostics;
using Microsoft.Extensions.AI;
using Xunit;

namespace LifeSim.AI.Tests;

public class PromptFingerprintTests
{
    [Fact]
    public void Compute_IsStable_ForIdenticalInput()
    {
        var messages = new[] { new ChatMessage(ChatRole.User, "hello") };
        var options = new ChatOptions { ModelId = "m", Temperature = 0.5f };

        PromptFingerprint.Compute(messages, options)
            .Should().Be(PromptFingerprint.Compute(messages, options));
    }

    [Fact]
    public void Compute_Differs_WhenContentChanges()
    {
        var first = PromptFingerprint.Compute([new ChatMessage(ChatRole.User, "hello")], null);
        var second = PromptFingerprint.Compute([new ChatMessage(ChatRole.User, "goodbye")], null);

        first.Should().NotBe(second);
    }

    [Fact]
    public void Compute_Differs_WhenModelChanges()
    {
        var messages = new[] { new ChatMessage(ChatRole.User, "hello") };

        var first = PromptFingerprint.Compute(messages, new ChatOptions { ModelId = "a" });
        var second = PromptFingerprint.Compute(messages, new ChatOptions { ModelId = "b" });

        first.Should().NotBe(second);
    }
}
