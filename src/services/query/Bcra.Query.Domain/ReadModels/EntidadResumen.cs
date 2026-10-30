namespace Bcra.Query.Domain.ReadModels;

public sealed record EntidadResumen(
    int Codigo,
    string? Nombre,
    decimal SumaTotalPrestamos);
