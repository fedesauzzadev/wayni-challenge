using Bcra.Shared.Domain.Errors;

namespace Bcra.Query.Application;

public static class QueryErrors
{
    public static Error NoActiveImport { get; } = Error.Unavailable(
        "Query.NoActiveImport",
        "No active import is published yet; queries are unavailable until the first import completes.");
}
