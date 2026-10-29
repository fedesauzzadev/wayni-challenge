using Bcra.Notifier.Application.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bcra.Notifier.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddNotifierApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ImportCompletedMessageHandler>();
        return services;
    }
}
