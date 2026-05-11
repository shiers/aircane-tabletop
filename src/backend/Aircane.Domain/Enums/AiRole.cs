namespace Aircane.Domain.Enums;

/// <summary>
/// Defines the role the AI plays in a campaign session.
/// </summary>
public enum AiRole
{
    /// <summary>Suggestions, rules lookup, and DM-facing help only.</summary>
    Assistant,

    /// <summary>Configurable delegated duties alongside a human DM.</summary>
    CoDm,

    /// <summary>AI runs the full session as DM.</summary>
    FullDm,

    /// <summary>Scene-level AI role overrides are allowed.</summary>
    Hybrid
}
