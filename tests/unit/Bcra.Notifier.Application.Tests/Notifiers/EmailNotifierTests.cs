using Bcra.Notifier.Application.Notifiers;
using Bcra.Notifier.Application.Ports;
using Bcra.Notifier.Domain.Notifications;
using Bcra.Notifier.Infrastructure;
using Bcra.Notifier.Infrastructure.Notifiers;
using Bcra.Shared.Domain.Contracts;
using Bcra.Shared.Domain.Errors;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        4,
        2,
        99.9m,
        12.5,
        new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc));

    private static EmailNotifier CreateNotifier(StubSmtpTransport transport, string to = "dest@example.com") =>
        new(
            transport,
            Options.Create(new EmailOptions { To = to, From = "bcra-notifier@localhost" }),
            Options.Create(new NotificationsOptions { TimeoutSeconds = 5 }),
            NullLogger<EmailNotifier>.Instance);

    [Fact]
    public async Task SendSuccess_SendsEmailToConfiguredRecipientWithAllMetrics_AndReturnsDelivered()
    {
        var transport = new StubSmtpTransport();
        var importCompleted = CreateEvent();
        var sut = CreateNotifier(transport, "fede.sauzza.dev@gmail.com");

        var result = await sut.NotifyAsync(importCompleted, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Delivered.Should().BeTrue();

        transport.Sent.Should().ContainSingle();
        var message = transport.Sent[0];
        message.To.Should().Be("fede.sauzza.dev@gmail.com");
        message.From.Should().Be("bcra-notifier@localhost");
        message.Subject.Should().Contain(importCompleted.JobId.ToString()).And.Contain("202311");
        message.Body.Should()
            .Contain(importCompleted.Source)
            .And.Contain("Lineas procesadas: 10")
            .And.Contain("Lineas rechazadas: 0")
            .And.Contain("Total deudores: 4")
            .And.Contain("Total entidades: 2")
            .And.Contain("Suma total prestamos: 99.9")
            .And.Contain("Duracion: 12.5 ms");
    }

    [Fact]
    public async Task SmtpFailure_ReturnsUnavailableFailure()
    {
        var transport = new StubSmtpTransport(throwOnSend: true);
        var sut = CreateNotifier(transport);

        var result = await sut.NotifyAsync(CreateEvent(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unavailable);
        result.Error.Code.Should().Be("Email.Failed");
    }

    [Fact]
    public void AddNotifierInfrastructure_EmptyTo_DoesNotRegisterEmailNotifier()
    {
        using var provider = BuildProvider();

        provider.GetServices<INotifier>().Should().NotContain(n => n.Strategy == "email");
    }

    [Fact]
    public void AddNotifierInfrastructure_ConfiguredTo_RegistersEmailNotifier()
    {
        using var provider = BuildProvider(("Notifications:Email:To", "fede.sauzza.dev@gmail.com"));

        provider.GetServices<INotifier>().Should().Contain(n => n.Strategy == "email");
    }

    private static ServiceProvider BuildProvider(params (string Key, string? Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
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
}
