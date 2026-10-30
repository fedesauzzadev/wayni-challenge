namespace Bcra.Notifier.Application.Ports;

public sealed record EmailMessage(string From, IReadOnlyList<string> To, string Subject, string Body)
{
    public string ToDisplay => string.Join(", ", To);
}
