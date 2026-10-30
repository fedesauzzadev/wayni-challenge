namespace Bcra.Query.Domain.ReadModels;

public sealed record DeudoresPage(long Total, IReadOnlyList<DeudorResumen> Items);
