namespace Aircane.Api.Authorization;

/// <summary>
/// Constants for the authorization policy names used across the application.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>
    /// Requires the Host role. Used for admin actions like managing the library,
    /// AI settings, session creation/end, and adventure management.
    /// </summary>
    public const string HostOnly = "HostOnly";

    /// <summary>
    /// Requires either Host or HumanDm role. Used for campaign CRUD,
    /// character management, proposal approval, campaign state mutations, etc.
    /// </summary>
    public const string DmOrHost = "DmOrHost";

    /// <summary>
    /// Requires any valid authenticated participant token.
    /// Used for player actions, dice rolls, character reads, rules questions, etc.
    /// </summary>
    public const string Authenticated = "Authenticated";

    /// <summary>
    /// <see cref="Authenticated"/>, and the token's session (<c>sid</c>) is the session the route is about:
    /// the session in the route, or the session that owns the campaign, character, adventure run or
    /// proposal it names. The participant must be approved and not denied. The trusted local host is
    /// exempt where the role policies exempt it. Applied at class level to every controller with a
    /// session-keyed route (see <see cref="SessionScopeRequirement"/>).
    /// </summary>
    public const string SessionMember = "SessionMember";
}
