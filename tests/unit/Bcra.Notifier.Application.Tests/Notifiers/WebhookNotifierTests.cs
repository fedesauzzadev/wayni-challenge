using Bcra.Notifier.Domain.Notifications;
using Bcra.Notifier.Infrastructure.Notifiers;
using Bcra.Shared.Domain.Contracts;
using Bcra.Shared.Domain.Errors;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Bcra.Notifier.Application.Tests.Notifiers;

public sealed class WebhookNotifierTests
{
    private static ImportCompletedEvent CreateEvent() => new(
        Guid.NewGuid(),
        "s3://bcra-local/deudores.txt",
        202311,
        10,
        0,
        12.5,
        new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc));

    private static WebhookNotifier CreateNotifier(StubHttpMessageHandler handler, string webhookUrl) =>
        new(
            CreateFactory(handler),
            Options.Create(new NotificationsOptions { WebhookUrl = webhookUrl, TimeoutSeconds = 5 }));

    private static IHttpClientFactory CreateFactory(StubHttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(handler));
        return factory;
    }

    [Fact]
    public async Task SuccessStatusCode_PostsEventJson_AndReturnsDelivered()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        var sut = CreateNotifier(handler, "http://hooks.example/imports");

        var result = await sut.NotifyAsync(CreateEvent(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Delivered.Should().BeTrue();

        handler.Requests.Should().ContainSingle();
        var request = handler.Requests[0];
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.ToString().Should().Be("http://hooks.example/imports");
        request.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
        handler.Bodies[0].Should().Contain("\"source\":\"s3://bcra-local/deudores.txt\"");
    }

    [Fact]
    public async Task SuccessStatusCode_SendsIdempotencyKeyHeaderWithJobId()
    {
        var importCompleted = CreateEvent();
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        var sut = CreateNotifier(handler, "http://hooks.example/imports");

        await sut.NotifyAsync(importCompleted, CancellationToken.None);

        var request = handler.Requests.Single();
        request.Headers.TryGetValues(WebhookNotifier.IdempotencyKeyHeader, out var values).Should().BeTrue();
        values.Should().ContainSingle().Which.Should().Be(
            importCompleted.JobId.ToString(),
            "el receptor deduplica re-entregas at-least-once por JobId (ADR-010)");
    }

    [Fact]
    public async Task NonSuccessStatusCode_ReturnsUndelivered()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        var sut = CreateNotifier(handler, "http://hooks.example/imports");

        var result = await sut.NotifyAsync(CreateEvent(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unavailable);
    }

    [Fact]
    public async Task HttpFailure_ReturnsUndelivered()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var sut = CreateNotifier(handler, "http://hooks.example/imports");

        var result = await sut.NotifyAsync(CreateEvent(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Webhook.Failed");
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return responder(request);
        }
    }
}
