using Amazon.SQS;
using Amazon.SQS.Model;
using Bcra.Ingestion.Infrastructure.Sqs;
using Bcra.Shared.Domain.Contracts;
using Bcra.Shared.Infrastructure.Sqs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Bcra.Ingestion.InfrastructureTests.Sqs;

public sealed class SqsImportEventPublisherTests
{
    private static ImportCompletedEvent CreateEvent() => new(
        Guid.NewGuid(),
        "s3://bcra-local/deudores.txt",
        202311,
        10,
        1,
        4,
        2,
        99.9m,
        12.5,
        new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task PublishAsync_WithoutQueueUrl_DoesNotSend()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        var sut = new SqsImportEventPublisher(sqs, Options.Create(new SqsOptions()), NullLogger<SqsImportEventPublisher>.Instance);

        await sut.PublishAsync(CreateEvent(), CancellationToken.None);

        await sqs.DidNotReceiveWithAnyArgs().SendMessageAsync(default!, default);
    }

    [Fact]
    public async Task PublishAsync_WithQueueUrl_SendsSerializedEvent()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        var importCompleted = CreateEvent();
        var sut = new SqsImportEventPublisher(
            sqs,
            Options.Create(new SqsOptions { ImportCompletedQueueUrl = "http://localstack:4566/000000000000/import-completed" }),
            NullLogger<SqsImportEventPublisher>.Instance);

        await sut.PublishAsync(importCompleted, CancellationToken.None);

        await sqs.Received(1).SendMessageAsync(
            Arg.Is<SendMessageRequest>(request =>
                request.QueueUrl == "http://localstack:4566/000000000000/import-completed"
                && request.MessageBody.Contains($"\"jobId\":\"{importCompleted.JobId}\"")
                && request.MessageBody.Contains("\"linesProcessed\":10")
                && request.MessageBody.Contains("\"totalDeudores\":4")
                && request.MessageBody.Contains("\"sumaTotalPrestamos\":99.9")),
            Arg.Any<CancellationToken>());
    }
}
