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

        foreach (var path in new[] { "/totales", "/deudores?top=5", "/deudores/20123456784", "/entidades/266", "/entidades", "/entidades?codigo=266" })
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
    public async Task WithActiveImport_GetTopDeudores_ReturnsPageEnvelope_AndCapsLimitToTopMax()
    {
        await fixture.ResetAsync();
        await fixture.SeedActiveImportAsync();
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync("/deudores?limit=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        page.GetProperty("total").GetInt64().Should().Be(4, "total es la cantidad de deudores del import");
        page.GetProperty("offset").GetInt32().Should().Be(0, "offset default es 0");
        page.GetProperty("limit").GetInt32().Should().Be(2, "TopMax=2 acota el limit pedido");

        var deudores = page.GetProperty("items");
        deudores.GetArrayLength().Should().Be(2, "TopMax=2 acota el limit pedido");

        var sums = deudores.EnumerateArray()
            .Select(deudor => deudor.GetProperty("sumaTotalPrestamos").GetDecimal())
            .ToList();
        sums.Should().BeInDescendingOrder();
        sums.Should().BeEquivalentTo([1000.5m, 500.0m]);

        deudores.EnumerateArray()
            .Last()
            .GetProperty("nombre").ValueKind.Should().Be(JsonValueKind.Null, "por defecto incluye deudores sin nombre");
    }

    [Fact]
    public async Task WithActiveImport_GetTopDeudores_PaginatesWithOffsetAndLimit_WithoutDuplicatesOrGaps()
    {
        await fixture.ResetAsync();
        await fixture.SeedActiveImportAsync();
        using var client = fixture.CreateClient();

        using var first = await client.GetAsync("/deudores?offset=0&limit=2");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var pageOne = await first.Content.ReadFromJsonAsync<JsonElement>();
        pageOne.GetProperty("total").GetInt64().Should().Be(4);
        pageOne.GetProperty("offset").GetInt32().Should().Be(0);
        pageOne.GetProperty("limit").GetInt32().Should().Be(2);
        pageOne.GetProperty("items").EnumerateArray()
            .Select(deudor => deudor.GetProperty("cuit").GetString())
            .Should().Equal(["20123456784", "20987654321"], "página 1 son los dos mayores por suma_total DESC");

        using var second = await client.GetAsync("/deudores?offset=2&limit=2");
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var pageTwo = await second.Content.ReadFromJsonAsync<JsonElement>();
        pageTwo.GetProperty("total").GetInt64().Should().Be(4);
        pageTwo.GetProperty("offset").GetInt32().Should().Be(2);
        pageTwo.GetProperty("limit").GetInt32().Should().Be(2);
        pageTwo.GetProperty("items").EnumerateArray()
            .Select(deudor => deudor.GetProperty("cuit").GetString())
            .Should().Equal(["20000000009", "27000000008"], "página 2 continúa donde termina la página 1");

        using var beyond = await client.GetAsync("/deudores?offset=99&limit=2");
        beyond.StatusCode.Should().Be(HttpStatusCode.OK, "offset mayor que total responde 200");
        var empty = await beyond.Content.ReadFromJsonAsync<JsonElement>();
        empty.GetProperty("total").GetInt64().Should().Be(4);
        empty.GetProperty("offset").GetInt32().Should().Be(99);
        empty.GetProperty("items").GetArrayLength().Should().Be(0, "offset fuera de rango devuelve items vacío");
    }

    [Fact]
    public async Task WithActiveImport_GetTopDeudores_TopAliasFallsBackToLimit_AndValidatesParams()
    {
        await fixture.ResetAsync();
        await fixture.SeedActiveImportAsync();
        using var client = fixture.CreateClient();

        using var aliased = await client.GetAsync("/deudores?top=1");
        aliased.StatusCode.Should().Be(HttpStatusCode.OK);
        var topPage = await aliased.Content.ReadFromJsonAsync<JsonElement>();
        topPage.GetProperty("limit").GetInt32().Should().Be(1, "top actúa como alias de limit");
        topPage.GetProperty("items").GetArrayLength().Should().Be(1);
        topPage.GetProperty("items")[0].GetProperty("cuit").GetString().Should().Be("20123456784");

        using var both = await client.GetAsync("/deudores?limit=1&top=2");
        both.StatusCode.Should().Be(HttpStatusCode.OK);
        var bothPage = await both.Content.ReadFromJsonAsync<JsonElement>();
        bothPage.GetProperty("limit").GetInt32().Should().Be(1, "si viene limit se usa limit y top se ignora");

        await AssertProblemAsync(client, "/deudores?top=0", "Query.InvalidTop");
        await AssertProblemAsync(client, "/deudores?limit=0", "Query.InvalidLimit");
        await AssertProblemAsync(client, "/deudores?offset=-1", "Query.InvalidOffset");
    }

    [Fact]
    public async Task WithActiveImport_GetTopDeudores_OmitirSinNombre_ExcludesUnnamedDeudores_AndAdjustsTotal()
    {
        await fixture.ResetAsync();
        await fixture.SeedActiveImportAsync();
        using var client = fixture.CreateClient();

        using var omitted = await client.GetAsync("/deudores?omitirSinNombre=true");
        omitted.StatusCode.Should().Be(HttpStatusCode.OK);

        var filtered = await omitted.Content.ReadFromJsonAsync<JsonElement>();
        filtered.GetProperty("total").GetInt64().Should().Be(1, "total respeta el filtro omitirSinNombre");
        filtered.GetProperty("items").GetArrayLength().Should().Be(1, "solo el deudor con nombre queda dentro del TopMax");
        filtered.GetProperty("items")[0].GetProperty("cuit").GetString().Should().Be("20123456784");
        filtered.GetProperty("items")[0].GetProperty("nombre").GetString().Should().Be("PEREZ JUAN CARLOS");

        using var included = await client.GetAsync("/deudores?omitirSinNombre=false");
        included.StatusCode.Should().Be(HttpStatusCode.OK);

        var all = await included.Content.ReadFromJsonAsync<JsonElement>();
        all.GetProperty("total").GetInt64().Should().Be(4, "sin filtro el total cuenta todos los deudores");
        all.GetProperty("items").GetArrayLength().Should().Be(2, "TopMax=2 acota el limit pedido");
        all.GetProperty("items").EnumerateArray()
            .Last()
            .GetProperty("nombre").ValueKind.Should().Be(JsonValueKind.Null, "omitirSinNombre=false se comporta como el default");

        using var invalid = await client.GetAsync("/deudores?omitirSinNombre=xyz");
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WithActiveImport_GetTopDeudores_LiteralTopPath_ReturnsTopN_AndValidatesN()
    {
        await fixture.ResetAsync();
        await fixture.SeedActiveImportAsync();
        using var client = fixture.CreateClient();

        using var top1 = await client.GetAsync("/deudores/top/1");
        top1.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await top1.Content.ReadFromJsonAsync<JsonElement>();
        page.GetProperty("offset").GetInt32().Should().Be(0, "el alias top/{n} pagina desde 0");
        page.GetProperty("limit").GetInt32().Should().Be(1, "n actúa como limit");
        page.GetProperty("items").GetArrayLength().Should().Be(1);
        page.GetProperty("items")[0].GetProperty("cuit").GetString().Should().Be("20123456784", "el deudor con mayor suma_total");

        using var clamped = await client.GetAsync("/deudores/top/99");
        clamped.StatusCode.Should().Be(HttpStatusCode.OK);
        var clampedPage = await clamped.Content.ReadFromJsonAsync<JsonElement>();
        clampedPage.GetProperty("limit").GetInt32().Should().Be(2, "n mayor que TopMax se acota igual que limit/top");

        await AssertProblemAsync(client, "/deudores/top/0", "Query.InvalidTop");
        await AssertProblemAsync(client, "/deudores/top/-3", "Query.InvalidTop");

        using var notInt = await client.GetAsync("/deudores/top/abc");
        notInt.StatusCode.Should().Be(HttpStatusCode.NotFound, "n no entero no matchea la ruta {n:int}");
    }

    [Fact]
    public async Task WithActiveImport_GetTopDeudores_SituacionFilter_FiltersBySituacionMaxima_AndCounts()
    {
        await fixture.ResetAsync();
        await fixture.SeedActiveImportAsync();
        using var client = fixture.CreateClient();

        using var irrecuperable = await client.GetAsync("/deudores?situacionMaxima=5");
        irrecuperable.StatusCode.Should().Be(HttpStatusCode.OK);
        var worst = await irrecuperable.Content.ReadFromJsonAsync<JsonElement>();
        worst.GetProperty("total").GetInt64().Should().Be(1, "con filtro activo el total cuenta con el mismo filtro");
        worst.GetProperty("items").EnumerateArray()
            .Should().OnlyContain(deudor => deudor.GetProperty("situacionMaxima").GetInt32() == 5);

        using var normal = await client.GetAsync("/deudores?situacionMaxima=1&limit=5");
        normal.StatusCode.Should().Be(HttpStatusCode.OK);
        var normals = await normal.Content.ReadFromJsonAsync<JsonElement>();
        normals.GetProperty("total").GetInt64().Should().Be(2, "dos deudores con situacion_maxima=1");
        normals.GetProperty("items").EnumerateArray()
            .Select(deudor => deudor.GetProperty("cuit").GetString())
            .Should().Equal(["20000000009", "27000000008"], "mantiene el orden por suma_total DESC dentro del filtro");

        using var empty = await client.GetAsync("/deudores?situacionMaxima=2");
        empty.StatusCode.Should().Be(HttpStatusCode.OK, "sin resultados responde 200 con items vacío");
        var emptyPage = await empty.Content.ReadFromJsonAsync<JsonElement>();
        emptyPage.GetProperty("total").GetInt64().Should().Be(0);
        emptyPage.GetProperty("items").GetArrayLength().Should().Be(0);

        await AssertProblemAsync(client, "/deudores?situacionMaxima=100", "Query.InvalidSituacion");
        await AssertProblemAsync(client, "/deudores?situacionMaxima=-1", "Query.InvalidSituacion");
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
    public async Task WithActiveImport_GetEntidades_ReturnsPageEnvelope_WithCodigoFilter()
    {
        await fixture.ResetAsync();
        await fixture.SeedActiveImportAsync();
        using var client = fixture.CreateClient();

        using var all = await client.GetAsync("/entidades");
        all.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await all.Content.ReadFromJsonAsync<JsonElement>();
        page.GetProperty("total").GetInt64().Should().Be(2, "total es la cantidad de entidades del import activo");
        page.GetProperty("items").EnumerateArray()
            .Select(entidad => entidad.GetProperty("codigo").GetInt32())
            .Should().Equal([266, 7], "ordenadas por suma_total DESC");
        page.GetProperty("items").EnumerateArray()
            .Select(entidad => entidad.GetProperty("sumaTotalPrestamos").GetDecimal())
            .Should().BeInDescendingOrder();
        var bancoNacion = page.GetProperty("items").EnumerateArray()
            .Single(entidad => entidad.GetProperty("codigo").GetInt32() == 266);
        bancoNacion.GetProperty("nombre").GetString().Should().Be("BANCO DE LA NACION ARGENTINA");

        using var filtered = await client.GetAsync("/entidades?codigo=266");
        filtered.StatusCode.Should().Be(HttpStatusCode.OK);
        var filteredPage = await filtered.Content.ReadFromJsonAsync<JsonElement>();
        filteredPage.GetProperty("total").GetInt64().Should().Be(1, "filtro exacto por codigo");
        filteredPage.GetProperty("items").GetArrayLength().Should().Be(1);
        filteredPage.GetProperty("items")[0].GetProperty("codigo").GetInt32().Should().Be(266);

        using var partial = await client.GetAsync("/entidades?codigo=2");
        partial.StatusCode.Should().Be(HttpStatusCode.OK);
        var partialPage = await partial.Content.ReadFromJsonAsync<JsonElement>();
        partialPage.GetProperty("total").GetInt64().Should().Be(1, "filtro parcial por prefijo de codigo");
        partialPage.GetProperty("items")[0].GetProperty("codigo").GetInt32().Should().Be(266);

        using var noMatch = await client.GetAsync("/entidades?codigo=999");
        noMatch.StatusCode.Should().Be(HttpStatusCode.OK, "sin resultados responde 200 con items vacío");
        var emptyPage = await noMatch.Content.ReadFromJsonAsync<JsonElement>();
        emptyPage.GetProperty("total").GetInt64().Should().Be(0);
        emptyPage.GetProperty("items").GetArrayLength().Should().Be(0);

        await AssertProblemAsync(client, "/entidades?codigo=abc", "Query.InvalidEntidadCodigo");
        await AssertProblemAsync(client, "/entidades?codigo=123456", "Query.InvalidEntidadCodigo");
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
        totales.GetProperty("cantidadDeudores").GetInt64().Should().Be(4);
        totales.GetProperty("cantidadEntidades").GetInt64().Should().Be(2);
        totales.GetProperty("sumaTotalPrestamos").GetDecimal().Should().Be(1850.7m);

        using var ready = await client.GetAsync("/health/ready");
        var body = await ready.Content.ReadAsStringAsync();
        body.Should().Contain("Healthy", "con import activo el servicio está healthy");
    }

    private static async Task AssertProblemAsync(HttpClient client, string path, string expectedTitle)
    {
        using var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"'{path}' responde 400");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be(expectedTitle);
    }
}
