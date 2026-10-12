namespace Aircane.Application.DTOs.Adventures;

/// <summary>Sent as generation moves through its stages.</summary>
public sealed record AdventureGenerationProgressNotification(
    string JobId, string Stage, int PercentComplete, string Message);

/// <summary>Sent once the adventure has been generated and saved.</summary>
public sealed record AdventureGenerationCompletedNotification(
    string JobId, string AdventureId, string AdventureTitle);

/// <summary>Sent when generation fails.</summary>
public sealed record AdventureGenerationFailedNotification(string JobId, string Error);

/// <summary>
/// The generation stages shown to the user: the name, how far along the bar is when the stage
/// begins, and the message displayed while it runs.
/// </summary>
public static class AdventureGenerationStages
{
    public const string Analysing = "analysing";
    public const string Pitch = "pitch";
    public const string Outline = "outline";
    public const string Scenes = "scenes";
    public const string Npcs = "npcs";
    public const string Encounters = "encounters";
    public const string Clues = "clues";
    public const string Complete = "complete";

    private static readonly Dictionary<string, (int Percent, string Message)> Table = new()
    {
        [Analysing] = (10, "Analysing party and setting..."),
        [Pitch] = (20, "Crafting the adventure concept..."),
        [Outline] = (35, "Building the adventure outline..."),
        [Scenes] = (55, "Writing scenes and locations..."),
        [Npcs] = (70, "Creating characters and NPCs..."),
        [Encounters] = (82, "Designing encounters..."),
        [Clues] = (92, "Placing clues and secrets..."),
        [Complete] = (100, "Adventure ready!"),
    };

    public static int PercentFor(string stage) => Table.TryGetValue(stage, out var v) ? v.Percent : 0;

    public static string MessageFor(string stage) => Table.TryGetValue(stage, out var v) ? v.Message : stage;
}
