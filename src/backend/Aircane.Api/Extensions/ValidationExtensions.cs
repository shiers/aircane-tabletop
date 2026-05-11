using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Extensions;

/// <summary>
/// Extension methods for converting FluentValidation results to ASP.NET Core problem details.
/// </summary>
public static class ValidationExtensions
{
    /// <summary>
    /// Converts a <see cref="ValidationResult"/> to a <see cref="ValidationProblemDetails"/>
    /// compatible with the standard ASP.NET Core model validation response format.
    /// </summary>
    public static ValidationProblemDetails ToValidationProblemDetails(this ValidationResult result)
    {
        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).ToArray());

        return new ValidationProblemDetails(errors)
        {
            Title = "One or more validation errors occurred.",
            Status = StatusCodes.Status400BadRequest,
        };
    }
}
