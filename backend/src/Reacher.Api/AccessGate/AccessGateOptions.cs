using System.ComponentModel.DataAnnotations;

namespace Reacher.Api.AccessGate;

/// <summary>Shared password for the test deployment (D17), bound from the <c>AccessGate</c> section.</summary>
public sealed class AccessGateOptions : IValidatableObject
{
    public const string SectionName = "AccessGate";

    public const int MinimumPasswordLength = 32;

    /// <summary>On unless a setting turns it off, so a forgotten setting fails closed.</summary>
    public bool Enabled { get; set; } = true;

    public string? Password { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enabled)
        {
            yield break;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            yield return new ValidationResult("Missing setting 'AccessGate:Password'.");
        }
        else if (Password.Length < MinimumPasswordLength)
        {
            yield return new ValidationResult(
                $"Setting 'AccessGate:Password' must be at least {MinimumPasswordLength} characters.");
        }
    }
}
