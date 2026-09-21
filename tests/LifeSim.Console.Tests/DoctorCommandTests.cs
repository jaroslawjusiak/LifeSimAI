using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using LifeSim.Console.Commands;
using LifeSim.Console.Configuration;
using Spectre.Console.Testing;
using Xunit;

namespace LifeSim.Console.Tests;

public class DoctorCommandTests
{
    private const string Endpoint = "http://127.0.0.1:1337/v1";
    private const string Model = "llama-3.2-8b-instruct";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<Task<HttpResponseMessage>> _responder;

        public StubHandler(Func<Task<HttpResponseMessage>> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => _responder();
    }

    private static HttpMessageHandler Responding(HttpStatusCode status, string body) =>
        new StubHandler(() => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }));

    private static LlmOptions Llm() => new() { Endpoint = Endpoint, Model = Model };

    [Fact]
    public async Task Run_ReturnsZero_AndRendersHealthy_WhenModelPresent()
    {
        var console = new TestConsole();
        var handler = Responding(
            HttpStatusCode.OK,
            """{"object":"list","data":[{"id":"llama-3.2-8b-instruct"}]}""");

        var exitCode = await DoctorCommand.RunAsync(Llm(), handler, console);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("healthy");
        console.Output.Should().Contain(Model);
    }

    [Fact]
    public async Task Run_ReturnsOne_AndRendersProblem_WhenUnreachable()
    {
        var console = new TestConsole();
        var handler = new StubHandler(() =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused")));

        var exitCode = await DoctorCommand.RunAsync(Llm(), handler, console);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("problem");
        console.Output.Should().Contain("docs/local-llm-setup.md");
    }

    [Fact]
    public async Task Run_ReturnsOne_AndListsModels_WhenModelMissing()
    {
        var console = new TestConsole();
        var handler = Responding(
            HttpStatusCode.OK,
            """{"object":"list","data":[{"id":"other-model"}]}""");

        var exitCode = await DoctorCommand.RunAsync(Llm(), handler, console);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("problem");
        console.Output.Should().Contain("other-model");
    }

    [Fact]
    public async Task Run_EscapesMarkup_InEndpointAndModel()
    {
        var console = new TestConsole();
        var malicious = new LlmOptions
        {
            Endpoint = "http://127.0.0.1:1337/v1",
            Model = "[bold]evil[/]",
        };
        var handler = Responding(HttpStatusCode.OK, """{"object":"list","data":[]}""");

        var exitCode = await DoctorCommand.RunAsync(malicious, handler, console);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("[bold]evil[/]");
    }
}
