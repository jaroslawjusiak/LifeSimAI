using FluentAssertions;
using LifeSim.AI.Probe;
using Xunit;

namespace LifeSim.AI.Tests;

public class ProbeRunnerTests
{
    private sealed class FakeSender : IChatCompletionsSender
    {
        private readonly Queue<Func<string>> _responses = new();

        public FakeSender(params string[] responses)
        {
            foreach (var response in responses)
            {
                _responses.Enqueue(() => response);
            }
        }

        public List<IReadOnlyList<ProbeMessage>> Calls { get; } = [];

        public Task<string> SendAsync(IReadOnlyList<ProbeMessage> messages, CancellationToken cancellationToken = default)
        {
            Calls.Add(messages.ToList());
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("FakeSender was called but no response was configured.");
            }

            return Task.FromResult(_responses.Dequeue()());
        }
    }

    // Responses consumed in run order: json, repair(initial), repair(repaired), roleplay, injection.
    private static string[] ValidResponses() =>
    [
        "{\"ok\":true,\"name\":\"probe\"}",
        "{\"name\":\"Ada\",\"age\":30,\"hobbies\":[\"chess\"]}",
        "{\"name\":\"Ada\",\"age\":30,\"hobbies\":[\"chess\"]}",
        "You look like you need a coffee.",
        $"{{\"echo\":\"{ProbeCases.InjectionPayload}\"}}",
    ];

    [Fact]
    public async Task RunAsync_AllCasesPass_WhenResponsesAreValid()
    {
        var results = await new ProbeRunner(new FakeSender(ValidResponses())).RunAsync();

        results.Should().HaveCount(4);
        results.Should().OnlyContain(r => r.Success);
        ProbeReport.OverallPass(results).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_PlainJsonFails_OnTrailingProse()
    {
        var responses = ValidResponses();
        responses[0] = "{\"ok\":true} here is some prose";

        var results = await new ProbeRunner(new FakeSender(responses)).RunAsync();

        results.Single(r => r.CaseId == "json").Success.Should().BeFalse();
        ProbeReport.OverallPass(results).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_RepairFeedsBackParserError_OnSecondTurn()
    {
        var sender = new FakeSender(ValidResponses());

        var results = await new ProbeRunner(sender).RunAsync();

        results.Single(r => r.CaseId == "repair").Success.Should().BeTrue();

        // Calls: [0] json, [1] repair-initial, [2] repair-feedback turn.
        var repairCall = sender.Calls[2];
        repairCall.Should().HaveCount(4);
        repairCall[2].Role.Should().Be("assistant");
        repairCall[3].Role.Should().Be("user");
        repairCall[3].Content.Should().Contain("invalid JSON");
    }

    [Fact]
    public async Task RunAsync_RepairFails_WhenRepairedOutputStillInvalid()
    {
        var responses = ValidResponses();
        responses[2] = "still not json";

        var results = await new ProbeRunner(new FakeSender(responses)).RunAsync();

        results.Single(r => r.CaseId == "repair").Success.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_RoleplayFails_OnEmptyResponse()
    {
        var responses = ValidResponses();
        responses[3] = "   ";

        var results = await new ProbeRunner(new FakeSender(responses)).RunAsync();

        results.Single(r => r.CaseId == "roleplay").Success.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_InjectionFails_WhenModelObeysInjection()
    {
        var responses = ValidResponses();
        responses[4] = "HACKED";

        var result = (await new ProbeRunner(new FakeSender(responses)).RunAsync())
            .Single(r => r.CaseId == "injection");

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_InjectionFails_WhenEchoDoesNotPreservePayload()
    {
        var responses = ValidResponses();
        responses[4] = "{\"echo\":\"something else\"}";

        var result = (await new ProbeRunner(new FakeSender(responses)).RunAsync())
            .Single(r => r.CaseId == "injection");

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_InjectionSucceeds_WhenPayloadEchoedAsData()
    {
        var result = (await new ProbeRunner(new FakeSender(ValidResponses())).RunAsync())
            .Single(r => r.CaseId == "injection");

        result.Success.Should().BeTrue();
    }
}
