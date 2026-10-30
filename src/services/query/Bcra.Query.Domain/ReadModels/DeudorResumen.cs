using Bcra.Shared.Domain.ValueObjects;

namespace Bcra.Query.Domain.ReadModels;

public sealed record DeudorResumen(
    Cuit Cuit,
    int SituacionMaxima,
    decimal SumaTotalPrestamos,
    string? Nombre);
