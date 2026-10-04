# Implementation Plan — Character Source Import Adapters

Worktree (ABSOLUTE paths): `d:\Development\aircane-tabletop\.worktrees\character-source-import`
Git: `git -C d:\Development\aircane-tabletop\.worktrees\character-source-import ...`
Design note: `.agents/tasks/character-source-import/design-character-source-import.md`
Design review findings folded in below as named, early work items.

## Verified build / test commands (run from the worktree root)
- Backend build: `dotnet build src/backend/Aircane.sln`
- Backend unit tests: `dotnet test tests/backend/Aircane.UnitTests/Aircane.UnitTests.csproj`
- Backend integration tests: `dotnet test tests/backend/Aircane.IntegrationTests/Aircane.IntegrationTests.csproj`
- Frontend build/typecheck: `npm run build` (cwd `src/frontend/aircane-web`)
- Frontend unit tests: `npx vitest run` (cwd `src/frontend/aircane-web`)

## Verified codebase facts (grounding)
- `CharacterService.ApplyMapping` is `private static`, reachable only via `ApplyFieldMappingsAsync` (DbContext-bound). Switch accepts the exact lowercased path set in the design; `ParseIntMapping` ranges: abilities 1..30, AC 0..99, hp/maxhp 0..9999, speed 0..999, initiative -20..20, proficiencyBonus 0..10; level `>= 1`. `ParseIntMapping` splits on space/'(' and `TrimEnd('f','t','.')`.
- `FieldMappingEntry(string SourceFieldName, string CanonicalFieldPath, string Value)` — SourceFieldName is a REQUIRED positional param (confirms MEDIUM-2). `ApplyFieldMappingsAsync` skips blank `CanonicalFieldPath`, never validates `SourceFieldName`.
- `CanonicalCharacter` defaults: abilities=10, AC=10, Speed=30, ProficiencyBonus=2, HP=0 (confirms the finding-2 "no phantom defaults" rationale).
- `CharactersController.ImportCharacter([FromBody] ImportCharacterJsonRequest, CancellationToken)` has three hard 400 guards: canonicalJson / gameSystem / ruleset. `PersistDraftFromExtractionAsync` DOES fall back to an Unknown/level-1 stub on `ArgumentException` (confirms NIT-5 — the new helper must NOT do this).
- Seed ids: `DnD5e2014Seed.DefinitionId = 10000000-...-0001`; `Pathfinder2eRemasterSeed.DefinitionId = ...0003`. `ISystemRegistry.ListAsync()` returns summaries with `Identifier` + `Id`.
- Infrastructure DI = `src/backend/Aircane.Infrastructure/DependencyInjection.cs` (`AddInfrastructure`). Named HttpClients registered via `AddHttpClient(...)` already present.
- `PdfCharacterExtractor.MapToCanonical` loops fields calling `TryMapField(key,value,...)` — hint override inserts before `TryMapField`.
- UnitTests csproj references Domain/Application/Infrastructure/Api/Workers. IntegrationTests csproj references Api + Workers, uses `AircaneWebApplicationFactory` + EF InMemory; WireMock.Net is NOT yet referenced (must add, pinned 1.6.11).
- Frontend: `src/frontend/aircane-web/src/features/characters/components/ImportCharacterModal.vue`, `.../characters/api.ts`, `.../characters/store.ts`, `.../game-systems/api.ts` all exist.

---

## FINDINGS FOLDED IN FIRST (named, before/with the work that depends on them)

- [ ] F1. MEDIUM-1 — legacy endpoint contract preserved (routing rule).
      Implemented inside the endpoint item (item 10). Routing: treat as LEGACY whenever `source` is absent AND (GameSystem present OR Ruleset present) — so `{ canonicalJson, gameSystem, no ruleset, no source }` still returns 400 "ruleset is required.". Route to ADAPTER only when `source` present OR BOTH GameSystem AND Ruleset absent.
      Verify (item 12): integration test asserts `{ canonicalJson, gameSystem, no ruleset, no source }` → 400 "ruleset is required.".

- [ ] F2. MEDIUM-2 — FieldMappingEntry full shape.
      Every synthesized/test entry constructs `new FieldMappingEntry(SourceFieldName: <mapped path or source label>, CanonicalFieldPath: <path>, Value: <representative in-range value>)`. Frontend sets `SourceFieldName` to the mapped path or source label. Applies to item 12 (ApplyMapping acceptance test) and item 13 (frontend confirm flow).

- [ ] F3. NIT-3 — range-safe per-path representative values in the ApplyMapping acceptance test.
      Table: ability=15, AC=15, hp=20, maxhp=20, speed=30, initiative=2, proficiencyBonus=2, level=1, string paths="x". Applies to item 12.

- [ ] F4. NIT-4 — numeric MappedFields values emitted as bare base-10 integer strings (no units/suffixes like " ft.") so `ParseIntMapping` passes them unchanged. Standing rule for every mapper (items 2,4,5,6,7,8,9).

- [ ] F5. NIT-5 — `PersistDraftFromCanonicalAsync` persists the partial CanonicalJson as-is with a warning and NEVER builds an Unknown/level-1 stub (unlike the PDF helper). Implemented in item 10.

---

## BACKEND — abstraction & first mapper (validates the rail)

- [ ] 1. Create the import abstraction types in `src/backend/Aircane.Application/Characters/Import/`.
      Files: `CharacterImportSource.cs` (enum: Unknown, DndBeyondApi, DndBeyondCompanion, PathbuilderTwo, FoundryDnd5e, FoundryPf2e, Roll20, GenericVtt), `SourceMapResult.cs` (record incl. `ImportConfidence` enum, `MappedFields` required dict, `ExtraFields`, `RequiresReviewPaths`, `Confidence`, `Ruleset`, `RulesetRequiresConfirmation`, `GameSystemIdentifier`, `Warnings`), `ICharacterSourceMapper.cs` (`Source`, `int Order => 0`, `bool CanMap(JsonDocument)`, `SourceMapResult Map(JsonDocument)`), `CanonicalCharacterPaths.cs` (`static IReadOnlySet<string> Supported` = exact lowercased ApplyMapping vocabulary; canonical path-string constants; schema-id↔path bridge table — NO flatten), `CharacterFormatDetector.cs` (orders by `Order`, `SafeCanMap` swallows exceptions, Unknown fallback). Design: "Mapper output contract", "Format detection".
      Verify: `dotnet build src/backend/Aircane.sln` succeeds.

- [ ] 2. Create `PathbuilderTwoMapper.cs` (Source=PathbuilderTwo, Order=0) in the Import folder.
      Detection: root has `build`; `build` has `class` AND `ancestry`; root has NO `system`. Map name/class/level/ancestry/background/abilities/hp(+derived currentHP → RequiresReviewPaths)/speed/ac into CanonicalCharacter and MappedFields (only when source carried the value; bare-int strings per F4). ExtraFields: heritage, classDC, perception, saves, lores, rank→bonus (`rank*2+level`), feats, spellCasters, equipment. Forced `pathfinder-2e-remaster`/"Remaster", Confidence High. Design: "Task 1".
      Files: `src/backend/Aircane.Application/Characters/Import/PathbuilderTwoMapper.cs`.
      Verify: `dotnet build src/backend/Aircane.sln` succeeds.

- [ ] 3. Add Pathbuilder unit tests + fixture + fixture copy rule.
      Files: `tests/backend/Aircane.UnitTests/Characters/Import/PathbuilderTwoMapperTests.cs`, `.../CharacterFormatDetectorTests.cs`, `.../Fixtures/pathbuilder2e.json` (anonymised, name "Test Character", no PII), and an `ItemGroup` in `Aircane.UnitTests.csproj` copying `Characters\Import\Fixtures\**\*.json` with `CopyToOutputDirectory=PreserveNewest`. Fixture loaded via `Path.Combine(AppContext.BaseDirectory, "Characters","Import","Fixtures", name)`. Tests: detection incl. `system` exclusion; ability/HP/AC/speed mapping; rank→bonus in ExtraFields; no PF2e save path on CanonicalCharacter; forced identifier/ruleset; MappedFields has no phantom-default keys; derived currentHP ∈ RequiresReviewPaths. Detector: precedence by Order under shuffled list, CanMap-throws swallowed, Unknown fallback.
      Verify: `dotnet test tests/backend/Aircane.UnitTests/Aircane.UnitTests.csproj` — new tests pass.

## BACKEND — DTOs, endpoint, acceptance guard (early, so integration can run)

- [ ] 10. Create new DTOs + extend existing DTOs + endpoint extension + new helpers.
      Files CREATE: `src/backend/Aircane.Application/DTOs/Characters/SourceImportResponse.cs` (record: DetectedSource, Confidence, RequiresSourceConfirmation, Ruleset, RulesetRequiresConfirmation, Review); `.../DndBeyondUrlImportRequest.cs` (CharacterUrl, GameSystemDefinitionId?, CampaignId?); `src/backend/Aircane.Application/Abstractions/IDndBeyondUrlImportService.cs` (+ `DndBeyondImportException` with `StatusKind` enum). Files MODIFY: `.../CharacterFieldReviewDto.cs` (add defaulted init-only members `GameSystemDefinitionId`, `MappedFields`, `RequiresGameSystemSelection`; `UnmappedFieldDto.RequiresReview` defaulted); `.../ImportCharacterJsonRequest.cs` (add `Guid? GameSystemDefinitionId = null`); `src/backend/Aircane.Api/Controllers/CharactersController.cs` (inject `CharacterFormatDetector`, `IEnumerable<ICharacterSourceMapper>`, `IDndBeyondUrlImportService`; extend `ImportCharacter` with `?source`/`gameSystemDefinitionId` + guard order incl. **F1 routing** + 2 MB cap + malformed-JSON controller-constructed ProblemDetails; add `PersistDraftFromCanonicalAsync` per **F5 (no Unknown stub)** + `BuildSourceReview`). DI stub for `IDndBeyondUrlImportService` can be a temporary no-op until item 8 lands, OR sequence item 8 before this — see FEAT ordering. Design: "Endpoint design", "DTO changes", "Draft persistence helper".
      Verify: `dotnet build src/backend/Aircane.sln` succeeds.

- [ ] 11. Register mappers + detector in Infrastructure DI.
      Files: `src/backend/Aircane.Infrastructure/DependencyInjection.cs` — register each `ICharacterSourceMapper` concrete, `CharacterFormatDetector`. (URL typed client added in item 8.)
      Verify: `dotnet build src/backend/Aircane.sln` succeeds.

- [ ] 12. Integration tests: endpoint happy-path + MEDIUM-1 + ApplyMapping acceptance guard.
      Files: `tests/backend/Aircane.IntegrationTests/CharacterSourceImportIntegrationTests.cs` (200+draft Pathbuilder; 200+requiresSourceConfirmation unknown; **F1** legacy `{canonicalJson, gameSystem, no ruleset, no source}` → 400 "ruleset is required."; malformed-JSON + >2 MB ProblemDetails exact Title/Detail/Status), `.../ApplyMappingPathAcceptanceTests.cs` (**F2 full FieldMappingEntry + F3 per-path range-safe table**; for each `CanonicalCharacterPaths.Supported` path, create draft then `PUT /field-mappings` single mapping asserting NOT rejected with "Unknown canonical field path"; negative bogus-path case; round-trip assert).
      Verify: `dotnet test tests/backend/Aircane.IntegrationTests/Aircane.IntegrationTests.csproj` — new tests pass.

## BACKEND — remaining mappers

- [ ] 4. `FoundryDnd5eMapper.cs` + `FoundryPf2eMapper.cs` (+ `DndBeyondSources.cs` if not yet) with unit tests + fixtures (`foundry-dnd5e.json`, `foundry-pf2e.json`). DetectRuleset for dnd5e (`_stats.systemVersion` v3+→2024/v2→2014, fallback source.book, default 2014+RulesetRequiresConfirmation when absent). Design: "Task 4", "Task 5", "Ruleset detection".
      Files: Import folder mappers; `tests/backend/Aircane.UnitTests/Characters/Import/FoundryDnd5eMapperTests.cs`, `FoundryPf2eMapperTests.cs`, Fixtures. Register in DI (item 11 pattern).
      Verify: `dotnet test tests/backend/Aircane.UnitTests/Aircane.UnitTests.csproj` — pass.

- [ ] 6. `Roll20Mapper.cs` (Order=0, always Low confidence, no forced system, every mapped path → RequiresReviewPaths) with unit tests + `roll20.json` fixture. Design: "Task 6".
      Files: Import mapper; `Roll20MapperTests.cs`; Fixture; DI register.
      Verify: `dotnet test tests/backend/Aircane.UnitTests/Aircane.UnitTests.csproj` — pass.

- [ ] 7. `GenericVttMapper.cs` (Order=int.MaxValue, Low confidence, all fields RequiresReview, warning text, no forced system) with unit tests + `generic-vtt.json` fixture. Design: "Task 7".
      Files: Import mapper; `GenericVttMapperTests.cs`; Fixture; DI register.
      Verify: `dotnet test tests/backend/Aircane.UnitTests/Aircane.UnitTests.csproj` — pass.

- [ ] 6b. Roll20/Generic two-call handshake integration test.
      Files: extend `CharacterSourceImportIntegrationTests.cs` — Roll20 with no source/gsid → `requiresGameSystemSelection=true` + no row persisted; re-POST with `?source=Roll20` + gsid → pinned detection, draft persisted, `requiresGameSystemSelection=false`. (Depends on items 6,10,12.)
      Verify: `dotnet test tests/backend/Aircane.IntegrationTests/Aircane.IntegrationTests.csproj` — pass.

## BACKEND — DDB PDF hints, URL importer, Companion

- [ ] 8. `DndBeyondPdfHints.cs` (Application, FieldMap → ApplyMapping paths; ClassLevel split regex `^(.+?)\s+(\d+)$`), extend `PdfCharacterExtractor.MapToCanonical` as priority override before `TryMapField`. Create `DndBeyondUrlImportService.cs` (Infrastructure; typed HttpClient "DndBeyond", BaseAddress `https://character-service.dndbeyond.com/`, 10s timeout, UA `Aircane-Tabletop/1.0`, GET `character/v5/character/{id}`, status→`DndBeyondImportException`, single-flight `SemaphoreSlim(1,1)` + ~2s min-interval guard, no caching, never log/store URL). Register typed client in DI (item 11). Design: "Task 8", "Task 2 — DndBeyondUrlImportService".
      Files: `src/backend/Aircane.Application/Characters/Import/DndBeyondPdfHints.cs`, `src/backend/Aircane.Infrastructure/Characters/{DndBeyondUrlImportService.cs}`, modify `PdfCharacterExtractor.cs`, `DependencyInjection.cs`.
      Verify: `dotnet build src/backend/Aircane.sln` succeeds.

- [ ] 9. `DndBeyondApiMapper.cs` (v5; sum all bonus layers; out-of-range ability→RequiresReviewPaths; DetectRuleset via `DndBeyondSources.Ruleset2024SourceIds {672,673,674}`, always RulesetRequiresConfirmation=true; forced dnd-5e-2014) + `DndBeyondCompanionMapper.cs` (`_meta`/`ddbId` detection; delegates to shared `MapFromRoot` on `character.`-prefixed element). Add `/api/characters/import/dndbeyond-url` action (`ImportCharacterFromDndBeyondUrl`) to the controller. Design: "Task 2", "Task 3".
      Files: Import mappers; controller action; DI register mappers.
      Verify: `dotnet build src/backend/Aircane.sln` succeeds.

- [ ] 9t. Unit + integration tests for DDB.
      Files: `DndBeyondApiMapperTests.cs`, `DndBeyondCompanionMapperTests.cs`, `DndBeyondUrlParseTests.cs`, `DndBeyondPdfHintsTests.cs` (+ extend `PdfCharacterExtractorTests`), fixtures `ddb-v5.json`/`ddb-companion.json`; `tests/backend/Aircane.IntegrationTests/DndBeyondUrlImportIntegrationTests.cs` using **WireMock.Net** (add `<PackageReference Include="WireMock.Net" Version="1.6.11" />` to `Aircane.IntegrationTests.csproj`), derived factory overrides "DndBeyond" BaseAddress; 200/403/404/timeout/5xx/invalid-URL cases with exact messages; assert URL never in captured logs. Also unit `CanonicalCharacterPathsTests` (bridge table 1:1, ⊆ Supported, excludes experiencePoints/passivePerception/initiative/proficiencyBonus) and `MapperMappedFieldsTests` (no phantom defaults).
      Verify: `dotnet test tests/backend/Aircane.UnitTests/Aircane.UnitTests.csproj` and `dotnet test tests/backend/Aircane.IntegrationTests/Aircane.IntegrationTests.csproj` — pass.

## FRONTEND

- [ ] 13. Extend characters feature + game-systems api.
      Files: MODIFY `src/frontend/aircane-web/src/features/characters/api.ts` + `store.ts` (add `importCharacterFromSource`, `importCharacterFromDndBeyondUrl`; TS types `SourceImportResponse`, extended `CharacterFieldReviewDto` review-scoped fields, `UnmappedFieldDto.requiresReview`, `ImportCharacterJsonRequest.gameSystemDefinitionId`), MODIFY `components/ImportCharacterModal.vue` (tabs Upload File | D&D Beyond URL | PDF; source badge; source + game-system confirmation dropdowns; DDB URL submit + disclaimer/help link), CREATE `components/ImportReviewPanel.vue` (loads FormDescriptor via `previewCharacterForm`, seeds from `review.mappedFields` via bridge table, highlights requiresReview/unmapped, D&D 5e 2014/2024 ruleset dropdown pre-populated with `response.ruleset` + amber when `response.rulesetRequiresConfirmation`; on confirm synthesizes `FieldMappingEntry[]` with **F2** `SourceFieldName` set to mapped path/label, submits via existing `applyFieldMappings`), MODIFY `src/features/game-systems/api.ts` (add `previewCharacterForm(id): Promise<FormDescriptor>` → POST empty body). Design: "Frontend files", "Review step".
      Verify: `npm run build` (cwd `src/frontend/aircane-web`) succeeds (typecheck passes).

- [ ] 14. Frontend unit tests.
      Files: `src/frontend/aircane-web/src/features/characters/components/__tests__/ImportCharacterModal.test.ts`, `.../ImportReviewPanel.test.ts` (api client mocked): tab switching, badge per source, source + game-system dropdowns, DDB URL submit, review panel seeds via bridge table, read-only "Imported from" header, D&D 5e ruleset dropdown + amber confirm highlight.
      Verify: `npx vitest run` (cwd `src/frontend/aircane-web`) — new tests pass.

## FINAL VERIFICATION
- [ ] 15. Full build + all test suites green.
      Verify: `dotnet build src/backend/Aircane.sln`; `dotnet test tests/backend/Aircane.UnitTests/Aircane.UnitTests.csproj`; `dotnet test tests/backend/Aircane.IntegrationTests/Aircane.IntegrationTests.csproj`; `npm run build` + `npx vitest run` (cwd `src/frontend/aircane-web`) — all succeed.
