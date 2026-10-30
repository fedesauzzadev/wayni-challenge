using Dapper;

namespace Bcra.Shared.Infrastructure.Persistence;

/// <summary>
/// Configuración global de Dapper (mapeo snake_case → PascalCase). Idempotente: la invoca la
/// composición de cada servicio que usa Dapper; los repositorios no mutan estado global.
/// </summary>
public static class DapperDefaults
{
    private static int _configured;

    public static void Configure()
    {
        if (Interlocked.Exchange(ref _configured, 1) == 1)
            return;

        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }
}
