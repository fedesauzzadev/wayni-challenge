namespace Bcra.Notifier.Application.Ports;

public sealed record EmailMessage(string From, string To, string Subject, string Body);
