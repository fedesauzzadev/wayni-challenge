using System.Text.Json;
using Bcra.Notifier.Application.Notifications;
using Bcra.Notifier.Application.Notifiers;
using Bcra.Notifier.Domain.Notifications;
using Bcra.Shared.Domain.Contracts;
using Bcra.Shared.Domain.Errors;
using Bcra.Shared.Domain.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Bcra.Notifier.Application.Tests.Notifications;

public sealed class ImportCompletedMessageHandlerTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private static ImportCompletedEvent CreateEvent() => new(
        Guid.NewGuid(),
        "/data/deudores.txt",
        202311,
        1_000,
        3,
        25,
        5,
        123_456.7m,
        4_321.5,
        new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc));

    private static ImportCompletedMessageHandler CreateHandler(params INotifier[] notifiers) =>
        new(notifiers, NullLogger<ImportCompletedMessageHandler>.Instance);

    private static INotifier OkNotifier(string strategy)
    {
        var notifier = Substitute.For<INotifier>();
        notifier.Strategy.Returns(strategy);
        notifier
            .NotifyAsync(Arg.Any<ImportCompletedEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new NotificationReceipt(strategy, true))));
        return notifier;
    }

    [Fact]
    public async Task ValidMessage_NotifiesEveryNotifier_AndReturnsDelete()
    {
        var first = OkNotifier("structured-log");
        var second = OkNotifier("webhook");
        var sut = CreateHandler(first, second);

        var outcome = await sut.HandleAsync(JsonSerializer.Serialize(CreateEvent(), WebOptions), CancellationToken.None);

        outcome.Should().Be(MessageProcessingOutcome.Delete);
        await first.Received(1).NotifyAsync(Arg.Any<ImportCompletedEvent>(), Arg.Any<CancellationToken>());
        await second.Received(1).NotifyAsync(Arg.Any<ImportCompletedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidMessage_PassesDeserializedEventToNotifiers()
    {
        var importCompleted = CreateEvent();
        var notifier = OkNotifier("structured-log");
        var sut = CreateHandler(notifier);

        await sut.HandleAsync(JsonSerializer.Serialize(importCompleted, WebOptions), CancellationToken.None);

        await notifier.Received(1).NotifyAsync(
            Arg.Is<ImportCompletedEvent>(e =>
                e.JobId == importCompleted.JobId
                && e.Source == importCompleted.Source
                && e.Periodo == importCompleted.Periodo
                && e.LinesProcessed == importCompleted.LinesProcessed
                && e.LinesRejected == importCompleted.LinesRejected
                && e.TotalDeudores == importCompleted.TotalDeudores
                && e.TotalEntidades == importCompleted.TotalEntidades
                && e.SumaTotalPrestamos == importCompleted.SumaTotalPrestamos
                && e.DurationMs == importCompleted.DurationMs
                && e.CompletedAtUtc == importCompleted.CompletedAtUtc),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifierReturnsFailedResult_ReturnsAbandon()
    {
        var notifier = Substitute.For<INotifier>();
        notifier.Strategy.Returns("webhook");
        notifier
            .NotifyAsync(Arg.Any<ImportCompletedEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<NotificationReceipt>(Error.Unavailable("Webhook.Down", "boom"))));
        var sut = CreateHandler(notifier);

        var outcome = await sut.HandleAsync(JsonSerializer.Serialize(CreateEvent(), WebOptions), CancellationToken.None);

        outcome.Should().Be(MessageProcessingOutcome.Abandon);
    }

    [Fact]
    public async Task NotifierReturnsUndeliveredReceipt_ReturnsAbandon()
    {
        var notifier = Substitute.For<INotifier>();
        notifier.Strategy.Returns("webhook");
        notifier
            .NotifyAsync(Arg.Any<ImportCompletedEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new NotificationReceipt("webhook", false, "endpoint refused"))));
        var sut = CreateHandler(notifier);

        var outcome = await sut.HandleAsync(JsonSerializer.Serialize(CreateEvent(), WebOptions), CancellationToken.None);

        outcome.Should().Be(MessageProcessingOutcome.Abandon);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"JobId\": \"not-a-guid\"}")]
    [InlineData("null")]
    public async Task MalformedMessage_IsDiscardedWithoutNotifying(string body)
    {
        var notifier = OkNotifier("structured-log");
        var sut = CreateHandler(notifier);

        var outcome = await sut.HandleAsync(body, CancellationToken.None);

        outcome.Should().Be(MessageProcessingOutcome.Delete);
        await notifier.DidNotReceive().NotifyAsync(Arg.Any<ImportCompletedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EventWithEmptyJobId_IsDiscardedAsMalformed()
    {
        var notifier = OkNotifier("structured-log");
        var sut = CreateHandler(notifier);

        var outcome = await sut.HandleAsync(
            "{\"jobId\":\"00000000-0000-0000-0000-000000000000\",\"source\":\"/data/deudores.txt\"}",
            CancellationToken.None);

        outcome.Should().Be(MessageProcessingOutcome.Delete);
        await notifier.DidNotReceive().NotifyAsync(Arg.Any<ImportCompletedEvent>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("{\"jobId\":\"%JOBID%\",\"source\":\"\"}")]
    [InlineData("{\"jobId\":\"%JOBID%\"}")]
    [InlineData("{\"jobId\":\"%JOBID%\",\"source\":\"   \"}")]
    public async Task EventWithEmptySource_IsDiscardedAsMalformed(string template)
    {
        var notifier = OkNotifier("structured-log");
        var sut = CreateHandler(notifier);

        var outcome = await sut.HandleAsync(template.Replace("%JOBID%", Guid.NewGuid().ToString()), CancellationToken.None);

        outcome.Should().Be(MessageProcessingOutcome.Delete);
        await notifier.DidNotReceive().NotifyAsync(Arg.Any<ImportCompletedEvent>(), Arg.Any<CancellationToken>());
    }
}
