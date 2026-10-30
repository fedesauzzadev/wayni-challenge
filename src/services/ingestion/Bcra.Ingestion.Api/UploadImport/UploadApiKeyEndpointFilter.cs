using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Bcra.Ingestion.Api.UploadImport;

/// <summary>
/// Exige en <c>POST /imports</c> un header <c>X-Api-Key</c> coincidente con <c>Upload:ApiKey</c>;
/// responde 401 con ProblemDetails si falta o no coincide. Con la key vacía no aplica (solo
/// alcanzable en Development: <see cref="UploadOptionsValidator"/> impide el arranque en el resto).
/// </summary>
public sealed class UploadApiKeyEndpointFilter(IOptions<UploadOptions> options) : IEndpointFilter
{
    public const string ApiKeyHeaderName = "X-Api-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = options.Value.ApiKey;
        if (!string.IsNullOrEmpty(expected))
        {
            context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var provided);
            if (!KeyMatches(provided.ToString(), expected))
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Upload.InvalidApiKey",
                    detail: $"Missing or invalid {ApiKeyHeaderName} header.",
                    type: "https://bcra.errors/Upload.InvalidApiKey");
        }

        return await next(context);
    }

    /// <summary>Comparación en tiempo constante para no filtrar la key por timing.</summary>
    private static bool KeyMatches(string? provided, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided ?? string.Empty),
            Encoding.UTF8.GetBytes(expected));
}
