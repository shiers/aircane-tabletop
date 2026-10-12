namespace Aircane.Domain.Entities;

/// <summary>
/// One line of an adventure run's conversation: something the player did, or the AI DM's narration
/// in response. Stored so a player can leave a run and come back to the same narration feed.
/// </summary>
public class AdventureSessionEntry
{
    /// <summary>Unique identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>The run this entry belongs to.</summary>
    public Guid AdventureSessionId { get; set; }

    /// <summary>Whether this is the player's action or the AI's narration.</summary>
    public AdventureEntryType EntryType { get; set; }

    /// <summary>The text of the action or narration.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>The scene the run was in when the entry was written.</summary>
    public string? SceneId { get; set; }

    /// <summary>When the entry was written. Entries are read back in this order.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Who an <see cref="AdventureSessionEntry"/> came from.</summary>
public enum AdventureEntryType
{
    /// <summary>What the player typed.</summary>
    PlayerAction = 0,

    /// <summary>What the AI DM narrated back.</summary>
    AiNarration = 1,
}
