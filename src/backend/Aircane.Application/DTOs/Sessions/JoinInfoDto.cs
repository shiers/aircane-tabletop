namespace Aircane.Application.DTOs.Sessions;

/// <summary>
/// The least a join page needs before the player has a token: whether the session can be joined,
/// and its name if so. Deliberately carries nothing else (no participants, invite code, campaign or
/// access mode), and says the same thing for a session that does not exist, has ended, or is not
/// open to players.
/// </summary>
/// <param name="Name">The session's name; null unless <paramref name="Joinable"/>.</param>
/// <param name="Joinable">True when the session is running and open to players.</param>
public sealed record JoinInfoDto(string? Name, bool Joinable)
{
    /// <summary>What every session that cannot be joined looks like.</summary>
    public static JoinInfoDto NotJoinable { get; } = new(Name: null, Joinable: false);
}
