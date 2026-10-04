# Design — Character Source Import Adapters (D&D Beyond, VTT, Pathbuilder 2e)

Base branch: `dev`. Worktree: `d:\Development\aircane-tabletop\.worktrees\character-source-import`.
This is **iteration 3**, rewritten to resolve the iteration-2 `design-review.json` findings (2 HIGH,
3 MEDIUM, 2 NIT). The responses to each finding are listed at the end. The two consequential changes
in this iteration:

1. **`MappedFields` is now owned by each mapper, not derived by flattening a `CanonicalCharacter`.**
   `CanonicalCharacter` initializes abilities to 10, AC to 10, Speed to 30, ProficiencyBonus to 2
   (verified in `CanonicalCharacter.cs`), so a blind `Flatten` cannot tell a value the mapper set
   from a constructor default and would seed the review form with phantom values for the very
   low-confidence sources (Roll20/Generic) that most need honest review. Each mapper now reports the
   exact set of `ApplyMapping` paths it actually populated; the controller echoes that set
   (findings 2, 3).
2. **The "every path is accepted by `ApplyMapping`" correctness guard moves to the integration suite**
   because `ApplyMapping` is `private static` on `CharacterService` and is only reachable through
   `ApplyFieldMappingsAsync` (which needs a persisted `Character` + DbContext). The pure unit test
   keeps only the schema-id↔path bridge-table 1:1 check (finding 1).

The iteration-1→2 re-anchoring onto the real `CanonicalCharacter` + `CharacterService.ApplyMapping`
rail (instead of `CharacterSchemaEngine.MapImportedFields` / schema-field-ids) is retained.

## Overview

This feature adds source-specific character import adapters (Pathbuilder 2e, D&D Beyond URL +
D&D Beyond file, Foundry dnd5e, Foundry pf2e, Roll20, Generic VTT, and D&D Beyond PDF hints) that
funnel into the **existing** character import + field-review + save pipeline. No new canonical
schema is introduced, no mapper writes to typed character state, and the AI never mutates state.

Each new source is an adapter that produces exactly what the existing PDF import path already
produces: a `CanonicalCharacter` draft plus a set of leftover source fields. The controller then
persists a draft `Character` and returns the existing `CharacterFieldReviewDto`, so the user lands
in the field-review UI that already exists. On confirm, the existing
`PUT /api/characters/{id}/field-mappings` (`ApplyFieldMappingsAsync`) serializes the final
`CanonicalJson`. Nothing bypasses the server-side validation that already lives in that flow.

## Why the mapper rail changed (reconciliation of the correction note vs. reality)

The task brief and the mid-task correction note both described mapper output in terms the repo does
not use (`CharacterImportDraft`; later, `CharacterSchemaEngine.MapImportedFields(dictionary, …)`).
Reading the real save path end-to-end shows there are **two different canonical vocabularies** in
the codebase, and only one of them is wired to persistence for the review flow:

1. **`CharacterSchemaEngine.MapImportedFields(Dictionary<string,string>, CharacterSchema)` →
   `FieldMappingResult`** keyed by **schema-field-ids** (`str`, `dex`, `hp_max`, `ac`, `ancestry`…).
   Verified field ids in `DnD5e2014Seed.cs`: `name, level, class, race, str, dex, con, int, wis,
   cha, ac, hp_max, hp_current` (+ calculated `*_mod`, `initiative`, spell slots, `known_spells`).
   Verified in `Pathfinder2eRemasterSeed.cs`: `name, level, ancestry, heritage, background, class,
   str, dex, con, int, wis, cha, ac, hp_max, hp_current, fortitude, reflex, will, perception_rank,
   class_dc_rank`. This engine is used by schema **validation** and **form rendering**, not by the
   character persistence that the review UI drives.

2. **`CanonicalCharacter` + `CharacterService.ApplyMapping(canonical, path, value)`** keyed by
   **dotted canonical paths** (`identity.name`, `identity.raceOrAncestry`, `identity.background`,
   `class`, `level`, `abilities.strength`…`abilities.charisma`, `combat.armorClass`,
   `combat.maxHitPoints`, `combat.hitPoints` (current), `combat.speed`, `combat.initiative`,
   `combat.proficiencyBonus`). This is the vocabulary the **existing PDF import** uses:
   `PdfCharacterExtractor` builds a `CanonicalCharacter`, `PersistDraftFromExtractionAsync`
   serializes it with `CharacterJsonSerializer.Serialize(CanonicalCharacter)`, the review DTO's
   `UnmappedFieldDto.SuggestedCanonicalField` emits these paths, and
   `applyFieldMappings` → `ApplyFieldMappingsAsync` → `ApplyMapping` consumes them to produce the
   final `CanonicalJson`. The frontend `CANONICAL_FIELD_OPTIONS` list is this exact set.

The review flow is **entirely built on rail #2.** Iteration 1 put the mappers on rail #1, which
caused the two HIGH findings (the controller can't both bind the typed request and feed a
`JsonDocument`; and `MapImportedFields`' `Contains` fallback mis-maps dotted/abbreviated keys) plus
the type-gap MEDIUM (`CharacterJsonSerializer.Serialize` needs a `CanonicalCharacter`, not a flat
dictionary). **Iteration 2 puts the mappers on rail #2**, which is the rail the entire existing
review/save flow already speaks. This is a deliberate CHOICE between the two viable rails, made for
these reasons:

- It reuses `PersistDraftFromExtractionAsync`, `CharacterFieldReviewDto`, `applyFieldMappings`, and
  `ApplyMapping` verbatim — the mappers slot in exactly where `PdfCharacterExtractor` already does.
- `ApplyMapping` validates every value server-side (range-checked `ParseIntMapping`, level parse)
  and throws `ArgumentException` on unknown paths/values, so steering's "AI/importers never mutate
  state without server validation" is satisfied by the pre-existing code with no new validator.
- It removes the schema-id alignment hazard entirely: mappers emit the finite, known
  `ApplyMapping` path set, not fuzzy keys matched by a `Contains` fallback.

`MapImportedFields` and `CharacterSchemaEngine` are therefore **not used** by the adapter import
path. (They remain untouched for validation/form rendering, which the review UI already consumes
through a separate, already-existing endpoint — see "Review step" below.)

## Reconciliation: brief/correction concept → real repo artifact

| Brief / correction concept | Real repository artifact | Resolution |
|---|---|---|
| `CharacterImportDraft` (mapper return) | **Does not exist.** Mapper returns a new `SourceMapResult` that wraps a real `CanonicalCharacter` (`Aircane.Application/Characters/CanonicalCharacter.cs`) + leftover fields. | See "Mapper output contract". |
| `ICharacterSourceMapper.Map` returns a "draft" | Returns `SourceMapResult` (new), whose core payload is the existing `CanonicalCharacter`. | No new canonical character type. |
| `POST /api/characters/import/json` | `POST /api/characters/import` — `CharactersController.ImportCharacter`, body `ImportCharacterJsonRequest`, returns 201 `CharacterDto` / 400 ProblemDetails / 422 `CharacterImportResult`. | Extended; legacy contract preserved. |
| PDF import | `POST /api/characters/import/pdf` → `CharacterFieldReviewDto`. | Unchanged shape; DDB PDF hints extend `PdfCharacterExtractor` internals only. |
| "the existing import service / interface" | `ICharacterService` (`Aircane.Application/Abstractions/ICharacterService.cs`) + `CharacterService` (`Aircane.Infrastructure/Characters/CharacterService.cs`). | Adapters reuse its `ApplyMapping`/draft-persist semantics. |
| "field review type" | `CharacterFieldReviewDto` + `UnmappedFieldDto` (`Aircane.Application/DTOs/Characters/CharacterFieldReviewDto.cs`). | Extended with source/confidence/ruleset signals + `GameSystemDefinitionId` + `MappedFields` + `RequiresGameSystemSelection` + per-field `RequiresReview`. |
| `CharacterSchemaEngine.MapImportedFields` (correction) | Real signature `FieldMappingResult MapImportedFields(Dictionary<string,string>, CharacterSchema)`. | **Not used by this feature** (see rationale above). |
| FormDescriptor renderer | `features/game-systems/components/CharacterSchemaRenderer.vue`; descriptor from `POST /api/game-systems/{id:guid}/preview-character-form`. | Reused in the review step; route named below (finding 4). |
| `ImportCharacterModal.vue` | `src/frontend/aircane-web/src/features/characters/components/ImportCharacterModal.vue`. | Extended with tabs. |
| characters API client | `src/frontend/aircane-web/src/features/characters/api.ts` (+ `store.ts`); HTTP via `@/shared/api/client`. | Extended with new calls/DTOs. |

## Mapper output contract

New abstraction in `src/backend/Aircane.Application/Characters/Import/`:

```csharp
public enum CharacterImportSource
{
    Unknown, DndBeyondApi, DndBeyondCompanion, PathbuilderTwo,
    FoundryDnd5e, FoundryPf2e, Roll20, GenericVtt
}

public enum ImportConfidence { High, Low }

public interface ICharacterSourceMapper
{
    CharacterImportSource Source { get; }

    /// <summary>
    /// Detection precedence. Lower runs first. GenericVtt uses int.MaxValue so it is always the
    /// last resort regardless of DI registration order (carried-forward resolution). Specific
    /// mappers use 0.
    /// </summary>
    int Order => 0;

    /// <summary>Cheap structural check: does this JSON look like my source format? Never throws.</summary>
    bool CanMap(JsonDocument doc);

    /// <summary>
    /// Builds a CanonicalCharacter draft from the source JSON. Never throws on missing/null/
    /// wrong-type input: unresolved values are simply left at their CanonicalCharacter defaults
    /// and surfaced via ExtraFields/RequiresReviewPaths.
    /// </summary>
    SourceMapResult Map(JsonDocument doc);
}
```

```csharp
public sealed record SourceMapResult
{
    /// <summary>The mapped draft, in the SAME shape PdfCharacterExtractor produces.</summary>
    public required CanonicalCharacter Character { get; init; }

    /// <summary>
    /// The authoritative set of ApplyMapping paths this mapper ACTUALLY populated, with their
    /// stringified values — e.g. {"abilities.strength":"16", "combat.maxHitPoints":"42"}. This is
    /// the ONLY source of MappedFields in the review payload. It is NOT derived by flattening
    /// Character, because CanonicalCharacter's non-zero defaults (abilities=10, AC=10, speed=30,
    /// proficiencyBonus=2) are indistinguishable from mapped values under a flatten and would seed
    /// the review form with phantom values a low-confidence source never provided (finding 2).
    /// A mapper adds a key here ONLY when it read a usable value from the source JSON; a field it
    /// could not resolve is omitted entirely (so numeric combat/ability paths default to omitted,
    /// not "0"/"10"/"30" — finding 3). Builders use the helper CanonicalCharacterPaths to keep the
    /// path spellings exactly aligned with the ApplyMapping switch.
    /// </summary>
    public required IReadOnlyDictionary<string, string> MappedFields { get; init; }

    /// <summary>
    /// Values the mapper extracted but that have NO ApplyMapping path (PF2e saves, class DC,
    /// perception, heritage, lores, spells, equipment, features…). Surfaced in the review UI as
    /// UnmappedFieldDto so the user can act on them; NEVER passed to ApplyMapping (which would
    /// throw "Unknown canonical field path"). Key = human/source label, Value = stringified value.
    /// </summary>
    public IReadOnlyDictionary<string, string> ExtraFields { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Subset of MappedFields keys whose value the user must review even though it mapped cleanly —
    /// e.g. every Roll20/Generic best-effort value, a DDB summed score outside 1..30, or a HP value
    /// the mapper only DERIVED (e.g. set combat.hitPoints = combat.maxHitPoints as a guess because
    /// the source gave only max). Every path here must also appear in MappedFields. The controller
    /// echoes these into the review payload's per-field RequiresReview set (finding 3).
    /// </summary>
    public IReadOnlyCollection<string> RequiresReviewPaths { get; init; } = [];

    public ImportConfidence Confidence { get; init; } = ImportConfidence.High;

    /// <summary>"2014"/"2024" for D&D 5e; null when the source does not imply a ruleset.</summary>
    public string? Ruleset { get; init; }
    public bool RulesetRequiresConfirmation { get; init; }

    /// <summary>
    /// Forced game-system IDENTIFIER (not display name): "dnd-5e-2014" or
    /// "pathfinder-2e-remaster" when the source implies one; null for Roll20/Generic.
    /// </summary>
    public string? GameSystemIdentifier { get; init; }

    public IReadOnlyCollection<string> Warnings { get; init; } = [];
}
```

`CanonicalCharacter` is the existing `Aircane.Application/Characters/CanonicalCharacter.cs` type
(`Identity{ Name, RaceOrAncestry, Background, Alignment }`, `Classes[] { ClassName, Level, Subclass,
HitDie }`, `Abilities{ Strength..Charisma }`, `Combat{ ArmorClass, Speed, MaxHitPoints,
CurrentHitPoints, TemporaryHitPoints, Initiative, ProficiencyBonus }`, `Notes`). Mappers set the
fields they can, leave the rest at defaults, and push everything else into `ExtraFields`.

### How a mapped draft becomes the review payload (no `MapImportedFields`)

The controller, for the adapter path, mirrors the PDF path precisely:

1. `var sr = mapper.Map(doc);`
2. Persist a draft `Character` from `sr.Character` via a persist helper (below) → `characterId`.
3. Build the review DTO:
   - `MappedFields` = **`sr.MappedFields` verbatim** — the exact `ApplyMapping` path → stringified
     value set the mapper reports it actually populated. The controller does **not** flatten
     `sr.Character` (a flatten cannot distinguish a mapped value from a non-zero constructor default
     — finding 2). The mapper is the single authority for which paths were set; the controller only
     echoes the dictionary. `CanonicalCharacterPaths` (new helper, in
     `Aircane.Application/Characters/Import/`) exists only to give mappers the canonical path-string
     constants and the schema-id↔path bridge table — it is NOT used to flatten defaults.
   - `UnmappedFields` = one `UnmappedFieldDto` per entry in `sr.ExtraFields`
     (`SourceFieldName`=label, `SourceValue`=value, `SuggestedCanonicalField`=null unless a safe
     suggestion exists, `Confidence`=0, `RequiresReview`=true).
   - `RequiresReview` per-field flags from `sr.RequiresReviewPaths` (every entry is also a
     `MappedFields` key).
   - source/confidence/ruleset signals from `sr`.
4. Return the new `SourceImportResponse` envelope wrapping that `CharacterFieldReviewDto`.

**Consistency invariant (controller-owned, asserted in integration tests):** every key in
`sr.MappedFields` must be a path the save-time `ApplyMapping` switch accepts, and every value must be
one `ApplyMapping` can parse for that path. Because the controller does not call `ApplyMapping`
itself (that happens later on the user's `applyFieldMappings` confirm), this invariant is proven by
the integration test described in the test plan (finding 1), not inside the controller.

On confirm, the frontend submits the (possibly user-edited) `MappedFields` via the existing
`applyFieldMappings` (`PUT /api/characters/{id}/field-mappings`), which runs `ApplyMapping` with its
server-side validation and serializes the final `CanonicalJson`. **No new persistence/validation
code path is introduced.**

### Draft persistence helper (finding 5)

`PersistDraftFromExtractionAsync` is PDF-specific (takes a `PdfCharacterExtractionResult` and
contains the "Unknown level-1 fallback that discards mapped data"). The adapter path must NOT
silently discard mapped data, so the design adds a sibling helper on the controller:

```csharp
private async Task<CharacterDto> PersistDraftFromCanonicalAsync(
    CanonicalCharacter mapped, string gameSystem, string ruleset,
    Guid? campaignId, string? originalFileName, CancellationToken ct)
```

It serializes `mapped` with `CharacterJsonSerializer.Serialize(mapped)` (the type gap is gone —
`mapped` is a real `CanonicalCharacter`), derives `Name`/`Level` the same way the PDF helper does,
and calls `CreateCharacterAsync`. On `ArgumentException` it does **not** fall back to an "Unknown"
stub; instead it persists a draft carrying the partial `CanonicalJson` as-is (name defaulted to the
file name or "Imported Character", level clamped to ≥1) and adds a warning to the review payload, so
the review UI always shows the mapped data. This is the documented, intentional difference from the
PDF helper.

## Format detection (precedence via `Order`, carried-forward resolution)

```csharp
public sealed class CharacterFormatDetector(IEnumerable<ICharacterSourceMapper> mappers)
{
    private readonly IReadOnlyList<ICharacterSourceMapper> _ordered =
        mappers.OrderBy(m => m.Order).ToList();   // deterministic, not DI order

    public (CharacterImportSource Source, ICharacterSourceMapper? Mapper) Detect(JsonDocument doc)
    {
        foreach (var mapper in _ordered)
            if (SafeCanMap(mapper, doc)) return (mapper.Source, mapper);
        return (CharacterImportSource.Unknown, null);
    }

    private static bool SafeCanMap(ICharacterSourceMapper m, JsonDocument doc)
    {
        try { return m.CanMap(doc); } catch { return false; } // untrusted JSON must never crash detection
    }
}
```

Precedence is owned by `Order`, not DI registration order: every specific mapper is `Order = 0`
(and their `CanMap` signatures are mutually exclusive by construction — see each task), and
`GenericVttMapper.Order = int.MaxValue` so it is always the last resort. If even Generic cannot
find a name, detection returns `Unknown`. `CharacterFormatDetectorTests` asserts Generic loses to
every specific mapper under a shuffled registration list.

## Endpoint design (contract-preserving extension)

### `POST /api/characters/import` (extended) — binding reconciliation (finding 1)

The real action is `ImportCharacter([FromBody] ImportCharacterJsonRequest request, …)` with hard
`400` guards when `CanonicalJson`/`GameSystem`/`Ruleset` are missing. A raw Pathbuilder/Foundry/
Roll20 upload has none of those. The design resolves this with **option (a) from the review**:
the adapter path reuses the existing envelope and carries the raw source JSON in `CanonicalJson`.

Concrete signature (unchanged binding; new optional inputs are query params + one new optional DTO
member):

```csharp
[HttpPost("import")]
public async Task<IActionResult> ImportCharacter(
    [FromBody] ImportCharacterJsonRequest request,
    [FromQuery] string? source,                    // optional CharacterImportSource (case-insensitive)
    [FromQuery] Guid? gameSystemDefinitionId,      // optional; also addable to the DTO
    CancellationToken cancellationToken)
```

Guard order (preserves every legacy 400 for legacy callers):

1. `if (string.IsNullOrWhiteSpace(request.CanonicalJson)) → 400 "canonicalJson is required."`
   (unchanged — the raw source text still lives in `CanonicalJson`, so this guard still passes for
   adapter callers and still fails for truly empty bodies).
2. Decide the path:
   - **Legacy path** when `source` is absent AND `request.GameSystem`/`request.Ruleset` are both
     present. Behavior is byte-for-byte the current behavior: run the two existing
     `GameSystem`/`Ruleset` 400 guards, then `ImportCharacterFromJsonAsync`, returning the current
     201/400/422. (This is the exact current branch, unmoved.)
   - **Adapter path** when `source` is present, OR when `GameSystem`/`Ruleset` are absent. In this
     branch the `GameSystem`/`Ruleset` guards do **not** apply (an adapter upload legitimately has
     neither). Before parsing, enforce a **2 MB size cap** on `request.CanonicalJson`
     (`Encoding.UTF8.GetByteCount(request.CanonicalJson) > 2 * 1024 * 1024`) → controller returns
     `400 ProblemDetails { Title = "Payload too large", Detail = "Character JSON exceeds the 2 MB
     limit.", Status = 400 }`. Then parse `request.CanonicalJson` into a `JsonDocument` inside a
     `try/catch (JsonException)`; on failure the **controller itself** returns
     `400 ProblemDetails { Title = "Malformed JSON", Detail = "The uploaded character JSON could not
     be parsed.", Status = 400 }`. (The adapter path never calls `ImportCharacterFromJsonAsync`, so
     it cannot reuse that service's "Invalid JSON" result — the controller constructs these two
     ProblemDetails directly, and the integration + frontend tests assert these exact Title/Detail
     strings — finding 5.) The raw JSON body is never echoed into the error (untrusted-content
     steering).
3. Resolve mapper: **if `source` is given, pick the registered mapper with that `Source` and skip
   detection entirely** (invalid enum → 400 "Unknown import source."); else
   `CharacterFormatDetector.Detect(doc)`. (Pinning by `source` is what makes the Roll20/Generic
   second call deterministic — finding 4.)
4. If `Unknown`/no mapper → `200 SourceImportResponse { RequiresSourceConfirmation=true,
   Confidence="low", Review = empty-but-valid CharacterFieldReviewDto }` so the frontend prompts for
   source selection.
5. Resolve the game system (below). **If the resolved mapper implies no system (Roll20/Generic) AND
   no `gameSystemDefinitionId` was supplied → short-circuit with `RequiresGameSystemSelection=true`
   and persist nothing** (finding 4). Otherwise (forced-system mapper, or a `gameSystemDefinitionId`
   was supplied) run `mapper.Map(doc)`, persist draft against the resolved definition, build the
   review DTO, and return `200 SourceImportResponse` with `RequiresGameSystemSelection=false`.

This keeps one wire contract (`ImportCharacterJsonRequest`) and never introduces a second body
binding. Legacy callers (which always send `source`-less canonical JSON with `gameSystem`+`ruleset`)
hit the legacy branch and see identical behavior; the integration suite asserts this (regression
guard).

Game-system resolution for the adapter path:
- `PathbuilderTwo`/`FoundryPf2e` → force identifier `pathfinder-2e-remaster`
  (`Pathfinder2eRemasterSeed.DefinitionId = 10000000-0000-0000-0000-000000000003`).
- `DndBeyondApi`/`DndBeyondCompanion`/`FoundryDnd5e` → force identifier `dnd-5e-2014`
  (`DnD5e2014Seed.DefinitionId = 10000000-0000-0000-0000-000000000001`).
- Resolve the definition id via `ISystemRegistry.ListAsync()` filtered by `Identifier` (or use the
  well-known `DefinitionId` constants directly — the design uses `ListAsync()` lookup so a
  renamed/reseeded id still resolves, and 400s "The required built-in game system is not installed."
  if absent).
- `Roll20`/`GenericVtt` imply no system. When one of these is the resolved mapper and no
  `gameSystemDefinitionId` was supplied, return `200 SourceImportResponse {
  RequiresSourceConfirmation=true, Review.RequiresGameSystemSelection=true,
  DetectedSource=<Roll20|GenericVtt> }` and do NOT persist a draft yet. (The draft
  persist/`CreateCharacterAsync` needs a concrete `GameSystem`/`Ruleset` string, so a system is
  required before persistence — this two-call handshake is intentional and documented in the
  frontend section and Risks.) `GameSystem`/`Ruleset` strings for the draft come from the chosen
  definition's `Name`/a ruleset derived from `sr.Ruleset` (defaulting "2014"/"Remaster").

**Second-call handshake rules (finding 4 — pin the mapper, don't re-detect):** when the frontend
re-POSTs after the user picks a system, it MUST include BOTH the originally returned
`?source=<DetectedSource>` (so the mapper is resolved by explicit source and detection is NOT re-run)
AND the chosen `gameSystemDefinitionId` (query param or DTO member). The endpoint applies two
explicit rules so the second call is deterministic:
  1. **`?source` present ⇒ skip `CharacterFormatDetector.Detect` entirely** and resolve the mapper
     by that enum. This guarantees the second call maps with the SAME mapper the first call
     reported, even for a Generic/Roll20 sheet that could marginally match more than one `CanMap`.
  2. **`gameSystemDefinitionId` present ⇒ the `RequiresGameSystemSelection` short-circuit is
     suppressed**: the resolved mapper runs, the draft is persisted against that definition, and a
     normal `SourceImportResponse` (with `RequiresGameSystemSelection=false`) is returned. A supplied
     `gameSystemDefinitionId` is what distinguishes "second call, user has chosen" from "first call,
     no system yet"; a mapper with no forced system AND no supplied id is the only case that
     short-circuits.
These two rules are added to the guard order below (steps 3 and 5) and mirrored in the frontend
section.

### `POST /api/characters/import/dndbeyond-url` (new, same controller)

- Body: `record DndBeyondUrlImportRequest(string CharacterUrl, Guid? GameSystemDefinitionId, Guid? CampaignId)`.
- Inherits `[Authorize(Policy = AuthorizationPolicies.DmOrHost)]` from the controller.
- Delegates to `IDndBeyondUrlImportService.FetchAsync(url, ct)` → raw DDB JSON string; the
  controller runs `DndBeyondApiMapper` + the shared persist/review helper against the forced
  `dnd-5e-2014` system.
- **Never store or log the character URL** (steering). The service logs only the parsed numeric id
  at Debug and the HTTP status code; the URL string is never logged or persisted. No response
  caching.
- Returns the same `SourceImportResponse` envelope.

Rate-limiting (finding 3): the global limiter already applies the `"api"` policy to every endpoint,
and `"api"` is a deliberate **no-op on LAN** (gated on `ITunnelStateService.IsInternetModeActive`),
throttling only in internet/tunnel mode. So an explicit `[EnableRateLimiting("api")]` here would be
redundant and would NOT protect the LAN (the product's primary mode). The design therefore:
- Does **not** add a redundant `[EnableRateLimiting]` attribute; it documents that `"api"` throttles
  this endpoint only in internet mode (same as every other endpoint), via the global limiter.
- Adds a cheap **in-service runaway guard** in `DndBeyondUrlImportService`: a process-wide minimum
  interval (`SemaphoreSlim(1,1)` single-flight + a short `TimeSpan` min-gap, e.g. 2s, between
  outbound calls) so a buggy frontend loop cannot hammer the unofficial DDB service on LAN. The
  guard is independent of tunnel state. The `HttpClient` timeout (10s) bounds each call.

New response envelope (returned 200 for both extended-import and dndbeyond-url when a draft needs
review):

```csharp
public sealed record SourceImportResponse(
    CharacterImportSource DetectedSource,
    string Confidence,                 // "high" | "low"
    bool RequiresSourceConfirmation,   // true when Unknown or low confidence
    string? Ruleset,                   // "2014"/"2024"/null
    bool RulesetRequiresConfirmation,
    CharacterFieldReviewDto Review);   // the EXISTING review DTO, extended below
```

## DTO changes (findings 6, 7)

`CharacterFieldReviewDto` (and `UnmappedFieldDto`) get new, **defaulted** members so the existing
PDF path serializes unchanged. Because the type is a positional `record`, the new members are added
as init-only properties with defaults rather than extending the positional parameter list (keeps the
PDF-path constructor call site compiling unchanged):

```csharp
public sealed record UnmappedFieldDto(
    string SourceFieldName,
    string SourceValue,
    string? SuggestedCanonicalField,
    float Confidence)
{
    /// <summary>Force the review UI to require explicit confirmation of this field.</summary>
    public bool RequiresReview { get; init; } = false;
}

public sealed record CharacterFieldReviewDto(
    Guid CharacterId,
    bool ReviewRequired,
    IReadOnlyList<UnmappedFieldDto> UnmappedFields,
    IReadOnlyList<string> Warnings)
{
    // ── New, all defaulted so the PDF path is unaffected. These are the review-SCOPED signals
    //    only; the source/confidence/ruleset ENVELOPE signals live on SourceImportResponse, NOT
    //    here, to avoid two sources of truth (finding 7). ──

    /// <summary>Game-system definition id the review form should render against (finding 4).</summary>
    public Guid? GameSystemDefinitionId { get; init; }

    /// <summary>ApplyMapping path → stringified value, to seed the review form (findings 2, 6).</summary>
    public IReadOnlyDictionary<string, string> MappedFields { get; init; }
        = new Dictionary<string, string>();

    /// <summary>True for Roll20/Generic when the user must pick a system before mapping (finding 4).</summary>
    public bool RequiresGameSystemSelection { get; init; }
}
```

`DetectedSource`, `Confidence`, `RequiresSourceConfirmation`, `Ruleset`, and
`RulesetRequiresConfirmation` are deliberately **NOT** on `CharacterFieldReviewDto`; they live only
on the `SourceImportResponse` envelope (finding 7). The frontend binds the source/confidence/ruleset
badges and the ruleset dropdown from `response.*`, and binds the form id / seeded values / system-
selection prompt from `response.review.*`. The PDF path returns a bare `CharacterFieldReviewDto`
(no envelope) and simply leaves all these new members at their defaults.

`ImportCharacterJsonRequest` gains `Guid? GameSystemDefinitionId = null` (backward-compatible
positional-with-default addition; the frontend TS interface mirrors it).

## Review step — FormDescriptor load path (finding 4)

The review panel renders the bound system's form via the already-existing endpoint
**`POST /api/game-systems/{gameSystemDefinitionId}/preview-character-form`** (verified in
`GameSystemsController.PreviewCharacterForm`; it is a **POST** keyed on the definition **Guid**,
body `PreviewCharacterFormRequest?`, returns `FormDescriptor`; `null` schema returns an empty
descriptor). The frontend adds/reuses an api method in `features/game-systems/api.ts`:
`previewCharacterForm(gameSystemDefinitionId: string): Promise<FormDescriptor>` →
`apiClient.post(\`/api/game-systems/${id}/preview-character-form\`, {})`. The panel obtains
`gameSystemDefinitionId` from the import response (`response.review.gameSystemDefinitionId`).

Note the rendering nuance: `CharacterSchemaRenderer` renders **schema-field-ids** (`str`, `hp_max`),
while the save flow consumes **`ApplyMapping` paths** (`abilities.strength`, `combat.maxHitPoints`).
Because this feature persists via `ApplyMapping` (rail #2), the review panel's **source of truth for
values is `review.mappedFields` (ApplyMapping paths)**, and the FormDescriptor is used for
labels/sections/field types only. The panel maps schema-field-id ↔ ApplyMapping-path with a small
static table local to the panel (name↔identity.name, str↔abilities.strength, ac↔combat.armorClass,
hp_max↔combat.maxHitPoints, hp_current↔combat.hitPoints, class↔class, level↔level, race↔
identity.raceOrAncestry, background↔identity.background). Fields with no `ApplyMapping` path (PF2e
saves, perception, class DC) render read-only from `unmappedFields` and are not submitted via
`applyFieldMappings`. This table is the single documented bridge between the two vocabularies and is
unit-tested for 1:1 consistency with `ApplyMapping`'s supported paths.

## Per-source mapping specifications

All mappers live in `Aircane.Application/Characters/Import/`, parse defensively (`TryGetProperty` +
`ValueKind` checks only; never assume types; never throw), and emit `ApplyMapping`-path values
through the `CanonicalCharacter` plus `ExtraFields` for everything else.

**Standing rule for every mapper (findings 2, 3):** whenever a mapper reads a usable value from the
source and writes it onto the `CanonicalCharacter`, it also adds that path→value to
`sr.MappedFields`. A field the source did not provide is left at its `CanonicalCharacter` default
AND omitted from `MappedFields` (so defaulted abilities=10/AC=10/speed=30/HP=0 never appear in the
review form as if imported). When a mapper DERIVES a value rather than reading it — specifically when
it sets `CurrentHitPoints = MaxHitPoints` because the source gave only max — it adds
`combat.hitPoints` to BOTH `MappedFields` and `RequiresReviewPaths`. The per-source notes below say
which paths each mapper sets; everything not listed is omitted when absent.

### Task 1 — `PathbuilderTwoMapper` (Source=PathbuilderTwo, Order=0)
Detection: root has `build`; `build` has `class` AND `ancestry`; root has NO `system` (excludes
Foundry). Mapping into `CanonicalCharacter`: `build.name→Identity.Name`; `build.class→Classes[0].
ClassName`; `build.level→Classes[0].Level`; `build.ancestry→Identity.RaceOrAncestry`;
`build.background→Identity.Background`; `build.abilities.str/dex/con/int/wis/cha→Abilities.*`;
`build.attributes.hp→Combat.MaxHitPoints` and `Combat.CurrentHitPoints=max` (derived — add
`combat.hitPoints` to `RequiresReviewPaths`); `build.attributes.speed→Combat.Speed`;
`build.attributes.ac→Combat.ArmorClass`. Each path above is added to `MappedFields` only when the
source actually carried the value; absent fields are omitted (finding 3). Into `ExtraFields`
(no ApplyMapping path): `build.heritage` ("Heritage"), `build.attributes.classDC`, `perception`,
`fortitude`/`reflex`/`will`, each `build.lores[]`, PF2e proficiency ranks converted to a bonus via
`rank*2+level` (Untrained=0, Trained=2+L, Expert=4+L, Master=6+L, Legendary=8+L) as e.g.
"Athletics: +7", `build.feats[]` (joined, "Feats"), `build.spellCasters[]` summarized per caster
("Arcane spells: …"; tradition inferred from caster name), `build.equipment[]` (joined). Forced
`GameSystemIdentifier="pathfinder-2e-remaster"`, `Ruleset="Remaster"`. Confidence High.

### Task 2 — `DndBeyondApiMapper` (Source=DndBeyondApi, Order=0) + `DndBeyondUrlImportService`
Detection (DDB API JSON saved as file): root has `id`, `dateModified`, `classes`, `stats`, `race`.
Mapping: `name→Identity.Name`; `race.fullName→Identity.RaceOrAncestry` (append
`race.subRaceShortName` into `ExtraFields` as "Subrace"); `sum(classes[].level)→Classes[0].Level`;
`classes[0].definition.name→Classes[0].ClassName`; `classes[0].subclassDefinition.name→Classes[0].
Subclass`; `stats[0..5].value` **plus all bonus layers** (`bonusStats`, `overrideStats`, racial/feat
modifiers) summed to a final score → `Abilities.*`; **if a summed score is <1 or >30, keep it but
add its `abilities.<name>` path to `RequiresReviewPaths`** (and let `ApplyMapping`'s 1..30 range
guard catch it at save if still out of range — so a bad value cannot silently persist);
`hitPointInfo` → `Combat.MaxHitPoints = base+con`, `CurrentHitPoints = max - removedHitPoints`,
`TemporaryHitPoints = temporaryHitPoints`; `armorClass.totalArmorClass` (or `overrideArmorClass`) →
`Combat.ArmorClass`. `spells[]`/`inventory[]` summarized into `ExtraFields`. Forced
`GameSystemIdentifier="dnd-5e-2014"`. `DetectRuleset` (below) sets `Ruleset` + always
`RulesetRequiresConfirmation=true`. Confidence High.

`DndBeyondUrlImportService` (`Aircane.Infrastructure/Characters/`): parse id from a full
`dndbeyond.com/characters/{id}` URL or a bare numeric id (first run of digits in the `/characters/`
segment; reject when none → typed exception → 400). Typed `HttpClient` (named "DndBeyond")
registered via `services.AddHttpClient<IDndBeyondUrlImportService, DndBeyondUrlImportService>` with
`BaseAddress=https://character-service.dndbeyond.com/`, `Timeout=10s`, default header
`User-Agent: Aircane-Tabletop/1.0`. GET `character/v5/character/{id}`. Status mapping → typed
`DndBeyondImportException(StatusKind)`; controller maps to the brief's exact messages and statuses
(one status each):
- 403 → **422** "This character is private. Make your character sheet public on D&D Beyond to import it."
- 404 → **422** "Character not found. Check the URL and try again."
- timeout/`TaskCanceled` → **422** "D&D Beyond is not responding. Try again later."
- other 5xx/network → **422** "D&D Beyond import failed. Use the PDF export option instead."
- URL parse failure → **400** "Enter a valid D&D Beyond character URL or ID."
(422 chosen uniformly to match the existing import endpoints' 422 convention for
user-correctable/no-retry cases.)

### Task 3 — `DndBeyondCompanionMapper` (Source=DndBeyondCompanion, Order=0)
Detection: root has `character` with `name`+`classes` AND (root has `_meta` [DDB-Importer] OR root
has `ddbId` [Companion]). Reuses `DndBeyondApiMapper`'s field logic against a `character.`-prefixed
view: the mapper extracts `root.character` as the working element and delegates to a shared internal
`DndBeyondApiMapper.MapFromRoot(JsonElement ddbRoot)` so both share one code path. Same ruleset
handling, forced `dnd-5e-2014`.

### Task 4 — `FoundryDnd5eMapper` (Source=FoundryDnd5e, Order=0)
Detection: `root.type=="character"` AND `root.system` has `abilities` AND `system.attributes.hp`
exists AND NOT pf2e (`system.details.ancestry` absent). Mapping: `name→Identity.Name`;
`system.details.race→Identity.RaceOrAncestry` (subtype → `ExtraFields` "Subrace");
`system.details.background→Identity.Background`; level = `system.details.level` or
`sum(items[type=class].system.levels)` → `Classes[0].Level`; `items[type=class].name→Classes[0].
ClassName`; `items[type=subclass].name→Classes[0].Subclass`; `system.abilities.*.value→Abilities.*`;
`system.attributes.hp.value/max/temp→Combat.CurrentHitPoints/MaxHitPoints/TemporaryHitPoints`;
`system.attributes.ac.value→Combat.ArmorClass`; `system.attributes.movement.walk→Combat.Speed`.
`items[type=spell|feat|weapon|equipment|tool|consumable|loot]` and `system.skills.{key}.total`
(18 keys) summarized into `ExtraFields`. Forced `GameSystemIdentifier="dnd-5e-2014"`. `DetectRuleset`
(below). Confidence High.

### Task 5 — `FoundryPf2eMapper` (Source=FoundryPf2e, Order=0)
Detection: `root.type=="character"` AND `system.details.ancestry` present. Mapping:
`name→Identity.Name`; `system.details.ancestry.name→Identity.RaceOrAncestry`
(heritage.name → `ExtraFields` "Heritage"); `system.details.background.name→Identity.Background`;
`system.details.level.value→Classes[0].Level`; `system.details.class.name→Classes[0].ClassName`;
`system.abilities.*.value (modifiers)→Abilities.*` (note PF2e stores modifiers; the PF2e seed schema
also treats these as modifiers, so this is consistent); `system.attributes.hp.value/max→Combat.
CurrentHitPoints/MaxHitPoints`; `system.attributes.ac.value→Combat.ArmorClass`;
`system.attributes.speed.value→Combat.Speed`. `classDC`, `saves.fortitude/reflex/will`, `items[action
|feat|spell|weapon|armor|equipment]` → `ExtraFields`. Forced `pathfinder-2e-remaster`/"Remaster".
Confidence High.

### Task 6 — `Roll20Mapper` (Source=Roll20, Order=0), best-effort
Detection: root has `schema_version` AND `root.character` has `attribs`. `attribs` is a flat array
of `{name,current,max}`. Map by attrib name (D&D 5e Roll20 sheet): `character_name→Identity.Name`;
`race→Identity.RaceOrAncestry`; `class→Classes[0].ClassName`; `level→Classes[0].Level`;
`strength/dexterity/constitution/intelligence/wisdom/charisma→Abilities.*`; `hp→Combat.
CurrentHitPoints`; `hp_max→Combat.MaxHitPoints`; `ac→Combat.ArmorClass`; `speed→Combat.Speed`.
Every mapped path is added to `RequiresReviewPaths`; any missing attrib leaves the default and is
noted. **Always `Confidence=Low`.** No forced game system (`GameSystemIdentifier=null`) → triggers
the Roll20 game-system-selection handshake.

### Task 7 — `GenericVttMapper` (Source=GenericVtt, Order=int.MaxValue), fallback
Heuristic name fields (`name`/`character_name`/`charname`/`Name`) and ability-score paths
(`str`/`strength`/`STR`/`abilities.str`/`stats.str`…). `CanMap` returns true if any name OR ability
heuristic matches. Everything mapped goes into `RequiresReviewPaths`; `Confidence=Low`; adds a
warning "We couldn't identify this character sheet format. Please review and correct all fields
before saving." No forced game system.

### Task 8 — `DndBeyondPdfHints` + `PdfCharacterExtractor` override
`DndBeyondPdfHints.cs` (Application): `static IReadOnlyDictionary<string,string> FieldMap` from DDB
PDF form-field names to **`ApplyMapping` paths** (so it fits the PDF path's existing `MapToCanonical`
→ `ApplyMapping`-aligned vocabulary): `CharacterName→identity.name`, `ClassLevel→` (special,
post-split), `Race→identity.raceOrAncestry`, `Background→identity.background`,
`STR/DEX/CON/INT/WIS/CHA→abilities.strength…charisma`, `HPMax→combat.maxHitPoints`,
`HPCurrent→combat.hitPoints`, `AC→combat.armorClass`, `Speed→combat.speed`,
`ProfBonus→combat.proficiencyBonus`, 18 skills→`ExtraFields`-style notes (no ApplyMapping path). In
`PdfCharacterExtractor`, consult `DndBeyondPdfHints.FieldMap` as a **priority override** before the
existing heuristic `TryMapField`: when a form-field name matches a key, use the mapped path. Post-
parse: if `ClassLevel` matches `^(.+?)\s+(\d+)$` split into class name + level. OCR/text fallback
untouched. (This is the one place the PDF path and the adapter path share a vocabulary; the hints
map to `ApplyMapping` paths, consistent with rail #2.)

### Ruleset detection — `DndBeyondSources.cs` (Application)
`static readonly HashSet<int> Ruleset2024SourceIds = { 672, 673, 674 };` (comment: list may be
extended). `DndBeyondApiMapper.DetectRuleset(JsonElement)`: scan `classes[].definition.sources[].
sourceId`; any in the set → "2024" else "2014"; always set `Ruleset` and
`RulesetRequiresConfirmation=true`. `FoundryDnd5eMapper.DetectRuleset(JsonElement)`: primary
`root._stats.systemVersion` (`Version.TryParse` ≥ 3.0.0 → "2024" else "2014"); fallback
`system.details.source.book` referencing a known 2024 publication; default "2014" and
`RulesetRequiresConfirmation=true` when `systemVersion` absent.

## Backend files to CREATE / MODIFY (grouped by layer, in implementation order)

### Application — `src/backend/Aircane.Application/Characters/Import/`
CREATE: `CharacterImportSource.cs` (enum); `ImportConfidence` (in `SourceMapResult.cs`);
`ICharacterSourceMapper.cs`; `SourceMapResult.cs`; `CanonicalCharacterPaths.cs` — exposes
`static IReadOnlySet<string> Supported` (the exact lowercased `ApplyMapping` path vocabulary:
`identity.name`/`name`, `identity.raceorancestry`/`race`, `identity.background`/`background`,
`identity.alignment`/`alignment`, `class`, `level`, `abilities.strength`…`abilities.charisma` (+bare
`strength`…`charisma`), `combat.armorclass`/`armorclass`, `combat.hitpoints`/`combat.currenthitpoints`/
`hitpoints`, `combat.maxhitpoints`/`maxhitpoints`, `combat.speed`/`speed`, `combat.initiative`/
`initiative`, `combat.proficiencybonus`/`proficiencybonus`, `identity.experiencepoints`/
`experiencepoints`, `passiveperception`) plus the canonical path-string constants mappers use and
the schema-id↔path bridge table. It does **NOT** flatten a `CanonicalCharacter` (removed — finding 2;
`MappedFields` is owned by each mapper). The `Supported` set is the thing the integration guard
iterates; `CharacterFormatDetector.cs`;
`PathbuilderTwoMapper.cs` (T1); `DndBeyondApiMapper.cs` + `DndBeyondSources.cs` (T2);
`DndBeyondCompanionMapper.cs` (T3); `FoundryDnd5eMapper.cs` (T4); `FoundryPf2eMapper.cs` (T5);
`Roll20Mapper.cs` (T6); `GenericVttMapper.cs` (T7); `DndBeyondPdfHints.cs` (T8).

### Application abstractions — `src/backend/Aircane.Application/Abstractions/`
CREATE: `IDndBeyondUrlImportService.cs` — `Task<string> FetchAsync(string characterUrl,
CancellationToken ct)`; `DndBeyondImportException : Exception` with a `StatusKind` enum
(Private403/NotFound404/Timeout/Upstream/InvalidUrl).

### DTOs — `src/backend/Aircane.Application/DTOs/Characters/`
CREATE: `SourceImportResponse.cs`; `DndBeyondUrlImportRequest.cs`.
MODIFY: `CharacterFieldReviewDto.cs` (add defaulted members above); `ImportCharacterJsonRequest.cs`
(add `Guid? GameSystemDefinitionId = null`).

### Infrastructure — `src/backend/Aircane.Infrastructure/`
CREATE: `Characters/DndBeyondUrlImportService.cs` (T2) — typed HttpClient; URL id parse; status
mapping; single-flight + min-interval runaway guard; no caching; no URL logging.
MODIFY: `Characters/PdfCharacterExtractor.cs` (T8) — hint override + ClassLevel split;
`DependencyInjection.cs` — register each mapper as `ICharacterSourceMapper` (order is irrelevant now,
`Order` governs precedence), `CharacterFormatDetector`, `AddHttpClient<IDndBeyondUrlImportService,
DndBeyondUrlImportService>(…)`. (Mappers are Application types registered in Infrastructure's DI,
consistent with existing `CharacterSchemaValidator` registration.)

### API — `src/backend/Aircane.Api/Controllers/`
MODIFY: `CharactersController.cs` — inject `CharacterFormatDetector`,
`IEnumerable<ICharacterSourceMapper>` (to resolve by explicit `source`), `IDndBeyondUrlImportService`.
Extend `ImportCharacter` with the `?source`/`gameSystemDefinitionId` branch per the guard order
above (legacy branch unmoved). Add `ImportCharacterFromDndBeyondUrl`. Add private helpers
`PersistDraftFromCanonicalAsync` and `BuildSourceReview(SourceMapResult, Guid characterId, Guid
gameSystemDefinitionId)`.

### Solution / build
No new projects; SDK-style globbing picks up new files. `build.ps1`/`build.sh` and `.sln` unchanged
except the WireMock package reference added to the integration test project (below).

## Frontend files to CREATE / MODIFY

### `features/characters/`
MODIFY `components/ImportCharacterModal.vue` (T9): tabs **Upload File | D&D Beyond URL | PDF**.
Upload File: `.json` picker → POST `/api/characters/import` carrying the raw JSON in `canonicalJson`
with `gameSystem`/`ruleset` left blank and `?source` only when the user overrides auto-detect; show
detected-source badge; on `requiresSourceConfirmation` show the source dropdown (Auto-detect,
Pathbuilder 2e, D&D Beyond (File), Foundry VTT (D&D 5e), Foundry VTT (PF2e), Roll20, Generic VTT);
on `requiresGameSystemSelection` show a game-system picker and re-POST with the chosen
`gameSystemDefinitionId`. D&D Beyond URL: URL input (placeholder
`https://www.dndbeyond.com/characters/...`), help text linking
`https://www.dndbeyond.com/account/sharing`, disclaimer "Uses an unofficial API — may not always
work. Use PDF export as fallback.", Import → POST `/api/characters/import/dndbeyond-url`. PDF tab
unchanged. Badge colors: PathbuilderTwo orange, DndBeyond* red, Foundry* purple, Roll20 blue,
GenericVtt gray, Unknown amber. Match existing Tailwind token style.

MODIFY `api.ts` + `store.ts`: add `importCharacterFromSource(request, { source?,
gameSystemDefinitionId? })` and `importCharacterFromDndBeyondUrl({ characterUrl,
gameSystemDefinitionId?, campaignId? })`; add TS types `SourceImportResponse`
(`detectedSource, confidence, requiresSourceConfirmation, ruleset, rulesetRequiresConfirmation,
review`), extended `CharacterFieldReviewDto` (review-scoped only: `gameSystemDefinitionId,
mappedFields, requiresGameSystemSelection`) and `UnmappedFieldDto.requiresReview`; add
`gameSystemDefinitionId?` to `ImportCharacterJsonRequest`. The source/confidence/ruleset badges and
the ruleset dropdown read from `response.*`; the form id, seeded `mappedFields`, and the
system-selection prompt read from `response.review.*` (finding 7).

CREATE `components/ImportReviewPanel.vue`: loads the FormDescriptor via the game-systems api method
`previewCharacterForm(gameSystemDefinitionId)`; seeds values from `review.mappedFields` (ApplyMapping
paths) using the schema-id↔path bridge table; highlights `requiresReview`/unmapped fields; shows the
read-only "Imported from: [badge]" header. The panel receives the whole `SourceImportResponse`, not
just the nested review DTO, so it binds the form id and seeded values from `response.review.*`
(`gameSystemDefinitionId`, `mappedFields`, `requiresGameSystemSelection`) and the source/ruleset
badges from `response.*` (finding 7). For D&D 5e it renders an editable 2014/2024 ruleset dropdown
pre-populated with **`response.ruleset`**, amber-highlighted with the confirm tooltip when
**`response.rulesetRequiresConfirmation`**; on confirm submits via the existing `applyFieldMappings`.

### `features/game-systems/`
MODIFY `api.ts`: add `previewCharacterForm(gameSystemDefinitionId: string): Promise<FormDescriptor>`
→ `POST /api/game-systems/{id}/preview-character-form` with an empty body.

## Error handling (concrete, per operation)

| Operation | Failure | Recoverable? | Caller receives | Logged |
|---|---|---|---|---|
| `canonicalJson` empty | missing body | fatal (request) | 400 "canonicalJson is required." (unchanged) | — |
| Adapter body size | > 2 MB | fatal | 400 `ProblemDetails{ Title="Payload too large", Detail="Character JSON exceeds the 2 MB limit.", Status=400 }` (controller-constructed) | Warning, no body echo |
| Parse adapter body JSON | malformed | fatal | 400 `ProblemDetails{ Title="Malformed JSON", Detail="The uploaded character JSON could not be parsed.", Status=400 }` (controller-constructed, NOT the service's "Invalid JSON") | Warning, no body echo |
| `source` query invalid enum | bad value | fatal | 400 "Unknown import source." | Warning |
| `Detect` finds nothing | Unknown | recoverable | 200 `SourceImportResponse{RequiresSourceConfirmation=true, Confidence="low"}` | Info |
| Resolve schema | Roll20/Generic + no `gameSystemDefinitionId` | recoverable | 200 `Review.RequiresGameSystemSelection=true` (no draft persisted) | Info |
| Resolve schema | built-in system not installed | fatal | 400 "The required built-in game system is not installed." | Warning |
| Resolve schema | explicit id not found | fatal | 400 "Unknown game system." | Warning |
| `mapper.CanMap`/`Map` | missing/null/wrong-type JSON | **never throws** | value left default; surfaced via `ExtraFields`/`RequiresReviewPaths` | Debug |
| Persist draft (`PersistDraftFromCanonicalAsync`) | `CreateCharacterAsync` `ArgumentException` | recoverable | draft persisted with partial CanonicalJson (NO Unknown stub); warning added | Warning |
| DDB fetch | 403 | fatal | 422 "This character is private. Make your character sheet public on D&D Beyond to import it." | Warning (status + id only) |
| DDB fetch | 404 | fatal | 422 "Character not found. Check the URL and try again." | Warning |
| DDB fetch | timeout | fatal | 422 "D&D Beyond is not responding. Try again later." | Warning |
| DDB fetch | other 5xx/network | fatal | 422 "D&D Beyond import failed. Use the PDF export option instead." | Warning |
| DDB URL parse | no numeric id | fatal | 400 "Enter a valid D&D Beyond character URL or ID." | Warning (no URL) |
| DDB summed ability score out of 1..30 | — | review | value kept, path forced into `RequiresReview`; `ApplyMapping` range guard rejects at save if still bad | Debug |

## Input validation (per external input)

- `canonicalJson` (adapter path) — required (non-empty); parsed with `JsonDocument`; size cap 2 MB
  (explicit guard; reject larger → 400). Treated as untrusted.
- `source` query — optional; must parse to `CharacterImportSource` (case-insensitive); else 400.
- `gameSystemDefinitionId` — optional Guid; when present must resolve via `ISystemRegistry`.
- `characterUrl` — required, max length 2048; accept full URL or bare numeric id; first digit run in
  the `/characters/` segment; reject if none.
- All mapper JSON access via `TryGetProperty`/`ValueKind`; numeric reads via `TryGetInt32`/
  `TryGetDouble`; arrays via `ValueKind==Array` before `EnumerateArray`.
- Final persisted values are range-validated by the existing `ApplyMapping`/`ParseIntMapping` on
  save (abilities 1..30, AC 0..99, HP 0..9999, speed 0..999, etc.) — a mapper cannot persist an
  out-of-range value through the save flow.

## Invariant ownership

- **Server-side auth on every endpoint** — API layer (`[Authorize(DmOrHost)]` on the controller;
  new/extended actions inherit it). Verified.
- **Server-side value validation** — owned by the existing `CharacterService.ApplyMapping`/
  `ParseIntMapping` on the save call (`applyFieldMappings`); mappers do not get a bypass.
- **Importers/AI never mutate campaign state directly** — mappers return data only; persistence is
  the existing draft `Character` + user-confirmed `ApplyFieldMappings` path.
- **Canonical serialization** — owned by `CharacterJsonSerializer` via the existing create/save
  helpers; mappers produce `CanonicalCharacter`, never serialize.
- **No secret/URL logging; no caching** — owned by `DndBeyondUrlImportService`.
- **Detection precedence** — owned by `ICharacterSourceMapper.Order` + `CharacterFormatDetector`
  (not DI order).
- **Ruleset finalization** — detected by the mappers, confirmed by the user in the review UI.

## Test plan

### Unit tests — `tests/backend/Aircane.UnitTests/Characters/Import/` (new folder), xUnit
One `<Type>Tests` class per mapper + detector + helpers, `Method_Scenario_Expectation` naming,
`[Fact]`/`[Theory]`:
- `PathbuilderTwoMapperTests` — detection incl. `system` exclusion; ability/HP/AC/speed mapping;
  rank→bonus (`rank*2+level`) in `ExtraFields`; PF2e-only data in `ExtraFields` (asserts NO PF2e
  save/classDC path is set on the `CanonicalCharacter`); forced identifier/ruleset.
- `DndBeyondApiMapperTests` — stat summing across bonus layers; out-of-range score forced into
  `RequiresReviewPaths`; `DetectRuleset` by sourceId; HP/AC math.
- `DndBeyondCompanionMapperTests` — `_meta`/`ddbId` detection; `character.`-prefixed delegation.
- `FoundryDnd5eMapperTests` — detection; `_stats.systemVersion` → ruleset (v3+→2024, v2→2014,
  absent→2014 + `RulesetRequiresConfirmation`); class/subclass from items; HP/AC/speed.
- `FoundryPf2eMapperTests` — ancestry detection; modifier mapping; forced PF2e.
- `Roll20MapperTests` — flat attrib mapping; missing attrib → review; always Low confidence; no
  forced system.
- `GenericVttMapperTests` — name/ability heuristics; all-fields review; Low confidence; warning text.
- `CharacterFormatDetectorTests` — **precedence by `Order` under a shuffled registration list**
  (Generic loses to every specific mapper); `CanMap`-throws is swallowed; Unknown fallback.
- `DndBeyondUrlParseTests` — full URL, bare id, trailing path, non-numeric → InvalidUrl.
- `DndBeyondPdfHintsTests` + extend `PdfCharacterExtractorTests` — hint override beats heuristic;
  `ClassLevel` "Fighter 5" split.
- `CanonicalCharacterPathsTests` — pure unit test, asserts **only** that the schema-id↔ApplyMapping-
  path bridge table is 1:1 (no duplicate schema id, no duplicate path) and that every path it names
  is a member of the hard-coded `CanonicalCharacterPaths.Supported` constant set. This test does
  **not** try to invoke `ApplyMapping` — that switch is `private static` on `CharacterService` and
  is only reachable through `ApplyFieldMappingsAsync` (DbContext-bound), so "a path is actually
  accepted by the live switch" is proven in the integration suite instead (finding 1). The bridge
  table covers **only paths that have a corresponding schema field id** rendered by the
  FormDescriptor (name, str/dex/con/int/wis/cha, ac, hp_max, hp_current, class, level, race,
  background); `experiencePoints`, `passivePerception`, `initiative`, and `proficiencyBonus` have no
  schema field id (they route to `Notes` or are derived) and are explicitly EXCLUDED from the 1:1
  assertion so it does not falsely fail against the full `ApplyMapping` path set (finding 6).
- `MapperMappedFieldsTests` — for each mapper, asserts `sr.MappedFields` contains **only** paths the
  fixture actually provided a value for, and that a fixture missing AC/speed/abilities/HP produces a
  `MappedFields` with **no** `combat.armorClass`/`combat.speed`/`abilities.*`/`combat.*HitPoints`
  key (guards the finding-2/3 "no phantom defaults" contract), and that every `RequiresReviewPaths`
  entry is also a `MappedFields` key.

**Fixture loading:** samples under `tests/backend/Aircane.UnitTests/Characters/Import/Fixtures/`
(`pathbuilder2e.json`, `ddb-v5.json`, `ddb-companion.json`, `foundry-dnd5e.json`,
`foundry-pf2e.json`, `roll20.json`, `generic-vtt.json`). Loaded via
`File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Characters", "Import", "Fixtures", name))`
with the files marked `CopyToOutputDirectory=PreserveNewest` via a new `ItemGroup` in
`Aircane.UnitTests.csproj` for `Characters\Import\Fixtures\**\*.json` (matches how existing tests
read inputs from the output directory). No new NuGet needed for unit tests.

### Integration tests — `tests/backend/Aircane.IntegrationTests/` via `AircaneWebApplicationFactory`
- `CharacterSourceImportIntegrationTests.cs` — POST `/api/characters/import` with a Pathbuilder and a
  Foundry fixture (raw JSON in `canonicalJson`, no `source`): assert `SourceImportResponse`
  (detected source, confidence, `review.gameSystemDefinitionId`, `review.mappedFields`, review
  payload). POST a **legacy** canonical-JSON body WITH `gameSystem`+`ruleset` and no `source`: assert
  the current 201/422 behavior is unchanged (regression guard for the preserved legacy contract).
  POST a Roll20 fixture with **no** `source`/`gameSystemDefinitionId`: assert
  `review.requiresGameSystemSelection=true`, `requiresSourceConfirmation=true`, and that NO character
  row was persisted. Re-POST the SAME Roll20 body with BOTH `?source=Roll20` AND a
  `gameSystemDefinitionId`: assert detection was pinned (still Roll20), a draft IS persisted, and
  `review.mappedFields` is returned with `review.requiresGameSystemSelection=false` (finding 4). The
  malformed-JSON and >2 MB adapter bodies assert the exact ProblemDetails `Title`/`Detail`/`Status`
  from the error table (finding 5).
- `ApplyMappingPathAcceptanceTests.cs` (integration) — **the finding-1 correctness guard, option
  (a).** For each path in `CanonicalCharacterPaths.Supported`: create a draft character via the
  normal create/import flow, then `PUT /api/characters/{id}/field-mappings` with a single mapping
  `{ canonicalFieldPath = <path>, value = <a representative valid value for that path> }` and assert
  the response is NOT a 4xx carrying "Unknown canonical field path" (i.e. the live `ApplyMapping`
  switch accepts every path a mapper can emit). This drives the real private `ApplyMapping` through
  its only public caller (`ApplyFieldMappingsAsync`), so the "mappers emit exactly the paths the
  save rail accepts" claim is proven against the live switch, not a hand-copied list. A companion
  negative case asserts a deliberately bogus path (`"not.a.real.path"`) IS rejected with that
  message, pinning the guard's sensitivity. Round-trip: after applying, GET the character and assert
  the value landed in the expected `CanonicalJson` location.
- `DndBeyondUrlImportIntegrationTests.cs` — **WireMock.Net** local server; a derived factory
  overrides the "DndBeyond" typed client `BaseAddress` to the WireMock URL via `ConfigureServices`.
  Cases: 200 → review payload; 403/404/timeout/5xx → the exact messages + 422; invalid URL → 400.
  Assert the character URL string never appears in captured logs (log capture via a test
  `ILoggerProvider`).

**WireMock.Net:** add `<PackageReference Include="WireMock.Net" Version="1.6.11" />` to
`Aircane.IntegrationTests.csproj` (exact pin, per tech steering — finding 7).

### Frontend tests — `src/frontend/aircane-web` (vitest)
- `ImportCharacterModal` — tab switching, badge rendering per source, source + game-system
  confirmation dropdowns, DDB URL submit path (api client mocked).
- `ImportReviewPanel` — seeds the renderer from `mappedFields` via the bridge table, read-only
  "Imported from" header, D&D 5e ruleset dropdown + amber confirm highlight.

## Risks / open ambiguities

1. **Two-vocabulary bridge.** The review renderer speaks schema-field-ids while persistence speaks
   `ApplyMapping` paths. The design pins a small, unit-tested bridge table as the single authority.
   If a built-in schema adds fields with no `ApplyMapping` path, those render read-only and are not
   submitted — acceptable (same limitation the PDF path already has).
2. **PF2e-only data (saves, class DC, perception, lores, spellcasting) has no `ApplyMapping` path.**
   It is surfaced in the review UI via `ExtraFields`/`UnmappedFieldDto` and is NOT persisted into
   typed fields in v1 (persisting it would require either extending `ApplyMapping` + the PF2e seed
   schema or a Notes dump). The design routes PF2e extras into `Notes` on confirm only if the user
   maps them; otherwise they remain review-only. Confirm this scoping is acceptable.
3. **Roll20/Generic two-call handshake** (no implied system) — deliberate. The second call is now
   deterministic: it pins the mapper via `?source` (no re-detection) and a supplied
   `gameSystemDefinitionId` suppresses the selection short-circuit (finding 4). Remaining product
   choice: this two-call flow vs. forcing an up-front system pick in the modal — confirm.
4. **Unofficial DDB endpoint** may change/break; UI disclaimer + PDF fallback are the mitigation; no
   caching. The DDB v5 field paths in the brief are external and verified only against a captured
   fixture at implementation time.
5. **Built-in system presence.** Forcing `dnd-5e-2014`/`pathfinder-2e-remaster` requires those seeds
   to be installed; the endpoint returns a clear 400 when absent. (Deterministic `DefinitionId`
   constants exist on the seeds as a fallback resolution path.)

## Responses to review findings — iteration-2 `design-review.json` (the review this revision answers)

- **1 (HIGH) — correctness guard can't invoke the private `ApplyMapping` as a unit test:** RESOLVED
  via the review's option (a). `ApplyMapping` is `private static` on `CharacterService`, reachable
  only through `ApplyFieldMappingsAsync` (DbContext-bound). The "every supported path is accepted by
  the live switch" assertion moves to a new **integration** test `ApplyMappingPathAcceptanceTests`
  that, for each path in `CanonicalCharacterPaths.Supported`, drives `PUT /field-mappings` and
  asserts no "Unknown canonical field path" rejection (plus a negative case that a bogus path IS
  rejected, and a round-trip assert). The pure unit `CanonicalCharacterPathsTests` keeps ONLY the
  schema-id↔path bridge-table 1:1 check and asserts its paths ⊆ `Supported` — it no longer pretends
  to call `ApplyMapping`.
- **2 (HIGH) — flattening `CanonicalCharacter` leaks non-zero defaults into `MappedFields`:**
  RESOLVED. `CanonicalCharacterPaths.Flatten` is removed. `SourceMapResult` now carries a required
  `MappedFields` dictionary that each mapper populates **only** for paths it genuinely read from the
  source; the controller echoes it verbatim. Defaulted abilities=10/AC=10/speed=30/HP=0 are omitted,
  not seeded. `MapperMappedFieldsTests` guards "no phantom defaults".
- **3 (MEDIUM) — defaulted/derived HP persists silently as 0:** RESOLVED. The standing mapper rule
  omits any numeric combat/ability path that was not read, and any mapper that DERIVES
  `CurrentHitPoints = MaxHitPoints` adds `combat.hitPoints` to `RequiresReviewPaths` (stated in the
  Pathbuilder spec and the standing rule). A source that supplies neither HP value emits neither
  path, so nothing persists as "0 HP" without review.
- **4 (MEDIUM) — two-call handshake could re-detect a different mapper:** RESOLVED. The second
  Roll20/Generic POST MUST carry the originally detected `?source` (pins the mapper, skips
  re-detection) AND the chosen `gameSystemDefinitionId`; a supplied `gameSystemDefinitionId`
  suppresses the `RequiresGameSystemSelection` short-circuit and proceeds to persist. Added to the
  endpoint guard order (steps 3 and 5), the handshake rules block, and the frontend section.
- **5 (MEDIUM) — adapter malformed-JSON / size-cap 400s lacked a concrete body:** RESOLVED. The
  controller constructs explicit `ProblemDetails` for both: `Title="Malformed JSON"` /
  `Detail="The uploaded character JSON could not be parsed."` and `Title="Payload too large"` /
  `Detail="Character JSON exceeds the 2 MB limit."`, both `Status=400`. Named in the error table and
  input-validation section; integration + frontend tests assert the exact strings.
- **6 (NIT) — bridge table omits `experiencePoints`/`passivePerception`:** RESOLVED. The bridge
  table and its 1:1 unit assertion are explicitly scoped to paths with a schema field id;
  `experiencePoints`, `passivePerception`, `initiative`, `proficiencyBonus` (no schema field id,
  route to Notes/derived) are excluded from the assertion. Stated in the test plan and the helper
  description.
- **7 (NIT) — signals duplicated on envelope and review DTO:** RESOLVED. `DetectedSource`,
  `Confidence`, `RequiresSourceConfirmation`, `Ruleset`, `RulesetRequiresConfirmation` live ONLY on
  `SourceImportResponse`; `CharacterFieldReviewDto` keeps only `GameSystemDefinitionId`,
  `MappedFields`, `RequiresGameSystemSelection`. The frontend section documents which object each UI
  element binds from.

### Carried forward from the iteration-1 review (unchanged, still in effect)
The iteration-1 resolutions remain part of the design: adapter path reuses `ImportCharacterJsonRequest`
(raw JSON in `CanonicalJson`) with legacy guards only on the legacy branch; `MapImportedFields` is
not used (mappers speak the `ApplyMapping` vocabulary); the `"api"` rate-limit is a documented LAN
no-op backed by an in-service single-flight + min-interval guard; FormDescriptor loads via
`POST /api/game-systems/{id}/preview-character-form`; `PersistDraftFromCanonicalAsync` avoids the
Unknown-stub fallback; `ICharacterSourceMapper.Order` (Generic = `int.MaxValue`) governs detection
precedence independent of DI order; all DDB fetch failures return 422 with the brief's exact
messages and URL-parse failure returns 400; WireMock.Net pinned to `1.6.11`.
