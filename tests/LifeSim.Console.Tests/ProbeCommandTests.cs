using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using LifeSim.AI.Probe;
using LifeSim.Console.Commands;
using LifeSim.Console.Configuration;
using Spectre.Console.Testing;
using Xunit;

namespace LifeSim.Console.Tests;

public class ProbeCommandTests
{
    private const string Model = "llama-3.2-8b-instruct";

    private sealed class QueuedHandler : HttpMessageHandler
    {
        private readonly Queue<string> _bodies;

        public QueuedHandler(params string[] bodies) => _bodies = new Queue<string>(bodies);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_bodies.Dequeue(), Encoding.UTF8, "application/json"),
            });
    }

    private static LlmOptions Llm() => new()
    {
        Endpoint = "http://127.0.0.1:1337/v1",
        Model = Model,
    };

    private static string Completion(string content) =>
        JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { role = "assistant", content } } },
        });

    private static string[] ValidResponses() =>
    [
        Completion("{\"ok\":true,\"name\":\"probe\"}"),
        Completion("{\"name\":\"Ada\",\"age\":30,\"hobbies\":[\"chess\"]}"),
        Completion("{\"name\":\"Ada\",\"age\":30,\"hobbies\":[\"chess\"]}"),
        Completion("You look like you need a coffee."),
        Completion($"{{\"echo\":\"{ProbeCases.InjectionPayload}\"}}"),
    ];

    [Fact]
    public async Task Run_ReturnsZero_AndArchivesAcceptanceRecord_WhenAllPass()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lifesim-probe-{Guid.NewGuid():N}");
        var archive = Path.Combine(directory, "model-acceptance.md");

        try
        {
            var console = new TestConsole();

            var exitCode = await ProbeCommand.RunAsync(
                Llm(), new QueuedHandler(ValidResponses()), console, archive);

            exitCode.Should().Be(0);
            console.Output.Should().Contain("PASS");

            File.Exists(archive).Should().BeTrue();
            var text = File.ReadAllText(archive);
            text.Should().Contain("Model Acceptance Record");
            text.Should().Contain(Model);
            text.Should().Contain("**Overall:** PASS");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Run_ReturnsOne_WhenJsonContractCaseFails()
    {
        var responses = ValidResponses();
        responses[0] = Completion("here is some prose, not JSON");

        var console = new TestConsole();

        var exitCode = await ProbeCommand.RunAsync(
            Llm(), new QueuedHandler(responses), console, archivePath: null);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("FAIL");
    }

    [Fact]
    public async Task Run_EscapesMarkup_InResultNote()
    {
        var responses = ValidResponses();
        responses[3] = Completion("[bold]HACKED[/]");

        var console = new TestConsole();

        var exitCode = await ProbeCommand.RunAsync(
            Llm(), new QueuedHandler(responses), console, archivePath: null);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("[bold]HACKED[/]");
    }
}
