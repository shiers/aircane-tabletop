namespace Aircane.Application.Abstractions;

/// <summary>Who moved a group run's turn on.</summary>
public enum TurnPassedBy
{
    /// <summary>The host passed it (they may pass any turn).</summary>
    Host,

    /// <summary>The acting player said they are done.</summary>
    Player,

    /// <summary>The run moved on by itself after the AI answered (<c>AfterEachAction</c>).</summary>
    Auto,
}

/// <summary>A request to pass the turn.</summary>
/// <param name="By">Who is passing it.</param>
/// <param name="PassedByParticipantId">The participant who passed it; null for the host and for auto-advance.</param>
/// <param name="ExpectedActiveParticipantId">
/// The participant the caller believes holds the turn. When set and the turn has moved on, nothing changes and
/// <see cref="TurnAlreadyMovedException"/> is thrown. Null passes whoever holds it (the host's plain "pass").
/// </param>
public sealed record TurnPass(TurnPassedBy By, Guid? PassedByParticipantId = null, Guid? ExpectedActiveParticipantId = null);

/// <summary>The turn is no longer where the caller expected it, so it was not moved.</summary>
public sealed class TurnAlreadyMovedException : InvalidOperationException
{
    public TurnAlreadyMovedException(Guid? nowActiveParticipantId, string? nowActiveName)
        : base($"The turn has already passed to {nowActiveName ?? "the next player"}.")
    {
        NowActiveParticipantId = nowActiveParticipantId;
        NowActiveName = nowActiveName;
    }

    public Guid? NowActiveParticipantId { get; }

    public string? NowActiveName { get; }
}
