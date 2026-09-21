using FluentAssertions;
using LifeSim.Console.Configuration;
using Xunit;

namespace LifeSim.Console.Tests;

public class CommandLineOptionsTests
{
    [Fact]
    public void Parse_DefaultsToNonVerbose()
    {
        CommandLineOptions.Parse([]).Verbose.Should().BeFalse();
    }

    [Theory]
    [InlineData("--verbose")]
    [InlineData("-v")]
    [InlineData("--VERBOSE")]
    public void Parse_RecognizesVerboseSwitch(string arg)
    {
        CommandLineOptions.Parse([arg]).Verbose.Should().BeTrue();
    }

    [Fact]
    public void Parse_IgnoresUnknownArguments()
    {
        CommandLineOptions.Parse(["--probe", "value"]).Verbose.Should().BeFalse();
    }

    [Fact]
    public void Parse_DefaultsToNoCommand()
    {
        CommandLineOptions.Parse([]).Command.Should().BeNull();
    }

    [Theory]
    [InlineData("doctor")]
    [InlineData("DOCTOR")]
    public void Parse_RecognizesBareCommandVerb(string arg)
    {
        CommandLineOptions.Parse([arg]).Command.Should().Be(arg);
    }

    [Fact]
    public void Parse_CombinesVerboseSwitchAndCommand()
    {
        var options = CommandLineOptions.Parse(["--verbose", "doctor"]);

        options.Verbose.Should().BeTrue();
        options.Command.Should().Be("doctor");
    }

    [Fact]
    public void Parse_TreatsLeadingDashTokenAsNotACommand()
    {
        CommandLineOptions.Parse(["--doctor"]).Command.Should().BeNull();
    }

    [Fact]
    public void Parse_CapturesOnlyFirstCommandVerb()
    {
        CommandLineOptions.Parse(["doctor", "probe"]).Command.Should().Be("doctor");
    }
}
