using Bcra.Query.Domain.ReadModels;

namespace Bcra.Query.Application.Contracts;

public static class DeudorMappers
{
    public static DeudorResumenResponse ToResponse(this DeudorResumen deudor) =>
        new(deudor.Cuit.Value, deudor.SituacionMaxima, deudor.SumaTotalPrestamos, deudor.Nombre);
}
