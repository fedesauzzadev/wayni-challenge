using Bcra.Notifier.Domain.Notifications;
using Bcra.Notifier.Application.Notifiers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bcra.Notifier.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddNotifierApplication(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
