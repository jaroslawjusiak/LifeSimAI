using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using LifeSim.Console.Commands;
using Xunit;

namespace LifeSim.Console.Tests;

public class LlmDoctorTests
{
    private const string Endpoint = "http://127.0.0.1:1337/v1";
    private const string Model = "llama-3.2-8b-instruct";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) =>
            _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            _responder(request, cancellationToken);
    }

    private static LlmDoctor DoctorResponding(HttpStatusCode status, string body) =>
        new(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        })));

    private static string ModelsJson(params string[] ids) =>
        $$"""{"object":"list","data":[{{string.Join(",", ids.Select(id => $$"""{"id":"{{id}}"}"""))}}]}""";

    [Fact]
    public async Task Check_ReportsReachableAndPresent_WhenModelListed()
    {
        var doctor = DoctorResponding(HttpStatusCode.OK, ModelsJson(Model, "other-model"));

        var result = await doctor.CheckAsync(Endpoint, Model);

        result.EndpointReachable.Should().BeTrue();
        result.ModelPresent.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Error.Should().BeNull();
        result.AvailableModels.Should().Contain(Model);
    }

    [Fact]
    public async Task Check_MatchesModelId_CaseInsensitively()
    {
        var doctor = DoctorResponding(HttpStatusCode.OK, ModelsJson("Llama-3.2-8B-Instruct"));

        var result = await doctor.CheckAsync(Endpoint, "llama-3.2-8b-instruct");

        result.ModelPresent.Should().BeTrue();
    }

    [Fact]
    public async Task Check_ReportsReachableButMissing_WhenModelAbsent()
    {
        var doctor = DoctorResponding(HttpStatusCode.OK, ModelsJson("some-other-model"));

        var result = await doctor.CheckAsync(Endpoint, Model);

        result.EndpointReachable.Should().BeTrue();
        result.ModelPresent.Should().BeFalse();
        result.AvailableModels.Should().Equal("some-other-model");
    }

    [Fact]
    public async Task Check_ReportsReachableButUnparseable_OnMalformedBody()
    {
        var doctor = DoctorResponding(HttpStatusCode.OK, "not-json-at-all");

        var result = await doctor.CheckAsync(Endpoint, Model);

        result.EndpointReachable.Should().BeTrue();
        result.ModelPresent.Should().BeFalse();
        result.Error.Should().Contain("unparseable");
    }

    [Fact]
    public async Task Check_ReportsReachableWithStatus_OnNonSuccessCode()
    {
        var doctor = DoctorResponding(HttpStatusCode.NotFound, "");

        var result = await doctor.CheckAsync(Endpoint, Model);

        result.EndpointReachable.Should().BeTrue();
        result.ModelPresent.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        result.Error.Should().Contain("404");
    }

    [Fact]
    public async Task Check_ReportsUnreachable_OnConnectionRefused()
    {
        var doctor = new LlmDoctor(new StubHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused"))));

        var result = await doctor.CheckAsync(Endpoint, Model);

        result.EndpointReachable.Should().BeFalse();
        result.ModelPresent.Should().BeFalse();
        result.StatusCode.Should().BeNull();
    }

    [Fact]
    public async Task Check_ReportsUnreachable_OnTimeout()
    {
        var doctor = new LlmDoctor(
            new StubHandler((_, ct) => Task.FromException<HttpResponseMessage>(
                new TaskCanceledException("timed out"))),
            TimeSpan.FromMilliseconds(50));

        var result = await doctor.CheckAsync(Endpoint, Model);

        result.EndpointReachable.Should().BeFalse();
        result.Error.Should().Contain("s");
    }

    [Fact]
    public async Task Check_ProbesModelsPath_UnderEndpoint()
    {
        Uri? probed = null;
        var doctor = new LlmDoctor(new StubHandler((request, _) =>
        {
            probed = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ModelsJson(Model), Encoding.UTF8, "application/json"),
            });
        }));

        await doctor.CheckAsync(Endpoint, Model);

        probed.Should().NotBeNull();
        probed!.AbsoluteUri.Should().Be($"{Endpoint}/models");
    }

    [Fact]
    public async Task Check_TrimsTrailingSlash_WhenBuildingModelsUri()
    {
        Uri? probed = null;
        var doctor = new LlmDoctor(new StubHandler((request, _) =>
        {
            probed = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ModelsJson(Model), Encoding.UTF8, "application/json"),
            });
        }));

        await doctor.CheckAsync($"{Endpoint}/", Model);

        probed!.AbsoluteUri.Should().Be($"{Endpoint}/models");
    }

    [Fact]
    public async Task Check_SendsAuthorizationHeader_WhenApiKeyProvided()
    {
        HttpRequestMessage? captured = null;
        var doctor = new LlmDoctor(new StubHandler((request, _) =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ModelsJson(Model), Encoding.UTF8, "application/json"),
            });
        }));

        await doctor.CheckAsync(Endpoint, Model, "secret-token");

        captured!.Headers.Authorization.Should().NotBeNull();
        captured.Headers.Authorization!.Scheme.Should().Be("Bearer");
        captured.Headers.Authorization.Parameter.Should().Be("secret-token");
    }

    [Fact]
    public async Task Check_OmitsAuthorizationHeader_WhenApiKeyNull()
    {
        HttpRequestMessage? captured = null;
        var doctor = new LlmDoctor(new StubHandler((request, _) =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ModelsJson(Model), Encoding.UTF8, "application/json"),
            });
        }));

        await doctor.CheckAsync(Endpoint, Model, apiKey: null);

        captured!.Headers.Authorization.Should().BeNull();
    }
}
