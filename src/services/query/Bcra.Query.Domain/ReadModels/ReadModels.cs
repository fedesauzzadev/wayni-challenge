using Bcra.Shared.Domain.ValueObjects;

namespace Bcra.Query.Domain.ReadModels;

public sealed record DeudorResumen(
    Cuit Cuit,
    int SituacionMaxima,
    decimal SumaTotalPrestamos,
    string? Nombre);

public sealed record EntidadResumen(
    int Codigo,
    string? Nombre,
    decimal SumaTotalPrestamos);

public sealed record DeudorTotales(
    long CantidadDeudores,
    long CantidadEntidades,
    decimal SumaTotalPrestamos);
