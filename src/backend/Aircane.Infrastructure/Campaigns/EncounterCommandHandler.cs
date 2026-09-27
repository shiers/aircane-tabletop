using System.Text.Json;
using Aircane.Domain.Combat;

namespace Aircane.Infrastructure.Campaigns;

/// <summary>
/// Bridges combat command payloads to the pure <see cref="CombatEngine"/> and persists the
/// resulting <see cref="EncounterState"/> into the campaign state JSON under the
/// <c>encounter</c> key. Keeps all combat JSON (de)serialization out of
/// <see cref="CampaignStateService"/>.
/// </summary>
internal static class EncounterCommandHandler
{
    private const string EncounterKey = "encounter";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>The combat command types this handler understands.</summary>
    public static bool Handles(string commandType) => commandType switch
    {
        "StartEncounter" or "RollInitiative" or "AdvanceTurn" or "TickConditions"
            or "ApplyDamage" or "ApplyHealing" or "ApplyCondition" or "RemoveCondition"
            or "DeathSave" => true,
        _ => false,
    };

    /// <summary>
    /// Applies a combat command to the encounter embedded in <paramref name="state"/>, mutating
    /// the <c>encounter</c> key in place. No-ops for commands that require an active encounter
    /// when none exists (except StartEncounter, which creates one).
    /// </summary>
    public static void Apply(
        Dictionary<string, JsonElement> state,
        string commandType,
        Dictionary<string, JsonElement> payload)
    {
        var encounter = ReadEncounter(state);

        var updated = commandType switch
        {
            "StartEncounter" => CombatEngine.StartEncounter(ReadCombatants(payload)),
            "RollInitiative" => encounter is null ? null : CombatEngine.RollInitiative(encounter, ReadInitiative(payload)),
            "AdvanceTurn" => encounter is null ? null : CombatEngine.AdvanceTurn(encounter),
            "TickConditions" => encounter is null ? null : CombatEngine.TickConditions(encounter),
            "ApplyDamage" => encounter is null ? null : CombatEngine.ApplyDamage(encounter, TargetId(payload), Amount(payload)),
            "ApplyHealing" => encounter is null ? null : CombatEngine.ApplyHealing(encounter, TargetId(payload), Amount(payload)),
            "ApplyCondition" => encounter is null ? null : CombatEngine.ApplyCondition(
                encounter, TargetId(payload), ConditionName(payload), RemainingRounds(payload), EndCondition(payload)),
            "RemoveCondition" => encounter is null ? null : CombatEngine.RemoveCondition(encounter, TargetId(payload), ConditionName(payload)),
            "DeathSave" => encounter is null ? null : CombatEngine.RecordDeathSave(
                encounter, TargetId(payload), Success(payload), RecoversHp(payload)),
            _ => encounter,
        };

        if (updated is not null)
            WriteEncounter(state, updated);
    }

    // ── State (de)serialization ────────────────────────────────────────────────

    private static EncounterState? ReadEncounter(Dictionary<string, JsonElement> state)
    {
        if (!state.TryGetValue(EncounterKey, out var el) || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        try
        {
            return el.Deserialize<EncounterState>(JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void WriteEncounter(Dictionary<string, JsonElement> state, EncounterState encounter)
    {
        state[EncounterKey] = JsonSerializer.SerializeToElement(encounter, JsonOptions);
    }

    // ── Payload readers (tolerant of missing/typed fields) ──────────────────────

    private static IReadOnlyList<Combatant> ReadCombatants(Dictionary<string, JsonElement> payload)
    {
        if (payload.TryGetValue("combatants", out var el) && el.ValueKind == JsonValueKind.Array)
        {
            try
            {
                return el.Deserialize<List<Combatant>>(JsonOptions) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
        return [];
    }

    private static IReadOnlyDictionary<string, int> ReadInitiative(Dictionary<string, JsonElement> payload)
    {
        var result = new Dictionary<string, int>();
        if (payload.TryGetValue("initiative", out var el) && el.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in el.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var v))
                    result[prop.Name] = v;
            }
        }
        return result;
    }

    private static string TargetId(Dictionary<string, JsonElement> payload) =>
        payload.TryGetValue("combatantId", out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? string.Empty
            : payload.TryGetValue("characterId", out var cid) && cid.ValueKind == JsonValueKind.String
                ? cid.GetString() ?? string.Empty
                : string.Empty;

    private static int Amount(Dictionary<string, JsonElement> payload) =>
        payload.TryGetValue("amount", out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v)
            ? v : 0;

    private static string ConditionName(Dictionary<string, JsonElement> payload) =>
        payload.TryGetValue("conditionName", out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? string.Empty : string.Empty;

    private static int? RemainingRounds(Dictionary<string, JsonElement> payload) =>
        payload.TryGetValue("remainingRounds", out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v)
            ? v : null;

    private static string? EndCondition(Dictionary<string, JsonElement> payload) =>
        payload.TryGetValue("endCondition", out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() : null;

    private static bool Success(Dictionary<string, JsonElement> payload) =>
        payload.TryGetValue("success", out var el) && el.ValueKind is JsonValueKind.True;

    private static bool RecoversHp(Dictionary<string, JsonElement> payload) =>
        payload.TryGetValue("recoversHp", out var el) && el.ValueKind is JsonValueKind.True;
}
