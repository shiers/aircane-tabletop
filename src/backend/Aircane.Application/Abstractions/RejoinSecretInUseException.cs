namespace Aircane.Application.Abstractions;

/// <summary>
/// A join request carried a rejoin secret that already belongs to a participant with a seat in the session.
/// The browser should rejoin with it instead; two seats must never share one secret.
/// </summary>
public sealed class RejoinSecretInUseException : Exception
{
    public RejoinSecretInUseException()
        : base("This browser already has a place in this session. Go back to it instead of joining again.")
    {
    }
}
