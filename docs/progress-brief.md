# Aircane Tabletop — Claude Handoff Brief

> **How this works:** Shawn pastes this file at the start of a new Claude session to restore
> full project context. Claude uses it to have productive design/planning conversations, then
> Shawn hands implementation work to AWS Kiro. Update this file after each significant session.
>
> **Last updated:** 2026-09-25 (late — Priority-1 backlog session)
> **MVP status:** ✅ Complete — all 9 phases shipped.
> **Phase 10 (Built-in Rules Content Bundle):** ✅ Complete — 10.1–10.8 done & verified
> (embedded bundles, license metadata, `LicensesController`, attribution UI, tests).
> **Priority-1 backlog:** ✅ All 6 items delivered this session (see "Latest Session" below).
> Work is on branch `dev` (pushed); commits `e12e1ad → 11a37c4`. Backend suite green:
> **1826 unit + 18 integration.**

---

## Latest Session — Priority-1 Backlog (all 6 delivered)

Autonomous session working the P1 backlog. Each item was committed as a logical green group
and pushed to `dev`. Full backend suite green after each (1826 unit + 18 integration).

1. **Embedding Provider Portability — core** (`e12e1ad`). Each `DocumentChunk` now records
   `EmbeddingProvider` / `EmbeddingModel` / `EmbeddingDimensions` at generation time (seeder,
   import job, re-embed). Vector search **excludes chunks whose provenance ≠ the active
   provider** and logs a "re-index required" warning (null provenance treated as compatible),
   so a provider/model change no longer silently returns garbage. Re-embed endpoints:
   `POST /api/library/documents/reembed-all` and `.../{id}/reembed`. The "ship text, embed on
   first run" contract is documented in `docs/architecture/overview.md`.
   *Deferred:* flexible column dimension (still hardwired `vector(768)`) and cloud embedding
   providers — both gated behind the dimension work. Async re-embed depends on the new
   Background Job Infrastructure backlog item (the app has **no** job runner yet — import,
   folder scan, reindex, re-embed all run synchronously on the request thread).

2. **Open-Content Compliance — About/Credits panel** (`2dc0c1c`). New `/about` view + sidebar
   link (`features/about/AboutView.vue`) reusing the public `/api/library/licenses` list. Shows
   the ORC Notice, CC BY / OGL / ORC attributions, an explicit **ORC-Content-vs-Reserved-Material
   downstream declaration**, and a trademark note. *Still open (maintainer decision):* the
   **application source-code license** — README still says "License TBD"; the About panel points
   users to the README for it.

3. **Advanced Combat Automation — backend** (through `8c5a729`). A pure `CombatEngine`
   (`Aircane.Domain/Combat/`) + an `EncounterState` model persisted **in campaign state JSON**
   (so combat joins undo/replay/broadcast) drives initiative order, turn advancement (round wrap
   + condition tick), per-instance condition durations, HP damage/healing (temp-HP absorption,
   downed-PC death saves, NPC death), and death-save resolution. New combat commands
   (StartEncounter, RollInitiative, AdvanceTurn, TickConditions, ApplyDamage, ApplyHealing,
   ApplyCondition, RemoveCondition, DeathSave) flow through the existing **AI-proposes → validate
   → authority → approval** pipeline; turn changes broadcast over SignalR (`CombatTurnChanged`,
   nullable-string IDs — PCs use Guid-string, NPCs use slugs). **Also fixed a latent bug:**
   ApplyDamage/ApplyHealing/ApplyCondition previously no-op'd via a generic state merge.
   *Deferred:* feed live combat state into the AI DM prompt (`PlayerActionService`); frontend
   initiative/turn/HP tracker UI; concentration checks; out-of-turn action enforcement.

4. **Pathfinder 2e Adapter — game-system definition** (through `926ff5f`). New
   `Pathfinder2eRemasterSeed` definition (DefinitionId `…0003`) capturing four degrees of success
   (margin vs DC), three-action economy + multiple-attack penalty, creature-level encounter
   budget, PF2e conditions, and proficiency ranks — validates against `GameSystemDefinitionValidator`.
   New `GameSystemDefinitionSeeder` (wired in `Program.cs`) **persists all built-in definitions**
   (D&D 5e, Freeform, PF2e Remaster) on startup — this fixed a gap where built-in defs were
   referenced by the migration service but never actually inserted. All three verified in the DB.
   *Deferred:* PF2e-specific encounter validator + generation (still uses `Dnd5eEncounterValidator`);
   an elite/weak mechanical applicator; and the ORC **rules text** (separate, user-blocked).

5. **OCR Pipeline** (`e7b720c`). New `IOcrEngine` abstraction; `TesseractOcrEngine` (TesseractOCR
   5.5.2 → Tesseract 5) that lazily initializes and **gates cleanly to unavailable when the native
   lib or `tessdata` is missing — never throws**; `NullOcrEngine` when disabled. `PdfPigTextExtractor`
   now takes `IOcrEngine`: on low/no-text pages it pulls the page's embedded raster images
   (`TryGetPng` / `RawBytes`), OCRs them, keeps whichever text is longer, and recomputes the
   OCR-required flag. **Off by default** (`Ocr:Enabled=false`; also `TessdataPath`, `Language`,
   `MinConfidence=0.3`). Docs: `docs/setup/ocr.md` + getting-started link. 7 new tests.
   *Deferred:* full-page rasterization (PDFium/Ghostscript) for vector-glyph PDFs; bundled/auto-download
   `tessdata`; a re-OCR-on-enable action (ties into Background Jobs).

6. **Maintainer-decision docs** (`11a37c4`). Added an "Intentionally Not Auto-Built" decision table
   to `docs/backlog.md` recording *why* four items were skipped and *what unblocks each*: PF2e real
   ORC text (licensing — Foundry/Obsidian sources are **not** ORC), Internet Tunnel (security-sensitive),
   Desktop Wrapper (new toolchain), and the app source-code license (owner decision).

**Suggested next focus (not started):** Priority-2 items are larger/architectural (Map/Battlemap,
AWS deploy, File Upload source mode, User Accounts, SQLite). None are blocked, but scope should be
agreed before starting. Lower-effort high-value follow-ups from P1: the **frontend combat tracker UI**
+ feeding combat state into the AI prompt (completes the combat feature end-to-end), and the
**Background Job Infrastructure** foundation (unblocks async import/reindex/re-embed).

---

## What We're Building

**Aircane Tabletop** — a local-first, AI-assisted tabletop RPG engine that supports *any*
TTRPG through data-driven Game System Definitions. No code changes needed to add a new system.
Ships with D&D 5e 2014 and a generic freeform definition as starters.

**Four pillars:**
- **Import** — rules PDFs, adventure PDFs, solo modules, characters, homebrew (folder-watching model — files never copied)
- **Run** — AI assistant / co-DM / full DM with configurable authority, dice, event-sourced campaign state
- **Host** — local server, LAN joins via browser (no install for players), invite codes + QR
- **Create** — AI-generated solo and group adventures (Adventure Forge)

**Not a chat interface with PDFs.** A structured RPG engine where the AI is embedded inside
rules retrieval, adventure state, dice, character data, and session hosting.

---

## Tech Stack (as built)

| Layer | Technology |
|-------|-----------|
| Frontend | Vue 3 + TypeScript + Vite + Pinia + Tailwind + SignalR TS client |
| Backend | ASP.NET Core 8, C#, SignalR, EF Core |
| Database | PostgreSQL 16 + pgvector |
| AI Providers | OpenAI + Ollama implemented; Azure / Bedrock / Grok stubbed → fall back to Fake |
| Dev/Test | Docker Compose, xUnit, Playwright, Testcontainers |

**Backend layers (clean architecture):**
`Aircane.Domain` → `Aircane.Application` → `Aircane.Infrastructure` → `Aircane.Api` → `Aircane.Workers`

**Frontend feature folders:**
`library`, `campaigns`, `characters`, `sessions`, `dice`, `ai`, `adventure-generation`, `game-systems`, `shared`

---

## What's Built (MVP Complete)

### Phase 0 — Foundation
Repo structure, ASP.NET Core solution + health endpoint, Vue 3 app, Docker Compose
(Postgres+pgvector, optional Redis), CI workflows, Kiro steering files.

### Phase 1 — Domain & Persistence
Core EF Core entities: `Campaign`, `Session`, `Participant`, `SourceDocument`, `DocumentChunk`,
`Character`, `Roll`, `CampaignEvent`, plus `WatchedFolder` for folder-based library.
pgvector embedding column on `DocumentChunk`, repository/service abstractions.

### Phase 2 — Library & Document Import
Folder-watching model (files never copied). Watched-folder registration API, source
classification (Rules/Adventure/Solo/Character/Homebrew/Generated/Unknown) inherited from
folder defaults. PDF text extraction (UglyToad.PdfPig), OCR deferred. Chunking with overlap +
page refs. Embedding provider interface + fake provider for tests. Semantic/keyword search with
visibility filtering. `IDocumentSource` abstraction: `FolderWatchDocumentSource` implemented,
`UploadDocumentSource` stubbed for future cloud. Folder scan job, source-availability tracking,
import status UI + SignalR `ImportStatusUpdated` events.

### Phase 3 — Campaigns, Characters, Templates
Campaign CRUD, canonical character JSON schema, manual character creation, JSON import +
validation, PDF form/text extraction → draft characters, field review/mapping UI, template
save/load, character-list-by-campaign endpoint (host sees all, player sees own only).

### Phase 4 — Dice & Session Hosting
Dice expression parser: `d20`, `NdM`, modifiers, keep-highest/lowest, advantage/disadvantage.
Dice roller service + persistence, manual roll entry. Session creation with access mode +
invite code. Session end + AI-resume endpoints. SignalR session hub (join/leave/chat/roll/
state). LAN join screen with QR code. Participant approval + role/character assignment.
**MVP signed session tokens** — validated on every API + SignalR call, reconnect support,
in-memory revocation on session end. Host and player session screens.

### Phase 5 — AI Provider & RAG Rules Assistant
AI provider abstraction (chat + structured output). OpenAI implemented. **Multi-provider
settings UI** with server-side key management: keys masked to last-4 chars, stored via env /
.NET user secrets / gitignored local `ai-settings.json`, never in DB or returned unmasked,
test-connection endpoint. RAG context builder: retrieval + source priority + visibility
filtering. Rules question endpoint with citations. Rules answer UI. Low-confidence
"cannot confirm from available sources" path.

### Phase 6 — AI DM Runtime
AI role config: Assistant / Co-DM / Full DM / Hybrid. Authority config: Suggest Only / Ask
Before Applying / Auto-Apply Safe Actions / Full Session Control. Campaign state service.
**Event-sourced state changes** — `CampaignEvent` append + snapshot. AI structured output
schema (narration / citations / private note / proposed actions) with server-side validation.
**AI proposal queue** — host approve / edit / reject. Validated state commands: RequestRoll,
ApplyDamage, ApplyHealing, ApplyCondition, RevealContent, MoveScene. Undo for reversible
commands. Player action → RAG retrieval → AI response → proposal queue / auto-apply loop.

**The AI never directly mutates campaign state.** All changes go through validated,
server-side commands gated by the authority level and the host proposal queue.

### Phase 7 — Adventure Generation
Generation input form (ruleset / mode / party size / level / tone / length / difficulty /
combat-roleplay-exploration ratios). Party analysis service. Staged pipeline:
pitch → outline → scenes → NPCs → encounters → treasure → clues. Generated adventure package
model. Draft review UI (approve / edit / regenerate section / save). Placeholder 5e encounter
difficulty validation. Generated adventures indexed as searchable chunks. Adventure list +
delete endpoints.

### Phase 8 — Security & Hardening
Server-side authorization policies on every API endpoint and SignalR hub method.
`SourceFileAccessBlockerMiddleware` — no raw PDFs ever served publicly. Upload safety checks
(filename sanitize, extension/MIME validation, size limits). Folder path validation + traversal
prevention. Structured logging (imports / AI calls / auth failures / session events).
Integration tests (document import, session join, dice, rules lookup, AI proposal flow).
E2E tests (host starts table, player joins, dice roll, AI roll request, state update).

### Phase 9 — Packaging & Release
Production build scripts (`build.ps1` / `build.sh`). Production Docker files + compose
override. Local release profile. MVP smoke-test checklist. Post-MVP backlog documented.

---

## Phase 10 — Built-in Rules Content Bundle (🚧 In Progress)

Ships open-licensed rules text as built-in library content the RAG pipeline can retrieve out
of the box, with per-document license metadata and attribution obligations fulfillable via API.

**Done & verified at runtime (10.1–10.5):**
- `EmbeddedResourceDocumentSource` reads bundles compiled into `Aircane.Workers` as embedded
  resources. `SourceMode.Embedded` added.
- `SourceDocument` gained `IsBuiltIn`, `IsDisabled`, `LicenseKey`, `LicenseDisplayName`,
  `AttributionText`, `AttributionUrl`. Delete guard (built-in cannot be deleted), disable/enable,
  and RAG exclusion of disabled docs are wired. `BuiltInLicenses` typed constants for
  `cc-by-4.0`, `orc`.
- `BuiltInContentSeeder` (idempotent, runs from `Program.cs` after migrations) seeds the
  built-in bundles from real licensed content:
  - **D&D 5e SRD 5.1** (CC BY 4.0) — 313 chunks
  - **Pathfinder 2e Remaster** (ORC) — 2 chunks (⚠️ intentionally incomplete; see below)
- Seeder hardened: logs a clear error per missing manifest file; warns (does not throw) when a
  bundle yields fewer than 10 chunks so the app starts cleanly with a partial bundle.
- **PF1e removed (commercialization):** the Pathfinder 1e PRD bundle (OGL v1.0a) that previously
  shipped was **removed** to avoid the OGL Section 15 / content-identification burden — along with
  the `ogl-1.0a` license key, the OGL/Section-15 endpoints + reader, and the `IsOgl` DTO field.
  Users can still import their own PF1e PDFs via the folder-watching Library. See `docs/backlog.md`
  decision table.

**Migration-chain repair (done):** The EF migration history was broken — several migrations
lacked `.Designer.cs` files (no `[Migration]` attribute) so `MigrateAsync` silently ignored
them, and three model tables (`GameStateSnapshots`, `AiActionProposals`, `GeneratedAdventures`)
had no migration at all. Consolidated into a single recognized `ConsolidateModelDrift` migration
with a correct snapshot. Also fixed a fresh-DB pgvector bug: Npgsql cached its type catalog
before `CREATE EXTENSION vector` ran, so embedding writes failed — `Program.cs` now calls
`ReloadTypesAsync()` after migrations, before seeding.

**Done (10.6–10.8):**
- **10.6** — `LicensesController` (anonymous): `GET /api/library/licenses`; rules-question citations
  carry `LicenseKey` / `LicenseDisplayName` / short attribution. (The OGL-text / Section-15
  endpoints were removed with the PF1e bundle.)
- **10.7** — attribution UI: license modal + badges in the library, plus the new
  **About & Credits** page (`/about`) added in the latest session.
- **10.8** — integration/regression tests for seeding, delete guard, disable/enable, RAG
  scoping, and the license endpoints.

**Known gaps / follow-ups:**
- **PF2e content is a stub** (2 chunks), pending properly-licensed ORC source text. Two sources
  were evaluated and are **not** usable: the Obsidian TTRPG Community repo (Paizo Community Use
  Policy, not bundleable) and the Foundry VTT PF2e system data — the Foundry system's Pathfinder
  content is used under a private Paizo↔Foundry partnership agreement and Paizo's Community Use
  Policy, **not** the ORC License, so extracting its packs and shipping them as ORC content in
  Aircane would misrepresent the license and likely breach Community Use. Real PF2e Remaster text
  must be sourced from an actual ORC-licensed release (the same content-sourcing step used for the
  D&D SRD), then dropped into the existing `pf2e_remaster` bundle. No code is needed —
  the bundle structure, ORC license file, manifest, and seeder already work and the seeder handles
  the partial bundle gracefully.
- **Dev RAG now uses real embeddings (local dev only).** `appsettings.Development.json` was
  switched from the Fake embedding provider to **Ollama `nomic-embed-text`** (768-dim), and the
  dev DB was re-seeded so all chunks carry real vectors. Verified end-to-end: the "grapple"
  query returns D&D SRD citations (all `cc-by-4.0`), correctly scoped by game system, with the 10.6
  license metadata populated. Note: the shipped defaults are unchanged — non-Development `appsettings.json` still
  defaults `Embeddings:Provider=Fake`; Production already defaults to Ollama.

### ⚠️ Embedding provider coupling (mostly addressed — see below)

Embeddings are **locked to the provider + model that generated them** — queries must be embedded
with the same provider/model or retrieval returns wrong/empty results. Today only Ollama
(`nomic-embed-text`, 768-dim) and a Fake test provider exist; there is **no OpenAI embedding
provider**, and an AI *chat* key does not enable embeddings.

**Addressed this session (see Latest Session #1):** each chunk now records provider/model/dimension,
vector search **guards against provenance mismatch** (skips mismatched chunks + logs a re-index
warning instead of returning garbage), and there are first-class re-embed endpoints. The
"ship text, embed on first run" contract is documented in `docs/architecture/overview.md`.

**Still open (backlog):** the `DocumentChunk.Embedding` column is still fixed at `vector(768)`, so
adding a different-dimension provider (e.g. OpenAI `text-embedding-3-small` at 1536) needs a
configurable/migrated column dimension + a full re-embed. Making re-embed/import/reindex asynchronous
depends on the new **Background Job Infrastructure** item (no job runner exists yet). User-facing
warnings are in `docs/setup/ai-configuration.md` and `docs/known-limitations.md`.

---

## Key Design Decisions (Essential Context)

| Decision | What and why |
|----------|-------------|
| **Folder-watching, not upload** | Source files stay on host disk. App stores paths + derived data (chunks, embeddings). `UploadDocumentSource` stub reserved for future cloud mode. |
| **AI never directly mutates state** | Structured JSON output → server-side validation → command execution → gated by authority level → host proposal queue. |
| **Event-sourced campaign state** | `CampaignEvent` append-only log. Full undo and audit trail. Current state derived from or snapshotted from events. |
| **Provider abstraction is mandatory** | `IAIProvider` / `IEmbeddingProvider` interfaces. Default is a deterministic **Fake** provider — app works with zero API keys. |
| **Local-first key storage** | Env vars / .NET user secrets / gitignored `ai-settings.json`. Never in DB, never returned unmasked, never logged. Shared/hosted mode would require Secrets Manager + per-user isolation. |
| **System-agnostic core** | Game System Definitions (JSON/YAML) drive dice conventions, character schema, conditions, action economy, encounter rules — declaratively, no code changes per system. |
| **RAG is standard vector similarity today** | Embed chunks, cosine search (pgvector), visibility filter, stuff into context. Works for simple rule lookups. Multi-hop rule chains are the current weak spot. |

---

## Known Limitations (Deliberate MVP Scope)

- **OCR is off by default and image-only** — a Tesseract pipeline now exists (`Ocr:Enabled`), but
  it needs native libs + `tessdata`, gates cleanly to unavailable if they're missing, and only OCRs
  images already embedded in the PDF (no full-page rasterization yet). Scanned PDFs stay `OcrRequired`
  when OCR is off/unavailable.
- **Combat automation is backend-only** — engine (initiative, turns, conditions, HP, death saves) is
  delivered behind the command pipeline, but there's **no combat tracker UI** and the AI prompt is
  **not yet fed live combat state**.
- **LAN only** — no HTTPS on LAN, no internet tunnel yet, no persistent user accounts.
- **In-memory token revocation** — lost on server restart.
- **Only OpenAI + Ollama fully wired** (chat); **only Ollama + Fake** for embeddings. Azure/Bedrock/Grok
  fall back to Fake. No OpenAI embedding provider yet (blocked on flexible embedding dimension).
- **Default provider is Fake** — deterministic placeholder; no real AI without config.
- **Encounter validation is D&D-5e placeholder logic** — PF2e has a game-system *definition* but no
  PF2e-specific encounter validator/generation yet.
- **PF2e Remaster rules text is a 2-chunk stub** — the mechanics definition ships, but real
  ORC-licensed rules text is still needed (see decision table in `docs/backlog.md`).
- **PDF character import is best-effort** — form-fillable + simple text-layer only.
- **No background job runner** — import/folder-scan/reindex/re-embed run synchronously on the request thread.
- **No cloud/upload mode, no desktop wrapper, no mobile UI, PostgreSQL required (no SQLite).**

---

## Post-MVP Backlog

**P1 — status after the latest session** (full detail + "not auto-built" decision table in `docs/backlog.md`):
- ✅ Pathfinder 2e adapter — *definition delivered; PF2e-specific validation/generation + ORC text still open*
- ✅ OCR pipeline (Tesseract) — *delivered, off by default; full-page rasterization still open*
- ✅ Advanced combat automation — *backend delivered; tracker UI + AI-prompt integration still open*
- ✅ Embedding portability (core) + About/Credits compliance panel
- ⛔ Internet tunnel / remote play — *not auto-built: security-sensitive (maintainer decision)*
- ⛔ Desktop wrapper (Tauri/Electron) — *not auto-built: new toolchain (maintainer decision)*
- ⛔ App source-code license — *not auto-built: owner decision (README "License TBD")*
- 🔜 **Background Job Infrastructure** (new) — single job runner (Hangfire/Quartz or in-process
  hosted-service + channel queue) for import/folder-scan/reindex/re-embed; unblocks async work above.
- 🔜 PF2e-specific encounter validator + generation; frontend combat tracker + AI combat-state prompt.

**P2:**
- Map / battlemap support
- AWS cloud deployment
- File upload source mode (`UploadDocumentSource` implemented)
- User accounts
- SQLite option

**P3:**
- Streaming AI narration
- Mobile-optimized UI
- Automatic folder watching (file-system watcher)
- Advanced PDF layout parsing
- D&D Beyond / VTT import
- Pathbuilder 2e character import (JSON export → existing JSON character import path)
- Multi-turn AI memory
- Rate limiting
- Persistent token revocation
- Session export / replay

**P4:**
- RAG reranking (cross-encoder over pgvector results)
- Deterministic adventure outline templates
- Plugin / extension system
- Multi-language support

---

## Active Design Discussion: RAG Upgrade Path

The current retrieval is standard vector similarity (embed → cosine search → filter → context).
Three options were evaluated:

| Approach | Strength | Weakness |
|----------|----------|----------|
| Standard RAG (current) | Fast, simple, great for single-rule lookups | Weak on multi-hop rule chains |
| Graph RAG | Excellent for cross-referenced rules | High build cost, complex to maintain |
| Agentic RAG | Most powerful for complex queries | Latency (1–5s), expensive per call |

**Agreed direction — hybrid in three stages:**
1. **Reranking first** — cross-encoder reranking on existing pgvector results. Biggest quality
   gain for smallest effort. Slots into `RagService` with no schema changes.
2. **Rules cross-reference graph** — `ChunkRelationship` join table in PostgreSQL linking SRD
   "see also" references and condition cross-references. Graph traversal step in
   `RagService.QueryAsync`. Not full GraphRAG — just edges where it counts.
3. **Agentic loop only for the AI DM** — expose `retrieve_rules()`, `retrieve_scene()`, and
   `get_character_state()` as tool calls the AI can invoke inside `AiRuntimeService` /
   `PlayerActionService`. Keeps agentic latency out of simple rule-question queries.

**Not decided yet:** Which reranker model to use (local Ollama reranker vs. a cross-encoder
API); whether to use `Microsoft.SemanticKernel` or a hand-rolled RAG upgrade.

---

## How to Use This Brief

**Starting a new Claude session:**
Paste this file (`docs/progress-brief.md`) as your first message, then say what you want to
work on. Claude will have full context on what's built, what the patterns are, and what's
being discussed.

**Keeping it current:**
After a productive session, ask Claude: *"Update the brief with what we discussed today."*
Claude will produce a revised version of this file. Paste the updated version back to Kiro
with: *"Update `docs/progress-brief.md` with this content."*

**Sections to update most often:**
- `Last updated` date
- `Active Design Discussion` — replace with whatever is currently being worked through
- `Post-MVP Backlog` — promote items as they move to active
- `Known Limitations` — remove items as they're resolved
