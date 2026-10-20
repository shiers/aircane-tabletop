namespace Aircane.Domain.Enums;

/// <summary>How the turn of a group adventure run moves to the next player.</summary>
public enum TurnAdvanceMode
{
    /// <summary>
    /// The turn moves only when the acting player says they are done or the host passes it. The default.
    /// </summary>
    ManualOnly = 0,

    /// <summary>
    /// The turn also moves to the next player by itself, once the AI's narration for the acting player's
    /// action has been answered (and unless the AI is waiting for that player to report a roll).
    /// </summary>
    AfterEachAction = 1,
}
