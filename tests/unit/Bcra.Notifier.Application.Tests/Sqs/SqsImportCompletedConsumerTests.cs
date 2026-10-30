using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Bcra.Notifier.Application.Notifications;
using Bcra.Notifier.Application.Notifiers;
using Bcra.Notifier.Domain.Notifications;
using Bcra.Notifier.Infrastructure.Sqs;
using Bcra.Shared.Domain.Contracts;
using Bcra.Shared.Domain.Results;
using Bcra.Shared.Infrastructure.Sqs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Bcra.Notifier.Application.Tests.Sqs;

public sealed class SqsImportCompletedConsumerTests
{
    [Fact]
    public async Task UnexpectedFailureInOneMessage_DoesNotBlockTheRestOfTheBatch()
    {
        var failingJobId = Guid.NewGuid();
        var healthyJobId = Guid.NewGuid();
        var notifier = Substitute.For<INotifier>();
        notifier.Strategy.Returns("test");
        notifier
            .NotifyAsync(Arg.Any<ImportCompletedEvent>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<ImportCompletedEvent>().JobId == failingJobId
                ? throw new InvalidOperationException("unexpected notifier failure")
                : Task.FromResult(Result.Ok(new NotificationReceipt("test", true))));

        var failingMessage = MakeMessage(Serialize(failingJobId));
        var healthyMessage = MakeMessage(Serialize(healthyJobId));

        var sqs = Substitute.For<IAmazonSQS>();
        var firstPoll = true;
        sqs.ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                List<Message> messages = firstPoll ? [failingMessage, healthyMessage] : [];
                firstPoll = false;
                return Task.FromResult(new ReceiveMessageResponse { Messages = messages });
            });

        var healthyBatchDeleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sqs.DeleteMessageBatchAsync(Arg.Any<DeleteMessageBatchRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var request = callInfo.Arg<DeleteMessageBatchRequest>();
                if (request.Entries.Any(entry => entry.ReceiptHandle == healthyMessage.ReceiptHandle))
                    healthyBatchDeleted.TrySetResult();
                return Task.FromResult(new DeleteMessageBatchResponse
                {
                    Successful = request.Entries
                        .Select(entry => new DeleteMessageBatchResultEntry { Id = entry.Id })
                        .ToList()
                });
            });

        var consumer = new SqsImportCompletedConsumer(
            sqs,
            Options.Create(new SqsOptions
            {
                ImportCompletedQueueUrl = "http://localhost/queues/import-completed",
                MaxMessages = 10,
                WaitTimeSeconds = 0,
                PollErrorDelaySeconds = 1
            }),
            new ImportCompletedMessageHandler([notifier], NullLogger<ImportCompletedMessageHandler>.Instance),
            NullLogger<SqsImportCompletedConsumer>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await consumer.StartAsync(cts.Token);
        var finished = await Task.WhenAny(healthyBatchDeleted.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        await consumer.StopAsync(CancellationToken.None);

        finished.Should().BeSameAs(healthyBatchDeleted.Task,
            "una excepción no anticipada en un mensaje no debe retrasar el resto del lote");
        await sqs.Received(1).DeleteMessageBatchAsync(
            Arg.Is<DeleteMessageBatchRequest>(request =>
                request.Entries.Select(entry => entry.ReceiptHandle).SequenceEqual(new[] { healthyMessage.ReceiptHandle })),
            Arg.Any<CancellationToken>());
        await sqs.DidNotReceive().DeleteMessageBatchAsync(
            Arg.Is<DeleteMessageBatchRequest>(request =>
                request.Entries.Any(entry => entry.ReceiptHandle == failingMessage.ReceiptHandle)),
            Arg.Any<CancellationToken>());
    }

    private static string Serialize(Guid jobId) =>
        JsonSerializer.Serialize(
            new ImportCompletedEvent(
                jobId,
                "/data/deudores.txt",
                202311,
                10,
                0,
                285,
                12.5,
                new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc)),
            ImportCompletedEventJson.Options);

    private static Message MakeMessage(string body) => new()
    {
        MessageId = Guid.NewGuid().ToString(),
        ReceiptHandle = Guid.NewGuid().ToString("N"),
        Body = body
    };
}
