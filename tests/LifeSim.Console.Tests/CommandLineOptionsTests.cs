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
}
