# Post-MVP Backlog

This document lists planned features and improvements beyond the Aircane Tabletop MVP, organized by priority.

> **Note on the frontend gate.** Status lines in this document that say `vue-tsc` was clean or had 0 errors predate
> the real gate: plain `vue-tsc` (what `npm run build` ran) read a root tsconfig with no files, so it checked nothing,
> and the project carried 37 type errors nobody saw. The frontend gate is now `npm run typecheck` (`vue-tsc -b` over
> the app, the vitest specs, `vite.config.ts` and the e2e specs, preceded by the coverage guard
> `scripts/check-typecheck-coverage.mjs`) and `npm run lint` (ESLint), both required steps of Frontend CI. See
> [Frontend Type-Check](setup/frontend-typecheck.md) and [Frontend Lint](setup/frontend-lint.md).

---

## Intentionally Not Auto-Built (Maintainer Decision Required)

The following backlog items were **deliberately left for a human maintainer** rather than
implemented autonomously, because each is blocked on a decision, carries security risk, or
needs a toolchain/content that cannot be safely chosen without owner input. They remain in
the priority lists below with full detail; this section records _why_ they were skipped and
_what unblocks them_.

| Item                                 | Why not auto-built                                                                                                                                                                                                                                                                                          | What unblocks it                                                                                                                                                                               |
| ------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **PF2e Remaster real ORC text**      | Content/licensing decision. The obvious data sources (Foundry PF2e, Obsidian TTRPG Community) are **not** ORC-licensed — they fall under a private Paizo↔Foundry agreement and Paizo's Community Use Policy, so bundling them as ORC content would be a licensing violation.                                | Maintainer sources text from a genuine ORC release and drops it into the existing `pf2e_remaster` bundle (no code changes needed).                                                             |
| **Internet Tunnel / Remote Play**    | **Decision made — Cloudflare Tunnel. Delivered.** Tunnel bundled as a second Tauri sidecar (checksum-verified at build time), with the security hardening prerequisites — per-IP rate limiting, SPA CSRF/origin checks, and persistent token revocation — implemented first. See "Completed P1 Work" below. | Nothing — delivered. Follow-up (persistent named tunnels via a Cloudflare account) is optional and tracked informally.                                                                         |
| **Desktop Wrapper (Tauri/Electron)** | **Decision made — Tauri.** Wrapper implemented: backend runs as a Tauri sidecar, native webview, LAN URL/QR in the tray, Ollama awareness. See "Completed P1 Work" below.                                                                                                                                   | Nothing — delivered. Remaining follow-ups (code signing/notarisation, auto-update, bundled database) are tracked as separate deferred items.                                                   |
| **Application source-code license**  | Decision made. **Proprietary** license chosen for the application source code (distinct from the content licenses ORC/CC BY, which are unaffected).                                                                                                                                                         | Nothing to unblock — done. `LICENSE` added and README license section updated; the in-app About panel points at the README for this.                                                     |
| **PF1e built-in content**            | Removed for commercialization — OGL v1.0a adds a Section 15 attribution chain and a content-identification burden that adds legal complexity for a commercial product regardless of the source-code license chosen. D&D 5e SRD (CC BY 4.0) and PF2e Remaster (ORC) are commercial-friendly and retained.    | Nothing to unblock — this is a deliberate removal, not deferred work. Users may still import their own PF1e PDFs via the folder-watching Library; only the bundled OGL rules text was removed. |

---

## Recently Delivered (not yet in a priority bucket)

### RAG Pipeline Optimization (Items 1–11)

> **✅ Delivered and committed to `dev`** across multiple commits (`20c1536`, `222f16f`, `691e573`, `de97e11`, `7ab4735`, `7ab4735`, `9b1083d`, `189ec21`, `89b2d3d`, `765b8cf`, `3c2cde0`). All unit (2,339), integration (118), and frontend (369) tests green.

A full retrieval quality overhaul covering:

- **Caller visibility fix** — `RulesQuestionController` reads actual caller role; unauthenticated/dev treated as Host, unknown role defaults to Player.
- **Source priority** — `ChunkResultDto` carries `SourceType`; `ApplySourcePriority` orders Homebrew > Rules > Adventure > Solo > Generated > Character > Unknown then by score.
- **Ruleset normalization** — case/whitespace-insensitive filter; `RulesetNormalizer` expands aliases (2014, 2024, "one dnd", remaster); `GET /api/ai/rules-question/rulesets` endpoint; Rules Lookup uses dropdown.
- **Scoring formula** — keyword-only = 0.7 × term_fraction; vector-only = 0.6 × cosine; both = min(1, 0.7k + 0.5v).
- **Retrieval limits** — TopK=10, MinRelevanceScore=0.4 in both `RulesQuestionService` and `PlayerActionService`.
- **Chunk deduplication** — `RemoveDuplicateChunks` skips chunk when >40% text overlaps earlier chunk (50-char windows).
- **OCR cleanup** — two regexes in `PdfPigTextExtractor.RecognizePageAsync`: spaced-character collapse (`A c t o r` → `Actor`) and sentence-end split (`.Actor` → `. Actor`); OCR-only, not PDF text layer.
- **Heading-aware chunking** — `SlidingWindowTextChunker` defaults now 1100 chars / 300 overlap; split order: heading (last 400 chars) → paragraph break (last 300) → newline/period (last 200) → hard cut; heading placed at START of next chunk; `SectionTitle` propagated across page boundaries.
- **Postgres integration tests** — `PostgresFixture` with `Testcontainers.PostgreSql`; clones fresh DB per test; 20 tests covering keyword/vector/filter/merge/dedup/source-priority against real pgvector.
- **Cross-encoder reranking** — `IRerankingProvider` abstraction with `NullRerankingProvider` (default), `CohereRerankingProvider` (Cohere Rerank API, `rerank-english-v3.0`), and `OllamaRerankingProvider` (LLM-judge via `/api/generate`, default model `llama3.2:3b`, up to 4 concurrent calls). Candidate pool = 25 when reranking active (vs 10 otherwise); `PostRerankTopN` = 7; `MinRelevanceScore` lowered to 0.15 when reranking active; source-priority boost applied after reranking (Homebrew +0.10, Rules +0.05). Settings UI includes provider select, per-provider fields, test connection, and amber latency warning for Ollama. Verified: Actor feat (PHB p.154) surfaces correctly with both Cohere and Ollama reranking. **Known gap:** complex multi-part questions (e.g. Misty Step + Fireball bonus action rule) may not surface the specific rule chunk — query decomposition (agentic retrieval) is the long-term fix, tracked in P4.
- **Structured stat block chunker** — `StatBlockDetector` finds monster/NPC stat blocks from text alone and `SlidingWindowTextChunker` emits each as ONE chunk (`ChunkType = "stat-block"`, `SectionTitle` = creature name), bypassing the 1100/300 window so action lists are never split (e.g. Giant Crocodile Bite + Tail). Handles line-based headers (name + size/type line, name — size/type, PF2e `Creature N`) **and** run-together text with no line breaks, as the real Monster Manual extracts (`GIANT. CROCODILE Huge beast, unaligned Armor Class 14 …`, confirmed by `Armor Class`). Blocks end at the next stat block, a heading, or end of page; blocks over 4,000 chars split at `ACTIONS`/`Reactions`/`Legendary Actions`/`Lair Actions` with a logged warning. Verified on the real Monster Manual PDF: 279 stat blocks over 354 pages, longest 3,933 chars. No migration (`DocumentChunk.ChunkType` already existed). Monster Manual re-imported and checked end to end by the maintainer; early results look successful (not yet a broad regression pass over other creatures or books). **Existing documents keep their old chunks — delete and re-import any other bestiary (e.g. Volo's, Mordenkainen's) to benefit.** **Known gaps:** detection is per page, so a block that continues onto the next page is cut at the page break; `ChunkType` is stored but not yet used to filter or boost in retrieval — both tracked in P4.

### Source-Specific Character Import (D&D Beyond / VTT / Pathbuilder 2e)

> **✅ Delivered and merged to `dev`** (PR #15, merge commit `89dc148`). Reviewed, build/tests green.
> Covers the former P2 items **"D&D Beyond / VTT Integration"** and **"Pathbuilder 2e Character Import"**.

Source-specific import adapters that funnel into the **existing** character import pipeline — purely
additive, no change to the canonical schema or the review/save flow. New
`ICharacterSourceMapper` abstraction + `CharacterFormatDetector` (Order-based precedence) in
`Aircane.Application/Characters/Import/`; each mapper emits a flat dictionary of raw field values that
funnels through the existing `CharacterService.ApplyMapping` / `CanonicalCharacter` rail into
`Character.CanonicalJson` (no new draft type, no typed-column writes). Mappers: **Pathbuilder 2e**
(proficiency rank → bonus, spellcasters → spell blocks, auto PF2e/Remaster), **D&D Beyond API v5**
(sums bonus layers, out-of-range → `requiresReview`, 2014/2024 ruleset via source IDs), **D&D Beyond
Companion/DDB-Importer**, **Foundry VTT dnd5e** (ruleset via `_stats.systemVersion`), **Foundry VTT
pf2e** (auto PF2e/Remaster), **Roll20** (flat `attribs`, always low-confidence), and **Generic VTT**
(heuristic fallback). New `POST /api/characters/import/dndbeyond-url` →
`DndBeyondUrlImportService` (typed HttpClient hitting the **unofficial**
`character-service.dndbeyond.com` v5 endpoint; 403/404/timeout → clear `422` messages; **never called
from the browser**, responses not cached, URL never stored/logged; in-service single-flight guard
since the `api` rate-limit policy is a LAN no-op). `DndBeyondPdfHints` priority-override wired into the
existing PdfPig extractor (splits `"Fighter 5"` → class + level). Frontend: `ImportCharacterModal.vue`
tabs (Upload File / D&D Beyond URL / PDF), detected-source badges, source-override dropdown, and an
`ImportReviewPanel.vue` reusing the existing **FormDescriptor-driven** character sheet renderer with an
always-editable 2014/2024 ruleset dropdown for D&D 5e. PF2e/5e sources auto-set the game system; Roll20
and Generic VTT use a two-call handshake so the user picks a system first. Tests: per-mapper unit tests,
detector precedence/negative tests, DDB URL parse/status tests (DDB API mocked with **WireMock.Net
1.6.11**), PDF-hint tests, endpoint integration tests (happy paths, `requiresSourceConfirmation`,
preserved legacy `400 "ruleset is required."`, DDB `422`), and anonymised fixtures per source. Verified:
backend 2125 unit + 96 integration green, frontend 317 vitest green (type-check and lint: see the frontend-gate note at the top). **Caveat:** the D&D
Beyond URL path depends on an unofficial API that may change — PDF export is the documented fallback.

### In-App "Report a bug" Feedback Feature

> **✅ Delivered and merged to `dev`** (`9f7c8d8`, with doc follow-up `a95ecfd`). Reviewed, build/tests green.

A "Report a bug" button available throughout the app that auto-captures diagnostic context at
submission time and posts a structured issue to **GitHub Issues** via the GitHub REST API, proxied
by the backend so the GitHub token never reaches the browser. Backend: unauthenticated,
rate-limited (`5/IP/hour`, limiting in **every** mode unlike the LAN-no-op `join`/`api` policies)
`POST /api/feedback` (`FeedbackController` → `IFeedbackService` → `GitHubFeedbackService`), config
keys `Feedback:GitHubToken`/`GitHubOwner`/`GitHubRepo`, clear **503** when unconfigured, token
server-side only, HTML-sanitized `summary`/`description`. Frontend: `FeedbackModal.vue`,
`useFeedbackDiagnostics`, a module-singleton `useFeedbackEventBuffer` (last-20 SignalR events, type +
short summary only — never narration/character/PDF content), a `console.error`/Vue-errorHandler/
`unhandledrejection` buffer installed before `createApp()`, a top-nav button, and a Tauri tray item.
Docs: `docs/setup/feedback.md`. **Known mismatch:** no persistent AI-provider/user-role store yet, so
`embeddingProvider`/`embeddingModel`/`userRole` are sent as `null` (rendered as dashes). **Maintainer
setup already done:** `bug` + `beta-feedback` labels exist on the repo, and the feedback GitHub
secrets are in .NET user-secrets for `Aircane.Api` (token verified against the Issues API).

### Solo Adventure Execution (AI-run play of a generated adventure)

> **✅ Delivered on `dev`** (`feat(adventures): solo adventure execution for generated adventures`).
> Backend unit (2,421), integration (140, incl. 6 against real PostgreSQL) and frontend (411) suites green (type-check and lint: see the frontend-gate note at the top); migration `AddAdventureSession` applies cleanly on top of the full chain.

An approved generated adventure can be played solo: the AI DM runs one player through it scene by scene,
narrating, tracking state, and advancing the story from the player's actions.

- **State.** New `AdventureSession` entity (`Aircane.Domain`): current scene, completed scenes, per-NPC state,
  revealed clue/secret IDs, encounter outcomes, status (`NotStarted/InProgress/Completed/Abandoned`). Lists are
  jsonb columns (with a snapshot `ValueComparer` so in-place edits are tracked); FKs cascade from the adventure and
  campaign. It also carries the **play `Session`** it broadcasts to (`SessionId`), created automatically on first start.
- **Service.** `IAdventureSessionService` / `AdventureSessionService` — start (idempotent; one in-progress run per
  campaign), state DTO, advance (target must exist **and** be an exit of the current scene), reveal clue/secret,
  patch NPC, complete, abandon, and `BuildAdventureContext`. Every change appends a non-reversible `Adventure:*` event
  to the campaign log (so undo/replay ignore it) and broadcasts `AdventureSceneAdvanced/ClueRevealed/SecretRevealed/
  NpcUpdated/Completed` over SignalR.
- **AI loop.** `PlayerActionService` injects the adventure context + action-schema instructions when a run is active
  and applies five new `AiActionType`s (`AdvanceScene`, `RevealClue`, `RevealSecret`, `UpdateNpc`,
  `CompleteAdventure`) through the session service. They are **role/authority-gated and never queued**: the proposal
  queue can't replay them, so a campaign whose authority isn't Full Session Control gets a `PermissionDenied` result
  with an explanatory message instead of a stuck proposal.
- **API.** `AdventureSessionsController` (`/api/adventure-sessions`): start/resume, get state, get active, advance,
  abandon, available scenes, plus `POST /{id}/player-action` (the solo loop; solo play has no formal character).
  Callers must be the Host, or the Player whose token was issued for the run's play session.
- **Frontend.** `features/adventures/`: `AdventureSoloSession.vue` (narration feed, scene panel, action input),
  `adventureSessionStore.ts`, `PlaySoloLauncher.vue` (campaign picker for adventures that are not yet in a
  campaign), route `/adventures/solo/:sessionId`. Exits ask the AI to narrate the move; if the AI agrees but the
  campaign's authority blocks it, the chosen exit is applied as a host override. Where adventures are listed and
  played from, and how a run is resumed, is covered in "Adventure Forge, Campaigns and Play Polish" below.
- **Content IDs.** The generation pipeline only numbers scenes, so NPCs/clues/secrets/encounters are addressed by
  deterministic derived IDs (`npc-<slug>`, `clue-N`, `secret-N`, `encounter-N`; see `AdventureContentIds`). Scenes also gained
  an optional `objectives` list (requested in the scenes prompt; older adventures simply have none).
- **Provider fix required for this to work.** The OpenAI/Ollama providers read structured output as snake_case with numeric
  enums, but the AI DM prompt tells the model to emit camelCase and action types by name — so `proposedActions` was
  silently dropped on real providers. They now share `AiStructuredOutputSerializer`, which accepts both key styles and
  string or numeric enums (existing snake_case tests unchanged).

**Known gaps / follow-ups:**
(1) `AdventureEncounterOutcome` is persisted and shown in the state DTO, but nothing records outcomes yet (no AI action
or endpoint). (2) Finished runs (Completed / Abandoned) can't be reopened or replayed from the UI; only in-progress runs
are surfaced. _(Earlier gaps now closed: the narration transcript is persisted, adventures have list views, and the
integration test factory no longer reaches a real AI provider; see below.)_

### Adventure Forge, Campaigns and Play Polish (generation, resume, AI DM behaviour)

> **✅ Delivered and merged to `dev`** (PRs #19–#36, 2026-10-12 → 2026-10-18). Backend unit (2,520) and integration (144, incl. real-PostgreSQL) and frontend (478) suites green (type-check and lint: see the frontend-gate note at the top). Migrations added: `AddGeneratedAdventureCampaignId`, `AddAdventureSessionPendingRoll`, `AddAdventureSessionEntry`.

Follow-on work that turned solo execution into something you can actually use day to day.

- **Async generation with live progress (#21, #22).** `POST /api/adventures/generate` queues an
  `AdventureGenerationJob` on the background queue and returns 202 `{ jobId }`; the job reports stages
  (analysing 10 % → complete 100 %) over a host-only `adventure-generation` group on `LibraryHub`
  (`AdventureGenerationProgress / Completed / Failed`). An app-level toast (mounted in `App.vue`) shows the stage and
  a bar, links to the result, auto-dismisses after 10 s unless hovered, and offers Try Again on failure; the Forge form
  locks and shows progress inline while a job runs, and navigation is never blocked. The listeners start once at app
  launch, rebuild if the connection closes for good, and adopt events for a job they didn't start (reload / other tab).
- **Adventures belong to campaigns (#23, #24).** `GeneratedAdventure.CampaignId` (nullable, `SET NULL` on campaign
  delete). New `/campaigns/:campaignId` page with an Adventures section (Draft: Review / Approve / Delete; Approved:
  Review / Play Solo / Delete), generation from a campaign saves into it (`?campaignId=`), and Back on the review screen
  returns to the campaign. `/adventures` (the sidebar's Adventure Forge item) lists **unassigned** adventures with
  Assign to Campaign / Approve / Review / Play Solo / Delete and a + Generate New Adventure button
  (`GET /api/adventures?unassigned=true`, `PATCH /api/adventures/{id}/assign`). The toast links to the campaign page, or
  to `/adventures` when no campaign was chosen.
- **Narration persistence and resume (#31, #33).** `AdventureSessionEntry` stores each player action and AI narration
  (best effort, never blocks a turn); `GET /api/adventure-sessions/{id}/history` returns the last 50. Reopening a run
  fills the feed behind a "── Resuming session ──" divider, scrolls to the latest exchange, and shows an AI-written
  **Story So Far** (`POST …/recap`, a short state-only call, no retrieval). Cards with a run in progress show
  "Scene X of Y — In Progress" and **Resume** instead of Play Solo (campaign page and Forge list), and the dashboard's
  Recent Campaigns panel (previously static markup) now lists every campaign by recent activity with a Resume Adventure
  link. `POST …/resume` clears a stale pending roll when a player comes back.
- **AI DM behaviour (#28–#30, #34, #35).** RequestRoll no longer needs a linked character (players can use an external
  sheet; the prompt then asks for the roll in words and waits for the result) but must carry a label; the run remembers
  the roll it asked for (`PendingRollLabel/DC`) so "I rolled a 14" is read as its result, and clears it after the next
  action; adventure retrieval skips reranking (standard pool of 10, 0.4 floor) to avoid the Ollama latency. Prompt rules
  added for mandatory roll triggers (physical, stealth, social, search, magic, combat, awareness/detection) with DC
  guidelines, narrate-then-roll, describing every exit with sensory detail, and ending every narration with a clear
  player prompt.
- **Resilient AI replies (#20, #27, #32).** Free-text fields use `StringOrObjectConverter` (object/array/number → compact
  JSON text) across the adventure DTOs, `AiStructuredOutput` and its actions/citations; the provider deserializer strips
  markdown fences and extracts the first JSON object from prose-wrapped replies; `RulesCitation` IDs are strings
  (models cite "PHB", not GUIDs).
- **Smaller changes.** Review-screen Back goes to the Forge list (#25); the "Dice Roller" nav item and dashboard card are
  now **Join Session** (#26); the dashboard's adventure card opens `/adventures`, and the integration test factory now
  always registers the fake AI, embedding and reranking providers, so tests can no longer make real, billed calls (#36).

**Known gaps / follow-ups:**
(1) The prompt rules (rolls, detection, exits, end-of-narration) are instructions, not enforcement: the model can still
ignore them. A server-side check (e.g. flag a narrated success for an action that should have been rolled) or a
smaller, structured "what happens next" step would make them reliable. (2) The recap is regenerated on every resume;
it could be cached on the run and refreshed only after new turns. (3) The transcript holds player actions and AI
narration only (clue/secret/NPC system lines are re-derived from state, not saved), and runs played before #31 have none.
(4) Reloading the page between the AI asking for a roll and the player answering clears the pending roll, so the AI may
ask again. (5) Generation jobs live in the in-memory queue: an API restart mid-generation loses the job, and a page
reload mid-job loses the progress display (the job still finishes and saves). (6) Strict deserialization remains for
non-text fields (GUIDs, numbers, booleans), where a wrong type has no sensible fallback. (7) Campaigns have no
"last active" field; the dashboard derives it from the campaign's update time and its latest run. (8) The Playwright e2e
suite is not run by the usual checks and has needed manual fixes after navigation changes (#24, #26, #36); consider
running it in CI.

### First-Launch Setup Wizard (PR #7, audited PR #37)

> **✅ Delivered and merged to `dev`** (PR #7 commits `a1e5554`, `ec8ee07`,
> `c81b117`; Ollama probe timeout raised in `765b8cf`; spec audit and model
> description fix in PR #37). Frontend (24 setup-specific) and integration
> (5 setup status) tests green (type-check and lint: see the frontend-gate note at the top).

A fullscreen setup wizard shown once on first launch (or when the AI provider
is still Fake and the wizard has not been previously dismissed).

- **Trigger.** Shows when `GET /api/setup/status` returns `aiProviderConfigured: false`
  and `localStorage.setup_wizard_completed` is not set. Skipped entirely for Player
  browsers (detected via `participant_token` in sessionStorage).
- **Steps.** Welcome → Choose provider (Ollama ★ Recommended / OpenAI / Try first) →
  Ollama setup (download link, two copy commands, live connection poller every 3 s) or
  OpenAI setup (API key field, test connection, save) → Done (summary + gentle reminder
  if provider is still Fake). The Try-first path skips Step 3 and shows "Step 2 of 3".
- **Ollama step.** Lists exactly two models: `llama3.2` (~2 GB, AI DM chat and
  reranking) and `nomic-embed-text` (~270 MB, rules library search). Connection check
  polls `/api/setup/status` automatically — no manual button.
- **Completion.** "Start playing" sets `localStorage.setup_wizard_completed = true` and
  navigates to the dashboard. Skip on any step does the same immediately.
- **Backend.** `GET /api/setup/status` (anonymous, `SetupController`) returns
  `isFirstLaunch`, `aiProviderConfigured`, `activeAiProvider`, `activeEmbeddingProvider`,
  `ollamaReachable` (2000 ms probe timeout, raised from 200 ms to fix a flaky test).

---

## Priority 1 - High Impact / Frequently Requested

> **Status:** The actionable P1 engineering items have been **completed and removed** from this
> list — background job infrastructure, combat automation surfacing (REST + tracker UI + AI-prompt
> integration), the PF2e adapter (validator, generation prompts, elite/weak), configurable
> embedding dimensions with cloud embedding providers, the library import UX follow-ups, and the
> OCR follow-ups all shipped. See the "Completed P1 Work" section below for what was delivered and
> where. What remains in P1 are the **maintainer-gated items** that cannot be built autonomously
> (see the decision table at the top of this file).

### Pathfinder 2e Remaster Built-in Content (real ORC text)

> **Not auto-built — content/licensing decision; maintainer action.** See the decision table at the top of this file.

The `pf2e_remaster` built-in bundle currently ships a small placeholder (2 chunks) so the app runs; the seeder handles the partial bundle gracefully. To complete it, source the real PF2e Remaster rules text **from a genuine ORC-licensed release** and drop the Markdown files into `src/backend/Aircane.Workers/Resources/builtin/pf2e_remaster/` (adding entries to its `manifest.json`). No code changes needed — the bundle structure, `LICENSE-ORC.txt`, manifest, and seeder already work. **Licensing caution:** do NOT extract content from the Foundry VTT PF2e system data or the Obsidian TTRPG Community repo — that material is distributed under a private Paizo↔Foundry partnership agreement and Paizo's Community Use Policy, not the ORC License, and cannot be bundled as ORC content here. Use only text that is actually released under ORC. Follow the full ORC requirements (Notice, upstream Paizo product attribution, downstream ORC-Content-vs-Reserved-Material declaration, Product Identity exclusions, and the OGL/ORC split) documented in [`docs/licensing/open-content-compliance.md`](licensing/open-content-compliance.md).

### Application Source-Code License

> **Done — Proprietary.** The application source code is licensed under a **Proprietary** license, kept distinct from the content licenses (ORC/OGL/CC BY), which are unaffected. See the decision table at the top of this file.

The `LICENSE` file and README License section are in place and reflect the proprietary terms. The in-app About / Credits panel (`/about`) already points users at the README for the app license. (The compliance surface itself — ORC Notice, CC BY / OGL attributions, ORC-Content-vs-Reserved-Material downstream declaration, and trademark note — is delivered in the About panel.)

---

## Completed P1 Work (delivered — kept for reference)

The following Priority-1 items were implemented and verified (backend build clean; full backend
unit + integration suites and the frontend vitest suite green). They are recorded here rather than
in the active list above.

- **Background Job Infrastructure.** In-process `IBackgroundJobQueue` (`ChannelBackgroundJobQueue`) + a `BackgroundJobWorker` hosted service dispatching to `IJobHandler<T>` in fresh DI scopes, an in-memory job-status store, and `GET /api/jobs/{id}`. Document import, folder scan, and re-embed now enqueue and return `202 Accepted`; `ImportStatusUpdated` carries the `jobId`. A `BackgroundJobs:Runner` switch is reserved for a future Hangfire swap.
- **Advanced Combat Automation (surfacing).** The backend `CombatEngine`/`EncounterState` (initiative, turns, conditions, HP, death saves) is now surfaced end-to-end: a `CombatController` exposes host combat controls, `CombatTracker.vue` renders initiative/HP/conditions (interactive for the host, read-only with rough HP bands for players) driven by the `CombatTurnChanged` SignalR event, and `PlayerActionService` feeds a compact live-combat block into the AI DM prompt (via `IAiContextAdapter.BuildCombatContext`). _Still open (lower priority):_ concentration checks and out-of-turn action enforcement.
- **Pathfinder 2e Adapter (generation logic).** `Pf2eEncounterValidator` (creature-level XP budget, Trivial/Low/Moderate/Severe/Extreme tiers) selected per campaign via `IEncounterValidatorSelector`; PF2e-aware adventure generation prompts (three-action economy, degrees of success, PF2e tiers/conditions, treasure-by-level); and an `EliteWeakAdjuster` with `ApplyEliteTemplate`/`ApplyWeakTemplate` commands. (The mechanics definition was delivered earlier; the ORC rules text remains a separate maintainer-gated item above.)
- **Embedding Provider Portability (dimensions + cloud providers).** Configurable embedding dimension with a startup compatibility check that disables vector search (keyword search still works) and logs a clear warning on a provider/column mismatch; `OpenAiEmbeddingProvider` (`text-embedding-3-small`, 1536) and `AzureOpenAiEmbeddingProvider` behind the existing `IEmbeddingProvider` abstraction. Provenance recording + mismatch guard + re-embed endpoints were delivered previously. Migration path documented in `docs/setup/ai-configuration.md`.
- **Library Import UX follow-ups.** Content-based duplicate detection (`SourceDocument.ContentHash` SHA-256 + `ExactDuplicate` flag), a `GameSystemAlias` table with seeded common aliases + canonicalizer alias lookup + management API, and configurable per-folder `WatchedFolder.ExcludePatterns` (glob) driving the likely-not-rules signal. (The review-before-import flow, quick import, filename suggestions, and canonicalization were delivered previously.)
- **OCR follow-ups.** Full-page rasterization via `IPdfRasterizer`/`DocnetPdfRasterizer` (PDFium, gated by `Ocr:FullPageRasterization`), an opt-in tessdata auto-downloader (`Ocr:AutoDownloadTessdata`), and re-OCR endpoints (`POST /api/library/documents/reocr[-all]`) with a "Re-run OCR" library UI button — all routed through the background job queue. (The Tesseract engine + embedded-image OCR were delivered previously.) See `docs/setup/ocr.md`.
- **Internet Tunnel / Remote Play (Cloudflare Tunnel).** Remote players can join a session over the internet without router config. `cloudflared` is bundled as a **second Tauri sidecar**, fetched at build time from the official Cloudflare release and **SHA256-verified against a pinned manifest (build fails on mismatch)**. A `tunnel.rs` lifecycle module spawns the tunnel, captures the `*.trycloudflare.com` URL, reports it to the backend, and tears it down on window close/app exit; `enable_/disable_internet_play` Tauri commands drive it from the Vue UX (`InternetPlayPanel` with a dismissible Cloudflare disclosure, `AccessModeToggle`, QR + copy). `GET /api/sessions/network-info` now also returns `tunnelUrl`/`tunnelActive`, and a loopback-only `TunnelController` records the URL. **Security hardening (hard prerequisite, implemented first):** per-IP rate limiting (fixed-window on join/auth, sliding-window on all other endpoints) that engages only in internet mode; SPA-oriented CSRF/origin + `Content-Type` + `X-Requested-With` checks (bearer-token SPA, so no antiforgery cookie); and **persistent token revocation** — a `RevokedToken` table + `jti` claim so revocations survive a server restart (replaces the old in-memory-only list), bulk-revoked on session end, with a daily cleanup job. Verified: backend 1964 unit + 34 integration tests green, frontend `vue-tsc` + 108 session tests green, desktop `cargo check` + 3 tunnel tests green, and the Desktop CI build passes on Windows/macOS/Linux. The tunnel runs only in the desktop wrapper; browser-only hosts use LAN mode. Docs: `docs/setup/internet-play.md`, `docs/architecture/security.md`. **Optional follow-up:** persistent named tunnels (requires a free Cloudflare account).
- **Desktop Wrapper (Tauri v2).** A `desktop/` Tauri v2 app that runs the ASP.NET Core backend as a managed sidecar (self-contained single-file publish), waits on `/api/health` behind a loading screen, then opens the Vue frontend in a native OS webview; a clear error screen handles startup timeout and port conflicts. Adds a system tray (Copy LAN URL, Show QR, Open, Quit), a dynamic window title with the local + LAN URLs backed by a new `GET /api/sessions/network-info` endpoint, and Ollama awareness (a dismissible banner when Ollama is the active provider but not running, plus a live status indicator in AI settings). The Vue app stays browser-agnostic behind a `window.__TAURI__` bridge. The sidecar sets `Aircane:TrustLocalHost=true` so the single local host can drive host-only surfaces (and first-run migrations) without a session token; a shared/hosted backend must not set it. Cross-platform build scripts (`desktop/build.{sh,ps1}`), a `Desktop CI` workflow (win/mac/linux, artifacts only), a single-source `VERSION` file with `scripts/bump-version.*`, and placeholder icons are included. See `docs/setup/desktop.md`. **Deferred follow-ups:** code signing/notarisation, `tauri-plugin-updater` auto-update, and a bundled/embedded database (the wrapper still needs an external PostgreSQL).

---

## Priority 2 - Significant Enhancements

### Map and Battlemap Support

Add grid-based or theater-of-the-mind map rendering with token placement, fog of war, and distance measurement. Integrate with the AI DM for spatial awareness during encounters.

### AWS Cloud Deployment

Production-ready deployment on AWS: ECS Fargate or App Runner for the backend, RDS PostgreSQL with pgvector, S3 for uploaded documents (cloud source mode), CloudFront + S3 for the frontend, Cognito for user accounts, Bedrock for AI/embeddings, and Secrets Manager for credentials.

### File Upload Source Mode

Implement the `Upload` path of the existing `IDocumentSource` abstraction so cloud-hosted instances can accept file uploads directly rather than requiring local folder paths.

### User Accounts and Persistent Identity

Replace short-lived session tokens with full user accounts (local credentials or OAuth/Cognito). Enables cross-session identity, campaign membership, and character ownership without re-joining.

### SQLite Option for Lightweight Installs

Allow single-user or desktop installations to run without Docker/PostgreSQL by using SQLite with a compatible vector extension.

### Group Play (Full AI DM and Co-DM Modes)

Solo adventure execution is delivered. The next play mode is group play:
multiple players joining a campaign session while the AI DM (or AI-assisted
human DM) runs the adventure.

- **Full AI DM mode** — same AI loop as solo but with multiple player tokens;
  each player submits actions, the AI narrates to all, and the session hub
  broadcasts events to every connected client.
- **Co-DM mode** — a human host runs the session with the AI in Suggest or
  Ask authority; the AI proposes actions (scene advances, roll requests,
  NPC updates) which the host approves or rejects before they take effect.
- Backend: `AdventureSession` already supports multiple players conceptually;
  the main work is the multi-player action queue and turn ordering.
- Frontend: player-facing session view (read-only narration feed, roll prompts,
  character panel) and host-facing DM controls (approve/reject proposals,
  manual overrides, combat tracker integration).

> **Delivered and moved out of P2:** _D&D Beyond / VTT Integration_ and _Pathbuilder 2e Character
> Import_ shipped together as the source-specific character import adapters — see "Recently
> Delivered" above (PR #15). The one standing caveat is that the D&D Beyond URL path relies on an
> unofficial API that may change.

### D&D Beyond Printable-Sheet Value Localization (OCR geometry — blocks real DDB PDF extraction)

The OCR infrastructure for DDB PDF character import shipped (first-class default-on OCR, desktop-bundled
Tesseract, caption-signature detection, region OCR with per-region PSM) and the pipeline runs end-to-end:
a real DDB 2024 printable sheet is detected, 15 regions are OCR'd, ruleset auto-detects as 2024, a review
draft is persisted, and nothing crashes. **But it does not yet extract the actual values.** Verified live
against a real DDB export: the caption-anchored value regions in `DndBeyondSheetLayout` land on the
caption words or empty space, not the value glyphs — e.g. the "ARMOR" region OCRs the caption word
"ARMOR" at _rising_ confidence as DPI increases, and the ability boxes return noise (`ee`/`ae`). An
exhaustive per-region offset sweep (Dy 0.6–3.0 × multiple widths) and DPI escalation (300/450/600)
recovered no real values, so this is a value-localization/geometry problem, not PSM/DPI tuning (the PSM
wiring is correct and already shipped — crops now return text instead of nothing). Needs an investigation
to choose a strategy, not incremental tuning. Candidate approaches:

- **Re-derive the DDB sheet value geometry** from the real layout (where each value glyph sits relative
  to its caption), likely per sheet version (2014 vs 2024 differ).
- **Whole-page OCR + spatial parsing** instead of fixed caption-anchored crops (OCR the page, then
  associate recognized value tokens to captions by position).
- **Lean on the verified-working alternatives and treat DDB-printable OCR as best-effort:** the
  **AcroForm path already works** for form-fillable sheets (verified: official WotC fillable sheet
  enumerates 334 fields and `DndBeyondPdfHints.FieldMap` keys match the real field names verbatim — no
  OCR needed), and the **character-service JSON API** path exists (now that the import-draft persistence
  400 is fixed). Likely product direction: guide DDB users toward a fillable export / JSON, with
  rasterized-printable OCR as a best-effort fallback.
  This is why the DDB _printable-PDF_ import specifically does not yet populate fields; the broader
  character-import feature (JSON sources, form-fillable PDFs) is unaffected.

---

## Priority 3 - Quality of Life

### Unify PDF Character Import onto the Source-Adapter Review Rail

The PDF character import now lives as a tab inside the single **Import Character** modal (alongside
Upload File and D&D Beyond URL), but it still runs on a **separate flow underneath**: it posts to
`/api/characters/import/pdf` and, when fields need attention, redirects to the full-page
`character-field-review` route — an _unmapped-fields_ review distinct from the inline, FormDescriptor-
driven `ImportReviewPanel` every other source uses. Two follow-ups to converge it:

- **Fold the PDF path behind the same source rail** so extracted PDF fields produce a flat field
  dictionary that funnels through `ApplyMapping`/`CanonicalCharacter` and the inline `ImportReviewPanel`,
  retiring the second review UI (one review experience for all sources, less duplicated test surface).
- **Drop the hardcoded `gameSystem=D&D 5e` / `ruleset=2014`** that the PDF upload currently sends in its
  multipart form. A non-5e PDF is mislabeled today; the system/ruleset should be detected or chosen the
  same way the JSON/source paths handle it (PF2e auto-set, 2014/2024 confirmable for 5e, user-selected
  for unknown). _Deferred deliberately_ from the button-consolidation change (Option A) because merging
  the two review models touches the backend PDF endpoint, the review components, and their tests, and the
  existing PDF flow works and is tested.

### Curated AI Model List

The OpenAI model dropdown currently fetches every model on the account (50+).
Replace with a curated hardcoded list of models that work well for TTRPG AI DM
use, with a recommended default. Pending model testing across gpt-4o, gpt-4.1,
gpt-4.1-mini, gpt-5-mini to confirm the best balance of quality and response
time before the list is locked down. Ollama keeps its live-fetched list since
users control what is installed locally.

### Art Assets and UI Polish

~95 art assets generated and placed in `public/assets/` subfolders. Kiro tasks
written covering: wiring all assets into their Vue components (backgrounds,
hero banners, panel art, thumbnails, portraits, nav icons, button art, ornaments),
the `v-bg-asset` directive and `AircaneImg` / `NavIcon` / `ThumbnailPlaceholder`
fallback components, and layout consistency fixes (common page template, sidebar
width, duplicate headings, background removal from pages that should be clean).
Previously blocked only on `new-character-button-art.png` regeneration for the
Characters page; that is no longer a blocker, because the Characters UI is hidden
(see Stretch Goals: Characters and character creation). All other assets are
ready to wire.

### Playwright E2E in CI

The Playwright end-to-end suite is not run by the usual CI checks and has needed
manual fixes after navigation changes (PRs #24, #26, #36). Add it to the CI
workflow so navigation regressions are caught automatically. The suite itself
exists; the work is wiring it into the CI pipeline and fixing any currently
broken specs.

### Adventure Prompt Rule Enforcement

The current AI DM prompt rules (mandatory roll triggers, detection checks, exit
descriptions, end-of-narration player prompt) are instructions — the model can
still ignore them. Known gaps: the AI can narrate an auto-success for an action
that should have required a roll, or end narration without a clear player prompt.

Options for making these reliable:

- **Server-side detection** — after receiving the AI response, check whether a
  mandatory-roll scenario was narrated without a `RequestRoll` action and flag or
  retry the response.
- **Structured "what next" step** — split the AI DM loop into narration + a
  separate structured decision step that forces the model to choose between
  RequestRoll, AdvanceScene, or NarrateOnly explicitly.

### In-App Help / How-To Menu

Add a Help menu item that surfaces user-facing how-tos and guidance in-app (rather than only in
`docs/`). First content to include: **character import instructions per source** (Pathbuilder 2e,
D&D Beyond URL + saved file, Foundry VTT 5e/PF2e, Roll20, Generic VTT, and PDF via the unified
**Import Character** modal) — the detailed step-by-step was drafted but intentionally not written to
`docs/` because it belongs in this feature. Likely also covers library import, hosting a session,
and AI provider setup. Surface it in the app shell (and the Tauri tray where it fits).

### Streaming Narration (All Providers)

Ensure token-by-token streaming works consistently across all AI providers and UI modes.

### Mobile-Optimized UI

Responsive redesign targeting phone and tablet screens for player-side usage.

### Native Player Companion App (iOS / Android)

A lightweight native app for players connecting to an Aircane session from a phone or
tablet. The host always runs the desktop app; this is purely the player-side view.
Planned features: push notifications for turn prompts and roll requests, offline character
sheet viewing, native dice roller with haptics, QR code scanner for session join, and
background session persistence. Built against the existing SignalR and REST API — no
backend changes required.

**Technology decision: Flutter.**

- Single codebase for both iOS and Android.
- Dart is close enough to TypeScript that the learning curve is shallow.
- Flutter compiles to native ARM — smooth animations and haptics without a JS bridge.
- Riverpod for state management (similar mental model to Pinia).
- SignalR: `signalr_netcore` community Dart client, or raw WebSocket against the existing hub.
- HTTP: `dio` (equivalent to axios).
- Push notifications: Firebase Cloud Messaging (FCM) — works for both platforms.
- QR scanner: `mobile_scanner`.
- Token storage: `flutter_secure_storage`.
- No backend changes required — the companion app consumes the existing REST + SignalR API.

Planned for after the main app (desktop wrapper + internet tunnel, both now delivered)
stabilises and real player feedback is available.

### Automatic Folder Watching

Add a filesystem watcher that detects new or changed files in registered folders and triggers re-indexing without manual scans.

### Advanced PDF Layout Parsing

Handle multi-column layouts, tables, sidebars, and complex formatting for more accurate text extraction from rulebooks.

### Multi-Turn AI Memory

Persistent AI memory across sessions beyond what is stored in campaign state - long-term NPC relationship tracking, world knowledge graphs, and player preference learning.

### Session Export and Replay

Export full session logs, event history, and summaries in a portable format for archival or sharing. (Adventure runs now
keep a persisted transcript and campaign event log, which this can build on.)

---

## Priority 4 - Exploratory / Long-Term

### Query Decomposition (Agentic Retrieval)

Complex multi-part questions (e.g. "If a Level 3 Wizard casts Misty Step as a bonus action, can they
cast Fireball with their main action?") fail to surface the specific rule chunk because keyword/vector
scoring spreads across all concepts in the question rather than focusing on the key constraint.
Diagnosed via the Misty Step/Fireball test: the correct chunk (PHB p.184, "Bonus Action" spellcasting
restriction — "You can't cast another spell during the same turn, except for a cantrip with a casting
time of 1 action") is retrievable via a targeted diagnostic query but missed by the natural-language
question. Fix: decompose complex questions into sub-queries before retrieval, retrieve separately, then
combine results. This is the agentic RAG approach — exposing `retrieve_rules()`, `retrieve_scene()`, and
`get_character_state()` as tool calls the AI DM can invoke inside `AiRuntimeService`/`PlayerActionService`,
keeping agentic latency out of simple rule-question queries.

### Stat Block Retrieval Follow-ups

Follow-ups to the stat block chunker: (1) **Cross-page stat blocks** — chunking is per page, so a block that
runs onto the next page is cut there and its continuation (no header) is treated as prose; carry an open stat
block across the page boundary in `DocumentImportJob`. (2) **Use `ChunkType`** — `stat-block` chunks are stored
but retrieval ignores the type; boost them (or filter to them) for creature questions such as "what are the
stats of a giant crocodile", and consider showing the creature name in citations. (3) **Name quality** — names
come from OCR text, so some are noisy (`Co RELLON LARETHIAN ELF CLERIC OF`, `Yug0loths`) or lost entirely;
a cleanup or lookup against the book's own monster index could fix them. (4) **Re-import tooling** — there is
no "re-chunk this document" endpoint, so applying a chunking change means deleting and re-adding the document
(which loses manual tags); a re-chunk-in-place job alongside `reembed` and `reocr` would avoid that.

### Vector Column Dimension Fix (OpenAI 1536-dim Embeddings)

The pgvector column is currently hardwired to `vector(768)` (matching `nomic-embed-text` and
`text-embedding-3-small` at 768 dims). OpenAI's `text-embedding-3-small` produces 1536-dim vectors
which require a column migration and full re-embed to use. The startup compatibility check already
blocks mismatched providers gracefully. Unblocks full OpenAI embedding quality for RAG retrieval.
Migration path: add `EmbeddingV2 vector(1536)` column alongside existing, re-embed into it, swap
search path, drop old column — or manual `ALTER TABLE` + re-embed via the existing reindex endpoints.

### Deterministic Adventure Outline Templates

Reduce token cost and improve consistency in adventure generation by combining LLM calls with structured outline templates.

### Plugin / Extension System

Allow community-contributed rulesets, character sheet layouts, and AI prompt templates.

### Multi-Language Support

Localize the UI and support non-English source documents.

---

## Stretch Goals

Features that are built, or partly built, but are not essential to Aircane today. They are parked on purpose, not
abandoned: the code stays in the repository, and where there is a UI it is hidden behind a feature flag
(`src/frontend/aircane-web/src/shared/features.ts`, documented in `docs/setup/feature-flags.md`).

### Characters and character creation

**Decision (2026-10-23):** character management and creation is a stretch goal, not essential for Aircane.
Aircane already works with no characters. Group and solo runs are played with players who have no character
assigned, and the AI treats character sheets as external (the "NO CHARACTER ATTACHED" prompt blocks, and roll
requests that need no linked character). So the Characters UI is **hidden, not removed**: all character code,
routes, backend endpoints and database data are intact.

**What is hidden while the flag is off:** the sidebar item; the dashboard "Character Sheets" card and "Create
Character" quick action; the `/characters` and `/characters/:characterId/field-review` routes (they redirect to the
dashboard); the host's Participants-panel Character column and assign dropdown; the group lobby's "as <character>"
line; the player's Character section on the Party tab (which instead shows whose turn it is); and the character
wording in the remove-player and replace-seat confirmations. Left as they were: the Adventure Forge (it already sends
no `characterIds`), the library upload form's "Character" source type, and the game system definition editor's
character schema section (a follow-up decision: hide it, or keep it because the schema will drive the future form).

**How to re-enable the UI:** set `VITE_ENABLE_CHARACTERS=true` (see `.env.example` and
`docs/setup/feature-flags.md`) and restart the dev server or rebuild. Nothing else changes.

**What a future implementation has to deal with:**

- **The validator is D&D-shaped.** `CharacterSchemaValidator` requires at least one class, a class level of 1 to 20
  (and a total level of 1 to 20), a hit die above 0, and ability scores of 1 to 30. `CharacterService` derives
  `Level` from the sum of the class levels.
- **The form is D&D-only.** `CharacterForm.vue` has six abilities, a computed D&D proficiency bonus, and requires a
  class and a level.
- **Level is not optional in the data model.** `Character.Level` is a non-nullable `int` column and
  `CreateCharacterRequest.Level` is non-nullable, so a system with no levels needs a migration (and a decision on
  whether `Level` becomes nullable).
- **Every import mapper assumes levels.** The Foundry, D&D Beyond, Pathbuilder, Roll20 and PDF mappers all produce
  `Classes[].Level`, and the draft sanitiser clamps levels to 1 to 20.
- **`PartyAnalysisService` is D&D-only and unreachable from the UI.** The Adventure Forge sends no `characterIds`, so
  party analysis never runs.
- **A likely direction.** Use the game system definition's character schema as the validator when one exists; make
  class, level and hit die optional; render the form from the schema (the import review already does this with the
  FormDescriptor renderer); and decide whether `Level` becomes nullable.
- **The AI side is already ready.** The roll prompts assume external sheets, so characters can come back without
  touching them.

---

_Last updated: 2026-10-23 — Characters UI hidden behind a feature flag and recorded as a
stretch goal. Earlier (2026-10-18): first-launch setup wizard delivered (PR #7, audited
PR #37); group play, curated model list, art assets/UI polish, Playwright CI, and
adventure prompt enforcement added to backlog. Earlier (2026-10-18): adventure
forge, campaign integration and play polish delivered (async generation with
progress toast, adventures on campaign pages, unassigned list, narration
persistence and resume with AI recap, roll/detection/exit prompt rules, resilient
AI reply parsing, fake AI in integration tests; PRs #19–#36). Earlier
(2026-10-12): solo adventure execution delivered. Earlier (2026-10-11): structured
stat block chunker delivered (RAG item 11). Earlier (2026-10-11): RAG pipeline
optimization delivered (items 1–10). Earlier (2026-10-04): source-specific
character import adapters delivered (PR #15). Earlier (2026-10-04): in-app
feedback feature delivered. Earlier (2026-10-01): Internet Tunnel / Remote Play
delivered. Earlier (2026-09-29): Flutter chosen for native mobile companion app._
