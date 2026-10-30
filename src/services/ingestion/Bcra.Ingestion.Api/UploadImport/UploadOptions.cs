using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bcra.Ingestion.Api.UploadImport;

public sealed class UploadOptions
{
    public const string SectionName = "Upload";

    /// <summary>
    /// API key que debe enviar el cliente de upload en el header <c>X-Api-Key</c> de <c>POST /imports</c>.
    /// Vacía: solo Development permite subir sin key (DX local); otros entornos fallan el arranque.
    /// </summary>
    public string? ApiKey { get; set; }
}

/// <summary>
/// Validación de arranque (ValidateOnStart): fuera de Development una key vacía es un error de
/// configuración, no una decisión de abrir el endpoint.
/// </summary>
public sealed class UploadOptionsValidator(IHostEnvironment environment) : IValidateOptions<UploadOptions>
{
    public ValidateOptionsResult Validate(string? name, UploadOptions options) =>
        string.IsNullOrWhiteSpace(options.ApiKey) && !environment.IsDevelopment()
            ? ValidateOptionsResult.Fail(
                "Upload:ApiKey must be set outside Development; POST /imports would otherwise be unprotected.")
            : ValidateOptionsResult.Success;
}
