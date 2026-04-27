using System.ComponentModel.DataAnnotations;

namespace ComplianceApp.Infrastructure.Authentication;

/// <summary>
/// Configuration for the local-dev JWT issuer (Phase 1, option A).
/// Bound from the <c>DevAuth</c> section of <c>appsettings.Development.json</c>.
/// In Production, the issuer must be disabled — startup throws if it isn't.
/// </summary>
public class DevAuthOptions
{
    public const string SectionName = "DevAuth";

    /// <summary>When false, the dev issuer endpoint and signing key are not registered.</summary>
    public bool Enabled { get; set; }

    [Required]
    public string Issuer { get; set; } = "compliance-flow-dev";

    [Required]
    public string Audience { get; set; } = "compliance-flow-api";

    /// <summary>Symmetric signing key. HS256 requires at least 32 bytes (256 bits).</summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 24 * 60)]
    public int TokenLifetimeMinutes { get; set; } = 60;
}
