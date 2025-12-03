using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace DbExecPlanMonitor.Application.Configuration;

/// <summary>
/// Validates configuration options at startup using DataAnnotations.
/// </summary>
/// <typeparam name="TOptions">The options type to validate.</typeparam>
public sealed class DataAnnotationsValidateOptions<TOptions>
    : IValidateOptions<TOptions> where TOptions : class
{
    /// <summary>
    /// The name of the options instance.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Validates the specified options.
    /// </summary>
    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        // Only validate if name matches
        if (Name is not null && Name != name)
        {
            return ValidateOptionsResult.Skip;
        }

        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(options);

        if (!Validator.TryValidateObject(
            options,
            validationContext,
            validationResults,
            validateAllProperties: true))
        {
            var failures = validationResults
                .Where(r => r != ValidationResult.Success)
                .Select(r => $"{string.Join(", ", r.MemberNames)}: {r.ErrorMessage}");

            return ValidateOptionsResult.Fail(failures);
        }

        // Also validate IValidatableObject implementation
        if (options is IValidatableObject validatable)
        {
            var customResults = validatable.Validate(validationContext);
            var customFailures = customResults
                .Where(r => r != ValidationResult.Success)
                .Select(r => $"{string.Join(", ", r.MemberNames)}: {r.ErrorMessage}");

            if (customFailures.Any())
            {
                return ValidateOptionsResult.Fail(customFailures);
            }
        }

        return ValidateOptionsResult.Success;
    }
}
