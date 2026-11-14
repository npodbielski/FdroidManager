using FluentValidation;

namespace FdroidMgr.Api.Validation;

public static class ValidationExtensions
{
    public static IResult? ValidateOrProblem<T>(this IValidator<T> validator, T request)
    {
        var result = validator.Validate(request);
        if (result.IsValid)
        {
            return null;
        }

        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        var flat = string.Join("; ", errors.SelectMany(kv => kv.Value.Select(m => $"{kv.Key}: {m}")));
        return Results.Problem(
            detail: string.IsNullOrEmpty(flat) ? "Invalid request body" : flat,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["error"] = flat, ["errors"] = errors });
    }
}
