using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using LifeSim.AI.Probe;
using Xunit;

namespace LifeSim.AI.Tests;

public class ProbeSenderTests
{
    private const string Endpoint = "http://127.0.0.1:1337/v1";
    private const string Model = "llama-3.2-8b-instruct";

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public CapturingHandler(string body, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _body = body;
            _statusCode = statusCode;
        }

        public HttpRequestMessage? Request { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static string Completion(string content) =>
        JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { role = "assistant", content } } },
        });

    [Fact]
    public async Task SendAsync_PostsToChatCompletions_AndReturnsContent()
    {
        var handler = new CapturingHandler(Completion("hello"));
        var sender = new ProbeSender(handler, Endpoint, Model);

        var content = await sender.SendAsync([new ProbeMessage("user", "hi")]);

        content.Should().Be("hello");
        handler.Request!.RequestUri!.AbsoluteUri.Should().Be($"{Endpoint}/chat/completions");
        handler.Request.Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task SendAsync_SendsAuthorizationHeader_WhenApiKeyProvided()
    {
        var handler = new CapturingHandler(Completion("hello"));
        var sender = new ProbeSender(handler, Endpoint, Model, apiKey: "secret-token");

        await sender.SendAsync([new ProbeMessage("user", "hi")]);

        handler.Request!.Headers.Authorization.Should().NotBeNull();
        handler.Request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Request.Headers.Authorization.Parameter.Should().Be("secret-token");
    }

    [Fact]
    public async Task SendAsync_OmitsAuthorizationHeader_WhenApiKeyNull()
    {
        var handler = new CapturingHandler(Completion("hello"));
        var sender = new ProbeSender(handler, Endpoint, Model, apiKey: null);

        await sender.SendAsync([new ProbeMessage("user", "hi")]);

        handler.Request!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_SendsTemperatureZero_StreamFalse_AndModel()
    {
        var handler = new CapturingHandler(Completion("hello"));
        var sender = new ProbeSender(handler, Endpoint, Model);

        await sender.SendAsync([new ProbeMessage("user", "hi")]);

        using var document = JsonDocument.Parse(handler.RequestBody!);
        document.RootElement.GetProperty("temperature").GetDouble().Should().Be(0.0);
        document.RootElement.GetProperty("stream").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("model").GetString().Should().Be(Model);
    }

    [Fact]
    public async Task SendAsync_Throws_OnNonSuccessStatus()
    {
        var handler = new CapturingHandler("unauthorized", HttpStatusCode.Unauthorized);
        var sender = new ProbeSender(handler, Endpoint, Model);

        var act = () => sender.SendAsync([new ProbeMessage("user", "hi")]);

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*401*");
    }
}
