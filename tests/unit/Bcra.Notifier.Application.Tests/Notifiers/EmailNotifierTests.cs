using Bcra.Notifier.Application.Notifiers;
using Bcra.Notifier.Application.Ports;
using Bcra.Notifier.Domain.Notifications;
using Bcra.Notifier.Infrastructure;
using Bcra.Notifier.Infrastructure.Notifiers;
using Bcra.Notifier.Infrastructure.Persistence;
using Bcra.Shared.Domain.Contracts;
using Bcra.Shared.Domain.Errors;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bcra.Notifier.Application.Tests.Notifiers;

public sealed class EmailNotifierTests
{
    private static ImportCompletedEvent CreateEvent() => new(
        Guid.NewGuid(),
        "s3://bcra-local/deudores.txt",
        202311,
        10,
        0,
        285,
        12.5,
        new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc));

    private static EmailNotifier CreateNotifier(
        StubSmtpTransport transport,
        StubRecipientStore recipientStore,
        string to = "dest@example.com") =>
        new(
            transport,
            recipientStore,
            Options.Create(new EmailOptions { To = to, From = "bcra-notifier@localhost" }),
            Options.Create(new NotificationsOptions { TimeoutSeconds = 5 }),
            NullLogger<EmailNotifier>.Instance);

    [Fact]
    public async Task RecipientsInTable_SendsSingleEmailToAllRegistered()
    {
        var transport = new StubSmtpTransport();
        var store = new StubRecipientStore(["ops@example.com", "fede.sauzza.dev@gmail.com"]);
        var sut = CreateNotifier(transport, store);

        var result = await sut.NotifyAsync(CreateEvent(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Delivered.Should().BeTrue();

        transport.Sent.Should().ContainSingle();
        var message = transport.Sent[0];
        message.To.Should().Equal(["ops@example.com", "fede.sauzza.dev@gmail.com"], "un solo email con todos los destinatarios de la tabla");
        message.From.Should().Be("bcra-notifier@localhost");
        message.Subject.Should().Contain("202311");
        message.Body.Should()
            .Contain("Lineas procesadas: 10")
            .And.Contain("Lineas rechazadas: 0")
            .And.Contain("Registros de control descartados (marcador 98/99): 285")
            .And.Contain("Duracion: 12.5 ms");
    }

    [Fact]
    public async Task EmptyTable_FallsBackToConfiguredRecipient()
    {
        var transport = new StubSmtpTransport();
        var store = new StubRecipientStore([]);
        var sut = CreateNotifier(transport, store, to: "fede.sauzza.dev@gmail.com");

        var result = await sut.NotifyAsync(CreateEvent(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        transport.Sent.Should().ContainSingle();
        transport.Sent[0].To.Should().Equal(["fede.sauzza.dev@gmail.com"], "tabla vacía → fallback a Notifications:Email__To");
    }

    [Fact]
    public async Task EmptyTableAndBlankConfig_SkipsWithoutFailing()
    {
        var transport = new StubSmtpTransport();
        var store = new StubRecipientStore([]);
        var sut = CreateNotifier(transport, store, to: "");

        var result = await sut.NotifyAsync(CreateEvent(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Delivered.Should().BeTrue("sin destinatarios no hay nada que reintentar");
        transport.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task SmtpFailure_ReturnsUnavailableFailure()
    {
        var transport = new StubSmtpTransport(throwOnSend: true);
        var sut = CreateNotifier(transport, new StubRecipientStore(["dest@example.com"]));

        var result = await sut.NotifyAsync(CreateEvent(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unavailable);
        result.Error.Code.Should().Be("Email.Failed");
    }

    [Fact]
    public void AddNotifierInfrastructure_RegistersEmailNotifierAndRecipientStore()
    {
        using var provider = BuildProvider();

        provider.GetServices<INotifier>().Should().Contain(n => n.Strategy == "email", "los destinatarios pueden aparecer por tabla en runtime");
        provider.GetRequiredService<INotificationRecipientStore>().Should().BeOfType<DapperNotificationRecipientStore>();
    }

    private static ServiceProvider BuildProvider(params (string Key, string? Value)[] settings)
    {
        var defaults = new List<(string Key, string? Value)>
        {
            ("Database:ConnectionString", "Host=localhost;Database=bcra"),
            ("Sqs:ImportCompletedQueueUrl", "http://localhost:4566/000000000000/import-completed")
        };
        defaults.AddRange(settings);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(defaults.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddNotifierInfrastructure(configuration)
            .BuildServiceProvider();
    }

    private sealed class StubSmtpTransport(bool throwOnSend = false) : ISmtpTransport
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            if (throwOnSend)
            {
                throw new InvalidOperationException("connection refused");
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class StubRecipientStore(IReadOnlyList<string> emails) : INotificationRecipientStore
    {
        public Task<IReadOnlyList<string>> GetRecipientEmailsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(emails);
    }
}
