namespace Bcra.Ingestion.Api.UploadImport;

public sealed record RequestImportRequest(string Source, string? Etag, long Size, int Periodo);
