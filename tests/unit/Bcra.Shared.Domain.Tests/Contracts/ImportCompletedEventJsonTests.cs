using System.Text.Json;
using Bcra.Shared.Domain.Contracts;
using FluentAssertions;
using Xunit;

namespace Bcra.Shared.Domain.Tests.Contracts;

public class ImportCompletedEventJsonTests
{
    private const string FixtureJson =
        """{"jobId":"0f2f1d0e-8b8a-4c3d-9e7f-2a1b0c5d4e6f","source":"/data/deudores.txt","periodo":202311,"linesProcessed":1000,"linesRejected":3,"linesSkippedControl":285,"durationMs":4321.5,"completedAtUtc":"2026-10-29T12:00:00Z","totalDeudores":19247633,"totalEntidades":485,"sumaTotalPrestamos":1850.7}""";

    private static readonly JsonSerializerOptions WebDefaultsItReplaces = new(JsonSerializerDefaults.Web);

    private static ImportCompletedEvent CreateFixtureEvent() => new(
        Guid.Parse("0f2f1d0e-8b8a-4c3d-9e7f-2a1b0c5d4e6f"),
        "/data/deudores.txt",
        202311,
        1_000,
        3,
        285,
        4_321.5,
        new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc),
        TotalDeudores: 19_247_633,
        TotalEntidades: 485,
        SumaTotalPrestamos: 1850.7m);

    [Fact]
    public void Serialize_Fixture_IsByteIdentical()
    {
        var json = JsonSerializer.Serialize(CreateFixtureEvent(), ImportCompletedEventJson.Options);

        json.Should().Be(FixtureJson);
    }

    [Fact]
    public void Serialize_ZeroTotals_OmitsThemFromDefaultCtor()
    {
        var withoutTotals = CreateFixtureEvent() with { TotalDeudores = 0, TotalEntidades = 0, SumaTotalPrestamos = 0 };

        JsonSerializer.Serialize(withoutTotals, ImportCompletedEventJson.Options)
            .Should().Contain("\"totalDeudores\":0", "los totales siempre viajan (default 0), sin condicionales de serialización");
    }

    [Fact]
    public void Deserialize_OldMessageWithoutTotals_DefaultsToZero()
    {
        var legacy =
            """{"jobId":"0f2f1d0e-8b8a-4c3d-9e7f-2a1b0c5d4e6f","source":"/data/deudores.txt","periodo":202311,"linesProcessed":1000,"linesRejected":3,"linesSkippedControl":285,"durationMs":4321.5,"completedAtUtc":"2026-10-29T12:00:00Z"}""";

        var deserialized = JsonSerializer.Deserialize<ImportCompletedEvent>(legacy, ImportCompletedEventJson.Options);

        deserialized.Should().Be(CreateFixtureEvent() with { TotalDeudores = 0, TotalEntidades = 0, SumaTotalPrestamos = 0 });
    }

    [Fact]
    public void Serialize_MatchesWebDefaults_ItReplaces()
    {
        JsonSerializer.Serialize(CreateFixtureEvent(), ImportCompletedEventJson.Options)
            .Should().Be(JsonSerializer.Serialize(CreateFixtureEvent(), WebDefaultsItReplaces));
    }

    [Fact]
    public void Fixture_RoundTripsThroughSharedOptions()
    {
        var deserialized = JsonSerializer.Deserialize<ImportCompletedEvent>(FixtureJson, ImportCompletedEventJson.Options);

        deserialized.Should().Be(CreateFixtureEvent());
    }

    [Fact]
    public void RoundTrip_ReturnsEqualEvent()
    {
        var json = JsonSerializer.Serialize(CreateFixtureEvent(), ImportCompletedEventJson.Options);
        var deserialized = JsonSerializer.Deserialize<ImportCompletedEvent>(json, ImportCompletedEventJson.Options);

        deserialized.Should().Be(CreateFixtureEvent());
    }

    [Fact]
    public void Options_IsImmutable()
    {
        ImportCompletedEventJson.Options.IsReadOnly.Should().BeTrue();
        var mutate = () => ImportCompletedEventJson.Options.PropertyNamingPolicy = null;
        mutate.Should().Throw<InvalidOperationException>();
    }
}
