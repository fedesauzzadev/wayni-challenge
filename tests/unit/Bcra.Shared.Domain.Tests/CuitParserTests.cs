using Bcra.Shared.Domain;
using Bcra.Shared.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Bcra.Shared.Domain.Tests;

public class CuitParserTests
{
    [Theory]
    [InlineData("20003905528")]
    [InlineData("27962062194")]
    [InlineData("33716066369")]
    public void TryParse_WithElevenDigits_Succeeds(string value)
    {
        var parsed = CuitParser.TryParse(value, out var cuit);

        parsed.Should().BeTrue();
        cuit.Value.Should().Be(value);
    }

    [Theory]
    [InlineData("2000390552")]
    [InlineData("200039055288")]
    [InlineData("")]
    [InlineData("2000390552A")]
    [InlineData("20-00390552-8")]
    [InlineData("  ")]
    public void TryParse_WithInvalidValues_Fails(string value)
    {
        CuitParser.TryParse(value, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_TrimsSurroundingWhitespace()
    {
        CuitParser.TryParse("  20003905528 ", out var cuit).Should().BeTrue();
        cuit.Value.Should().Be("20003905528");
    }

    [Fact]
    public void Format_ReturnsCanonicalGrouping()
    {
        CuitParser.Format(Cuit.FromTrusted("27962062194")).Should().Be("27-96206219-4");
    }

    [Theory]
    [InlineData("20003905528")]
    [InlineData("20-00390552-8")]
    [InlineData("  20003905528 ")]
    [InlineData("20 00390552 8")]
    public void TryNormalize_StripsNonDigits_AndSucceedsWithElevenDigits(string value)
    {
        var normalized = CuitParser.TryNormalize(value, out var cuit);

        normalized.Should().BeTrue();
        cuit.Value.Should().Be("20003905528");
    }

    [Theory]
    [InlineData("2000390552")]        // menos de 11 dígitos
    [InlineData("200039055288")]      // más de 11 dígitos
    [InlineData("20-0039055-288")]    // más de 11 dígitos tras strip
    [InlineData("abcdefghijklm")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("--")]
    public void TryNormalize_WithoutExactlyElevenDigits_Fails(string value)
    {
        CuitParser.TryNormalize(value, out var cuit).Should().BeFalse();
        cuit.Value.Should().BeNull();
    }
}

public class CuitTests
{
    [Fact]
    public void IsValid_RejectsNonElevenDigits()
    {
        Cuit.IsValid("20003905528").Should().BeTrue();
        Cuit.IsValid("2003905528").Should().BeFalse();
        Cuit.IsValid("abcdefghijklm").Should().BeFalse();
    }

    [Fact]
    public void ValueObjects_WithSameValue_AreEqual()
    {
        Cuit.FromTrusted("20003905528").Should().Be(Cuit.FromTrusted("20003905528"));
    }
}
