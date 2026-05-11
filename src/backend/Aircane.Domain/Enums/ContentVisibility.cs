namespace Aircane.Domain.Enums;

/// <summary>
/// Controls who can see a piece of content (document, chunk, or roll).
/// Revealed is used for adventure content that the DM has explicitly shown to players.
/// </summary>
public enum ContentVisibility
{
    Public,
    DMOnly,
    Hidden,
    Revealed
}
