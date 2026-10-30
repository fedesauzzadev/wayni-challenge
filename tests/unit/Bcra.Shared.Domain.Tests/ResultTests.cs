using Bcra.Shared.Domain.Errors;
using Bcra.Shared.Domain.Results;
using FluentAssertions;
using Xunit;

namespace Bcra.Shared.Domain.Tests;

public class ResultTests
{
    [Fact]
    public void Ok_CarriesValue()
    {
        var result = Result.Ok(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Fail_CarriesError()
    {
        var error = Error.NotFound("X.NotFound", "missing");

        var result = Result.Fail<int>(error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Value_OnFailedResult_Throws()
    {
        var result = Result.Fail<int>(Error.Failure("X", "broken"));

        var act = () => result.Value;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Fail_NonGeneric_ConstructsWithoutThrowing()
    {
        var act = () => Result.Fail(Error.NotFound("X", "x"));

        act.Should().NotThrow();
    }
}
