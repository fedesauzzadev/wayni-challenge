using Bcra.Notifier.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Bcra.Notifier.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddNotifierApplication(this IServiceCollection services)
    {
        services.AddSingleton<ImportCompletedMessageHandler>();
        return services;
    }
}
