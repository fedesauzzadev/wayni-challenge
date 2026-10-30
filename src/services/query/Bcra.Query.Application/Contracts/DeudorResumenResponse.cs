namespace Bcra.Query.Application.Contracts;

public sealed record DeudorResumenResponse(string Cuit, int SituacionMaxima, decimal SumaTotalPrestamos, string? Nombre);
