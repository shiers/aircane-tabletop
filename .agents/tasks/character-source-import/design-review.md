# Design Review — Character Source Import Adapters (iteration 3)

Design under review: `design-character-source-import.md`
Repository: `d:\Development\aircane-tabletop\.worktrees\character-source-import`
Reviewer stance: fresh read, every factual claim checked against real source.

## Summary

This is a mature, iteration-3 design that has already resolved most of the hard reconciliation
problems. I independently verified the repository against every load-bearing claim: the real
import endpoint, the real DTOs it extends, the `ApplyMapping` path vocabulary, the canonical
defaults, DI registration, the game-system seeds and resolution path, the FormDescriptor endpoint,
the rate-limit model, the auth policy, and the frontend targets. The design does NOT invent a
parallel `CharacterImportDraft` type, does NOT fabricate `/import/json`, funnels through the real
`CharacterService.ApplyMapping`/`ApplyFieldMappingsAsync` save rail, and targets the real
`ImportCharacterModal.vue`. The vast majority of its factual assertions are accurate.

Findings below. Two are MEDIUM (a real behavior-contract change on the legacy branch, and a
concrete gap in the integration-test construction against the real `FieldMappingEntry` shape). The
rest are NITs.

---

## Findings

### 1. MEDIUM — Legacy-branch selection changes an existing 400 contract for partial legacy requests

**Where:** "Endpoint design → guard order", step 2 ("Decide the path").

The design routes to the **legacy path** only "when `source` is absent AND `request.GameSystem`/
`request.Ruleset` are both present", and to the **adapter path** "when `source` is present, OR when
`GameSystem`/`Ruleset` are absent."

I verified the real `ImportCharacter` (CharactersController.cs): today a request with a non-empty
`CanonicalJson` and `GameSystem` but a **missing `Ruleset`** returns a deterministic
`400 ProblemDetails "ruleset is required."` (and symmetrically for a missing `GameSystem`). Under
the proposed routing, that same partial legacy request (`source` absent, `Ruleset` absent) now falls
into the **adapter** branch, which skips the gameSystem/ruleset guards, parses `CanonicalJson` as a
source format, and most likely returns `200 SourceImportResponse { RequiresSourceConfirmation=true }`
or an "Unknown import source"/detection path — NOT the current `400 "ruleset is required."`.

So the "legacy contract preserved" claim holds only for *fully-formed* legacy requests. A legacy
caller that omits exactly one of gameSystem/ruleset sees a changed response (400 → 200/other). This
is an observable contract change and the regression integration test as described (which posts a
*complete* legacy body) would not catch it.

**Concrete fix:** make the branch decision explicit and preserve the partial-legacy 400s. For
example: treat the request as **legacy** whenever `source` is absent AND (`GameSystem` is present OR
`Ruleset` is present) — i.e. any sign of legacy intent keeps the legacy guards, so a partial legacy
body still gets its "ruleset is required."/"gameSystem is required." 400. Route to the **adapter**
branch only when `source` is present OR BOTH `GameSystem` AND `Ruleset` are absent. Add an
integration case asserting that `{ canonicalJson, gameSystem, (no ruleset) }` with no `source` still
returns `400 "ruleset is required."`.

### 2. MEDIUM — `ApplyMappingPathAcceptanceTests` payload does not match the real `FieldMappingEntry` shape

**Where:** Test plan → `ApplyMappingPathAcceptanceTests.cs`, and the frontend confirm flow.

The design describes driving `PUT /api/characters/{id}/field-mappings` with "a single mapping
`{ canonicalFieldPath = <path>, value = <value> }`". The real contract
(`ApplyFieldMappingsRequest.cs`) is:

```csharp
public sealed record FieldMappingEntry(string SourceFieldName, string CanonicalFieldPath, string Value);
public sealed record ApplyFieldMappingsRequest(IReadOnlyList<FieldMappingEntry> Mappings);
```

`SourceFieldName` is a **required positional parameter**, not optional. A mapping object with only
`canonicalFieldPath` + `value` will not construct. The design's test (and, by extension, the
frontend `applyFieldMappings` submission that seeds from `mappedFields`) must supply a
`SourceFieldName` for every entry. This matters beyond the test: the frontend confirm flow builds
`FieldMappingEntry[]` from `review.mappedFields` (ApplyMapping path → value), and the design never
says what `SourceFieldName` those synthesized entries carry.

Also note `ApplyFieldMappingsAsync` **skips** any entry whose `CanonicalFieldPath` is blank (it does
not validate `SourceFieldName`), so `SourceFieldName` can be any non-null string — but it must be
present and the design must say so.

**Concrete fix:** In the test plan, specify the exact entry shape:
`new FieldMappingEntry(SourceFieldName: <path>, CanonicalFieldPath: <path>, Value: <representative value>)`
(reusing the path as the source-field label is fine). In the frontend section, state that each
synthesized `FieldMappingEntry` sets `SourceFieldName` to the mapped path (or the source label) so
the submission is well-formed.

### 3. NIT — "every value `ApplyMapping` can parse" invariant must respect `ParseIntMapping` range limits

**Where:** "Consistency invariant (controller-owned)" and the `ApplyMappingPathAcceptanceTests`
"representative valid value for that path".

I verified `ParseIntMapping`: abilities `1..30`, armorClass `0..99`, hitpoints/maxhitpoints
`0..9999`, speed `0..999`, initiative `-20..20`, proficiencyBonus `0..10`, and `level` must be `>= 1`.
The acceptance test's "a representative valid value for that path" is correct in spirit but must pick
per-path in-range values (e.g. a flat `"50"` would fail `abilities.*` [max 30] and `initiative`
[max 20]). This is implied but not spelled out, and getting it wrong would make the guard fail for
reasons unrelated to path acceptance.

**Concrete fix:** Give the acceptance test a per-path representative-value table (ability=15, AC=15,
hp=20, maxhp=20, speed=30, initiative=2, proficiencyBonus=2, level=1, string paths="x") so each PUT
exercises path acceptance without tripping a range error that would mask the real assertion.

### 4. NIT — `ParseIntMapping` strips a trailing `t`/`f`/`.`; mapper numeric output should be clean

**Where:** per-source mapper specs (numeric fields) and input-validation section.

`ParseIntMapping` cleans values by splitting on space/`(` and then `TrimEnd('f','t','.')` (to handle
"30 ft."). A mapper that stringifies a numeric read as, say, `"30ft"` or a value ending in a
stripped character could be silently altered at save. The mappers read numerics via
`TryGetInt32`/`TryGetDouble`, so in practice they emit clean integer strings — but the design should
state the contract that `MappedFields` values for numeric paths are bare integer strings (no units),
so they survive `ParseIntMapping` unchanged.

**Concrete fix:** Add one line to the standing mapper rule: "Numeric `MappedFields` values are
emitted as bare base-10 integer strings (no units/sign suffixes) so `ParseIntMapping` passes them
through unmodified."

### 5. NIT — PDF-path `PersistDraftFromExtractionAsync` Unknown-stub claim is accurate; sibling helper naming is the only risk

**Where:** "Draft persistence helper (finding 5)".

Verified: the real `PersistDraftFromExtractionAsync` does fall back to a minimal `Unknown`/level-1
character on `ArgumentException`, discarding the mapped `CanonicalJson` — exactly as the design
states. The new sibling `PersistDraftFromCanonicalAsync` is a sound, non-destructive alternative.
No correctness problem; just confirm the new helper does not accidentally reuse the PDF helper's
fallback branch. This is a NIT only because the design already calls the difference out explicitly.

---

## Verified Assumptions (checked against source)

1. **No `CharacterImportDraft` is invented.** Mapper output is a new `SourceMapResult` wrapping the
   REAL `CanonicalCharacter` (`Aircane.Application/Characters/CanonicalCharacter.cs`). Confirmed the
   type exists with `Identity`, `Classes[]`, `Abilities`, `Combat`, `Notes` as described.
2. **Real endpoint is `POST /api/characters/import`**, action `ImportCharacter([FromBody]
   ImportCharacterJsonRequest, …)`, returning `201 CharacterDto` / `400 ProblemDetails` /
   `422 CharacterImportResult`. Confirmed in CharactersController.cs — the design correctly uses this,
   not a fabricated `/import/json`.
3. **`CharacterImportResult`** is the real 422 type (`DTOs/Characters/CharacterImportResult.cs`,
   `record (bool Success, CharacterDto? Character, IReadOnlyList<string> Errors)`). The design maps
   the brief's non-existent draft onto this + `CharacterFieldReviewDto` correctly.
4. **`CharacterFieldReviewDto` + `UnmappedFieldDto`** are positional records
   (`DTOs/Characters/CharacterFieldReviewDto.cs`). The design's plan to add defaulted init-only
   members (`GameSystemDefinitionId`, `MappedFields`, `RequiresGameSystemSelection`,
   `UnmappedFieldDto.RequiresReview`) is compatible and keeps the PDF-path constructor call site
   compiling. Confirmed the PDF path constructs this DTO positionally.
5. **`ApplyMapping` path vocabulary** matches the design's list exactly (CharacterService.cs):
   `identity.name`/`name`, `identity.raceorancestry`/`race`, `identity.background`/`background`,
   `identity.alignment`/`alignment`, `identity.experiencepoints`→Notes, `class`, `level`,
   `abilities.*`/bare, `combat.armorclass`, `combat.hitpoints`/`combat.currenthitpoints`,
   `combat.maxhitpoints`, `combat.speed`, `combat.initiative`, `combat.proficiencybonus`,
   `passiveperception`→Notes. `ApplyMapping` is indeed `private static`, reachable only via
   `ApplyFieldMappingsAsync` — the design's rationale for moving the acceptance guard to integration
   is correct.
6. **`CanonicalCharacter` non-zero defaults** are real: `AbilityScores` all `= 10`,
   `CombatStats.ArmorClass = 10`, `Speed = 30`, `ProficiencyBonus = 2`, HP default `0`. This
   justifies the "no flatten / mapper owns `MappedFields`" decision (finding-2 fix) accurately.
7. **The existing save flow validates server-side.** `ApplyMapping`/`ParseIntMapping` range-check
   and throw `ArgumentException`; the controller maps that to 400. `CreateCharacterAsync` runs
   `CharacterSchemaValidator`. The "importers never mutate state without server validation" steering
   is satisfied by existing code. Confirmed.
8. **DI:** mappers are Application types registered in Infrastructure's `DependencyInjection.cs`,
   consistent with the existing `services.AddScoped<CharacterSchemaValidator>()` and
   `AddScoped<ICharacterService, CharacterService>()`. `ITunnelStateService` is a singleton.
   `services.AddHttpClient(...)` is already used (typed/named clients present).
9. **Game-system resolution:** `DnD5e2014Seed.DefinitionId = 10000000-…-0001`, identifier
   `dnd-5e-2014`; `Pathfinder2eRemasterSeed.DefinitionId = 10000000-…-0003`, identifier
   `pathfinder-2e-remaster`. `ISystemRegistry.ListAsync()` returns `GameSystemDefinitionSummary`
   which exposes `Identifier`, `Name`, `Id` — so resolving a definition id by identifier is feasible
   exactly as described.
10. **FormDescriptor endpoint:** `POST /api/game-systems/{id:guid}/preview-character-form`
    (GameSystemsController.cs) binds `[FromBody] PreviewCharacterFormRequest?`, returns
    `FormDescriptor`, and returns an empty descriptor (`Sections = []`) when `CharacterSchema` is
    null. The design's load path and empty-body call are accurate.
11. **Rate limiting:** the `"api"` policy is the global limiter and is a genuine LAN no-op gated on
    `ITunnelStateService.IsInternetModeActive` (RateLimitingExtensions.cs). The design's decision to
    NOT add a redundant `[EnableRateLimiting("api")]` and to add an in-service single-flight +
    min-interval guard is well-founded. Named policies (`join`, `feedback`) exist as described.
12. **Auth:** `CharactersController` carries `[Authorize(Policy = AuthorizationPolicies.DmOrHost)]`;
    `DmOrHost` and `Authenticated` constants exist. New actions inherit `DmOrHost` as claimed.
13. **Frontend targets:** `ImportCharacterModal.vue`, `features/characters/api.ts` (with
    `importCharacterFromJson`, `ImportCharacterJsonRequest`, `applyFieldMappings`,
    `CANONICAL_FIELD_OPTIONS`), and `store.ts` all exist at the stated paths. The frontend
    `CANONICAL_FIELD_OPTIONS` list matches the `ApplyMapping` path vocabulary 1:1, validating the
    bridge-table premise.
14. **`ImportCharacterJsonRequest`** is a positional record with trailing optional parameters, so
    adding `Guid? GameSystemDefinitionId = null` as another trailing default is backward-compatible.
    Confirmed.
15. **PDF Unknown-stub fallback** really exists in `PersistDraftFromExtractionAsync` (discards mapped
    data), as the design's finding-5 motivation states.

## Unverified / Wrong Assumptions

- **`FieldMappingEntry.SourceFieldName` is required (finding 2).** The design's description of the
  `applyFieldMappings` payload omits this required field. Not wrong about the endpoint, but
  incomplete about the request shape — must be corrected before the test/frontend code is written.
- **Legacy-branch routing preserves ALL legacy 400s (finding 1).** Verified FALSE for partial legacy
  requests (missing exactly one of gameSystem/ruleset); the proposed routing changes that response.
- **External/unverified (acknowledged by the design, not a finding):** the D&D Beyond v5 field paths
  (`stats[]`, `hitPointInfo`, `armorClass.totalArmorClass`, bonus layers), the Pathbuilder/Foundry/
  Roll20 JSON shapes, the 2024-source-id set `{672,673,674}`, Foundry `_stats.systemVersion`
  semantics, and the WireMock.Net `1.6.11` version pin are all external and cannot be verified from
  this repo. The design correctly flags these as "verified against a captured fixture at
  implementation time" and routes them through defensive `TryGetProperty`/`ValueKind` parsing. No
  finding raised, but implementation must validate against real captured samples.
- **`CharacterSchemaRenderer.vue` schema-field-id ↔ ApplyMapping-path bridge table** correctness was
  not exhaustively verified against the renderer's actual field ids (only the backend `ApplyMapping`
  paths and the frontend `CANONICAL_FIELD_OPTIONS` were confirmed to align). The design unit-tests
  this bridge, which is the right mitigation; flagged here as not independently confirmed.

---

## Verdict

HIGH findings: 0. MEDIUM findings: 2 (findings 1 and 2). NIT findings: 3.

Because there are MEDIUM findings, the verdict is **CHANGES_REQUESTED**. Both MEDIUMs are small,
localized fixes (branch-selection rule + test/frontend request shape) and should resolve in one
quick loop-back.
