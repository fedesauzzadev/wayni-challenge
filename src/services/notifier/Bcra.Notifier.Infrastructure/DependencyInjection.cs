using Bcra.Notifier.Application.Notifiers;
using Bcra.Notifier.Infrastructure.Notifiers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bcra.Notifier.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNotifierInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<INotifier, StructuredLogNotifier>();
        return services;
    }
}
