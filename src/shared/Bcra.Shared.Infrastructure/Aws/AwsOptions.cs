using System.ComponentModel.DataAnnotations;

namespace Bcra.Shared.Infrastructure.Aws;

public sealed class AwsOptions
{
    public const string SectionName = "AWS";

    /// <summary>Vacío = endpoint por defecto de AWS (solo LocalStack/prod-local usa endpoint propio).</summary>
    public string EndpointUrl { get; set; } = string.Empty;

    [Required]
    public string Region { get; set; } = "us-east-1";

    /// <summary>Vacíos = resolución de credenciales del entorno/rol IAM.</summary>
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
}
