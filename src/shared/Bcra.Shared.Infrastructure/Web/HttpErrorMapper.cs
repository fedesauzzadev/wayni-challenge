using Bcra.Shared.Domain.Errors;
using Microsoft.AspNetCore.Http;

namespace Bcra.Shared.Infrastructure.Web;

public static class HttpErrorMapper
{
    public static IResult ToProblem(this Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Problem(
            statusCode: statusCode,
            title: error.Code,
            detail: error.Description,
            type: $"https://bcra.errors/{error.Code}");
    }
}
