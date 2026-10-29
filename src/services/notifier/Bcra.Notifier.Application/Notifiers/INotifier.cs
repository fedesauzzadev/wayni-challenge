using Bcra.Notifier.Domain.Notifications;
using Bcra.Shared.Domain.Contracts;
using Bcra.Shared.Domain.Results;

namespace Bcra.Notifier.Application.Notifiers;

public interface INotifier
{
    string Strategy { get; }

    Task<Result<NotificationReceipt>> NotifyAsync(ImportCompletedEvent importCompleted, CancellationToken cancellationToken);
}
