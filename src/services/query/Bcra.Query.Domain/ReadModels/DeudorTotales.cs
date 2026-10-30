namespace Bcra.Query.Domain.ReadModels;

public sealed record DeudorTotales(
    long CantidadDeudores,
    long CantidadEntidades,
    decimal SumaTotalPrestamos);
