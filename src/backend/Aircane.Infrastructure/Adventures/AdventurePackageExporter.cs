using System.Text.Json;
using Aircane.Application.DTOs.Adventures;

namespace Aircane.Infrastructure.Adventures;

/// <summary>
/// Serializes a generated adventure into the package format.
/// The package format consists of individual JSON files for each section,
/// matching the design specification:
///   /generated-adventures/{adventureId}/
///     adventure.json
///     scenes.json
///     npcs.json
///     encounters.json
///     treasure.json
///     clues.json
///     session-notes.md
/// </summary>
public static class AdventurePackageExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>
    /// Converts a GeneratedAdventureDto into the package structure as a dictionary
    /// of filename → content. This can be written to disk or returned as a download.
    /// </summary>
    /// <param name="adventure">The generated adventure to export.</param>
    /// <returns>Dictionary mapping file names to their serialized content.</returns>
    public static Dictionary<string, string> ExportToPackage(GeneratedAdventureDto adventure)
    {
        ArgumentNullException.ThrowIfNull(adventure);

        var package = new Dictionary<string, string>();

        // adventure.json — metadata, pitch, and outline
        var metadata = new AdventurePackageMetadata
        {
            Id = adventure.Id,
            Title = adventure.Pitch?.Title ?? "Untitled Adventure",
            Status = adventure.Status,
            Ruleset = adventure.Request.Ruleset,
            GameSystem = adventure.Request.GameSystem,
            Pitch = adventure.Pitch,
            Outline = adventure.Outline,
            CreatedAt = adventure.CreatedAt,
            UpdatedAt = adventure.UpdatedAt,
        };
        package["adventure.json"] = JsonSerializer.Serialize(metadata, JsonOptions);

        // scenes.json
        if (adventure.Scenes is not null)
            package["scenes.json"] = JsonSerializer.Serialize(adventure.Scenes, JsonOptions);

        // npcs.json
        if (adventure.Npcs is not null)
            package["npcs.json"] = JsonSerializer.Serialize(adventure.Npcs, JsonOptions);

        // encounters.json
        if (adventure.Encounters is not null)
            package["encounters.json"] = JsonSerializer.Serialize(adventure.Encounters, JsonOptions);

        // treasure.json
        if (adventure.Treasure is not null)
            package["treasure.json"] = JsonSerializer.Serialize(adventure.Treasure, JsonOptions);

        // clues.json
        if (adventure.Clues is not null)
            package["clues.json"] = JsonSerializer.Serialize(adventure.Clues, JsonOptions);

        // session-notes.md — empty template for DM session notes
        package["session-notes.md"] = GenerateSessionNotesTemplate(adventure);

        return package;
    }

    /// <summary>
    /// Writes the adventure package to a directory on disk.
    /// Creates the directory if it does not exist.
    /// </summary>
    /// <param name="adventure">The generated adventure to export.</param>
    /// <param name="basePath">Base directory for generated adventures.</param>
    /// <returns>The full path to the adventure package directory.</returns>
    public static async Task<string> ExportToDirectoryAsync(
        GeneratedAdventureDto adventure,
        string basePath)
    {
        ArgumentNullException.ThrowIfNull(adventure);
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);

        var adventureDir = Path.Combine(basePath, adventure.Id.ToString());
        Directory.CreateDirectory(adventureDir);

        var package = ExportToPackage(adventure);

        foreach (var (fileName, content) in package)
        {
            var filePath = Path.Combine(adventureDir, fileName);
            await File.WriteAllTextAsync(filePath, content);
        }

        return adventureDir;
    }

    /// <summary>
    /// Generates a markdown session notes template for the adventure.
    /// </summary>
    private static string GenerateSessionNotesTemplate(GeneratedAdventureDto adventure)
    {
        var title = adventure.Pitch?.Title ?? "Untitled Adventure";
        var sceneList = string.Empty;

        if (adventure.Scenes is { Count: > 0 })
        {
            sceneList = string.Join("\n", adventure.Scenes.Select(s =>
                $"### {s.Title}\n\n- Status: Not started\n- Notes:\n"));
        }

        return $"""
            # Session Notes: {title}

            ## Adventure Info
            - **ID:** {adventure.Id}
            - **Status:** {adventure.Status}
            - **Created:** {adventure.CreatedAt:yyyy-MM-dd}

            ## Session Log

            {sceneList}
            ## General Notes

            """;
    }
}
