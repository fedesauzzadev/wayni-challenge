using System.Text.Json;

namespace Bcra.Shared.Domain.Contracts;

public static class ImportCompletedEventJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
