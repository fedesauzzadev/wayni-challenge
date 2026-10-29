using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Bcra.Query.IntegrationTests;

[Collection("query-postgres")]
public sealed class QueryEndpointsTests(QueryApiFixture fixture)
{
    [Fact]
    public async Task WithoutActiveImport_QueryEndpoints_Return503ProblemDetails_AndReadyIsDegraded()
    {
        await fixture.ResetAsync();
        using var client = fixture.CreateClient();

        foreach (var path in new[] { "/totales", "/deudores?top=5", "/deudores/20123456784", "/entidades/266" })
        {
            using var response = await client.GetAsync(path);
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, $"sin import activo '{path}' responde 503");
            response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            problem.GetProperty("title").GetString().Should().Be("Query.NoActiveImport");
        }

        using var ready = await client.GetAsync("/health/ready");
        var body = await ready.Content.ReadAsStringAsync();
        body.Should().Contain("Degraded", "sin import activo el servicio está degradado");
    }

    [Fact]
    public async Task WithActiveImport_GetDeudorByCuit_ReturnsSummary_And404ForUnknown()
    {
        await fixture.ResetAsync();
        await fixture.SeedActiveImportAsync();
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync("/deudores/20-12345678-4");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var deudor = await response.Content.ReadFromJsonAsync<JsonElement>();
        deudor.GetProperty("cuit").GetString().Should().Be("20123456784");
        deudor.GetProperty("situacionMaxima").GetInt32().Should().Be(5);
        deudor.GetProperty("sumaTotalPrestamos").GetDecimal().Should().Be(1000.5m);
        deudor.GetProperty("nombre").GetString().Should().Be("PEREZ JUAN CARLOS");

        using var unknown = await client.GetAsync("/deudores/27123456780");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var invalid = await client.GetAsync("/deudores/not-a-cuit");
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WithActiveImport_GetTopDeudores_OrdersBySumaTotalDesc_AndCapsToTopMax()
    {
        await fixture.ResetAsync();
        await fixture.SeedActiveImportAsync();
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync("/deudores?top=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var deudores = await response.Content.ReadFromJsonAsync<JsonElement>();
        deudores.GetArrayLength().Should().Be(2, "TopMax=2 acota el top pedido");

        var sums = deudores.EnumerateArray()
            .Select(deudor => deudor.GetProperty("sumaTotalPrestamos").GetDecimal())
            .ToList();
        sums.Should().BeInDescendingOrder();
        sums.Should().BeEquivalentTo([1000.5m, 500.0m]);

        using var invalid = await client.GetAsync("/deudores?top=0");
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WithActiveImport_GetEntidad_ReturnsSummary_And404ForUnknown()
    {
        await fixture.ResetAsync();
        await fixture.SeedActiveImportAsync();
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync("/entidades/266");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var entidad = await response.Content.ReadFromJsonAsync<JsonElement>();
        entidad.GetProperty("codigo").GetInt32().Should().Be(266);
        entidad.GetProperty("nombre").GetString().Should().Be("BANCO DE LA NACION ARGENTINA");
        entidad.GetProperty("sumaTotalPrestamos").GetDecimal().Should().Be(900.5m);

        using var unknown = await client.GetAsync("/entidades/999");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WithActiveImport_GetTotales_ReturnsTotalsOfActiveImport()
    {
        await fixture.ResetAsync();
        await fixture.SeedActiveImportAsync();
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync("/totales");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var totales = await response.Content.ReadFromJsonAsync<JsonElement>();
        totales.GetProperty("cantidadDeudores").GetInt64().Should().Be(3);
        totales.GetProperty("cantidadEntidades").GetInt64().Should().Be(2);
        totales.GetProperty("sumaTotalPrestamos").GetDecimal().Should().Be(1750.7m);

        using var ready = await client.GetAsync("/health/ready");
        var body = await ready.Content.ReadAsStringAsync();
        body.Should().Contain("Healthy", "con import activo el servicio está healthy");
    }
}
