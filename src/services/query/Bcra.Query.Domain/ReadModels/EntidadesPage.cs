namespace Bcra.Query.Domain.ReadModels;

public sealed record EntidadesPage(long Total, IReadOnlyList<EntidadResumen> Items);
