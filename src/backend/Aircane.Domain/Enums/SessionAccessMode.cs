namespace Aircane.Domain.Enums;

/// <summary>
/// Describes how players connect to a session.
/// </summary>
public enum SessionAccessMode
{
    Solo,
    LocalLan,
    InternetTunnel,
    Cloud
}
