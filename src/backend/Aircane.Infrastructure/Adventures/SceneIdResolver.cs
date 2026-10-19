using System.Globalization;
using Aircane.Application.DTOs.Adventures;

namespace Aircane.Infrastructure.Adventures;

/// <summary>
/// Maps the scene ids a stage returned onto the real scene ids from the scenes stage. The AI is told to
/// echo the exact string ids but sometimes returns an index instead (<c>3</c>), so a value that isn't a
/// scene id is accepted as an index into the scene list: 1-based by default, 0-based when the same
/// response also uses 0. Anything that can't be mapped fails with the field path and the bad value.
/// </summary>
internal sealed class SceneIdResolver
{
    private readonly IReadOnlyList<string> _sceneIds;
    private readonly bool _zeroBased;

    private SceneIdResolver(IReadOnlyList<string> sceneIds, bool zeroBased)
    {
        _sceneIds = sceneIds;
        _zeroBased = zeroBased;
    }

    /// <summary>Creates a resolver for one stage response, given every id value that response used.</summary>
    public static SceneIdResolver For(IReadOnlyList<GeneratedScene> scenes, IEnumerable<string?> idsUsedByResponse)
    {
        var sceneIds = scenes.Select(s => s.SceneId).ToList();
        var zeroBased = idsUsedByResponse.Any(id =>
            id is not null
            && TryParseIndex(id, out var n) && n == 0
            && !sceneIds.Contains(id, StringComparer.Ordinal));
        return new SceneIdResolver(sceneIds, zeroBased);
    }

    /// <summary>The valid ids, comma-separated, for error messages and prompts.</summary>
    public static string Describe(IEnumerable<string> sceneIds) => string.Join(", ", sceneIds);

    /// <summary>Resolves one id, or throws <see cref="InvalidOperationException"/> naming <paramref name="field"/>.</summary>
    public string Resolve(string? raw, string field)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw Invalid(field, "is null or empty");

        var exact = _sceneIds.FirstOrDefault(id => string.Equals(id, raw, StringComparison.Ordinal))
            ?? _sceneIds.FirstOrDefault(id => string.Equals(id, raw.Trim(), StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        if (TryParseIndex(raw, out var number))
        {
            var index = _zeroBased ? number : number - 1;
            if (index >= 0 && index < _sceneIds.Count)
                return _sceneIds[index];
        }

        throw Invalid(field, $"has the value '{raw}', which is not a scene id");
    }

    /// <summary>True when <paramref name="raw"/> is a whole number, however the AI wrote it (3, "3", 3.0).</summary>
    public static bool TryParseIndex(string raw, out int number)
    {
        number = 0;
        if (!decimal.TryParse(raw.Trim(), NumberStyles.Integer | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value)
            || value != decimal.Truncate(value)
            || value is < int.MinValue or > int.MaxValue)
            return false;

        number = (int)value;
        return true;
    }

    private InvalidOperationException Invalid(string field, string problem)
        => new($"Field '{field}' {problem}. Valid scene ids are: {Describe(_sceneIds)}.");
}
