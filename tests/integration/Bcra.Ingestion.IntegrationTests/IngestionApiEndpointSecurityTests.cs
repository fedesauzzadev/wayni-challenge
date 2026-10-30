using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bcra.Ingestion.Api.Hosting;
using Bcra.Ingestion.Api.UploadImport;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bcra.Ingestion.IntegrationTests;

[Collection("postgres")]
public sealed class IngestionApiEndpointSecurityTests(PostgresFixture fixture)
{
    private static readonly string UploadUrl = "/imports";

    private Dictionary<string, string?> Config(string? uploadApiKey = null) => new()
    {
        ["Database:ConnectionString"] = fixture.ConnectionString,
        ["Upload:ApiKey"] = uploadApiKey
    };

    private static JsonContent UploadBody(string etag) => JsonContent.Create(new
    {
        source = "/data/deudores.txt",
        etag,
        size = 1_000,
        periodo = 202311
    });

    private static async Task<HttpStatusCode> PostUploadAsync(HttpClient client, string etag, string? apiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, UploadUrl) { Content = UploadBody(etag) };
        if (apiKey is not null)
            request.Headers.Add(UploadApiKeyEndpointFilter.ApiKeyHeaderName, apiKey);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task PostImports_WithoutApiKey_Returns401ProblemDetails()
    {
        await fixture.ResetAsync();
        await using var factory = new IngestionApiFactory(Config("integration-upload-key"));
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(UploadUrl, UploadBody($"etag-{Guid.NewGuid():N}"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Upload.InvalidApiKey");
    }

    [Fact]
    public async Task PostImports_WithWrongApiKey_Returns401()
    {
        await fixture.ResetAsync();
        await using var factory = new IngestionApiFactory(Config("integration-upload-key"));
        using var client = factory.CreateClient();

        var status = await PostUploadAsync(client, $"etag-{Guid.NewGuid():N}", "wrong-key");

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostImports_WithCorrectApiKey_Returns201()
    {
        await fixture.ResetAsync();
        await using var factory = new IngestionApiFactory(Config("integration-upload-key"));
        using var client = factory.CreateClient();

        var status = await PostUploadAsync(client, $"etag-{Guid.NewGuid():N}", "integration-upload-key");

        status.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task PostImports_WithoutConfiguredKey_InDevelopment_IsAllowed()
    {
        await fixture.ResetAsync();
        await using var factory = new IngestionApiFactory(Config(uploadApiKey: ""));
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(UploadUrl, UploadBody($"etag-{Guid.NewGuid():N}"));

        response.StatusCode.Should().Be(HttpStatusCode.Created, "con Upload:ApiKey vacía Development permite subir sin key (DX local)");
    }

    [Fact]
    public async Task GetImports_ExceedingWindowLimit_Returns429()
    {
        await fixture.ResetAsync();
        await using var factory = new IngestionApiFactory(Config());
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        foreach (var _ in Enumerable.Range(0, 101))
            statuses.Add((await client.GetAsync($"/imports/{Guid.NewGuid()}")).StatusCode);

        statuses.Count(s => s == HttpStatusCode.NotFound).Should().Be(100, "la ventana default permite 100 requests");
        statuses.Count(s => s == HttpStatusCode.TooManyRequests).Should().Be(1, "el pedido 101 de la ventana se rechaza con 429");
    }

    [Fact]
    public async Task PostImports_ExceedingWindowLimit_Returns429()
    {
        await fixture.ResetAsync();
        await using var factory = new IngestionApiFactory(Config("integration-upload-key"));
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        foreach (var _ in Enumerable.Range(0, 101))
            statuses.Add(await PostUploadAsync(client, "etag-rate-limit", "integration-upload-key"));

        statuses.Count(s => s is HttpStatusCode.Created or HttpStatusCode.OK).Should().Be(100);
        statuses.Count(s => s == HttpStatusCode.TooManyRequests).Should().Be(1);
    }
}

public sealed class UploadOptionsValidatorTests
{
    [Theory]
    [InlineData(null, "Production", false)]
    [InlineData("", "Production", false)]
    [InlineData("  ", "Staging", false)]
    [InlineData(null, "Development", true)]
    [InlineData("some-key", "Production", true)]
    public void EmptyKey_OutsideDevelopment_FailsValidation(string? apiKey, string environment, bool shouldSucceed)
    {
        var validator = new UploadOptionsValidator(new StubHostEnvironment(environment));

        var result = validator.Validate(null, new UploadOptions { ApiKey = apiKey });

        result.Succeeded.Should().Be(shouldSucceed);
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "test";
        public string EnvironmentName { get; set; } = environmentName;
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}

public sealed class IngestionApiFactory(Dictionary<string, string?> settings) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        builder.ConfigureServices(services =>
        {
            var hostedServicesToRemove = services
                .Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType is { } implementation
                    && (implementation == typeof(MigrationHostedService) || implementation == typeof(ImportWorkerHostedService)))
                .ToList();
            foreach (var descriptor in hostedServicesToRemove)
                services.Remove(descriptor);
        });
    }
}
