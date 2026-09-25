# Aircane Tabletop — Claude Handoff Brief

> **How this works:** Shawn pastes this file at the start of a new Claude session to restore
> full project context. Claude uses it to have productive design/planning conversations, then
> Shawn hands implementation work to AWS Kiro. Update this file after each significant session.
>
> **Last updated:** 2026-09-25
> **MVP status:** ✅ Complete — all 9 phases shipped.
> **Phase 10 (Built-in Rules Content Bundle):** 🚧 In progress — 10.1–10.5 done & verified at
> runtime; 10.6 (license/attribution API) underway; 10.7 (attribution UI) and 10.8 (tests) pending.

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
  `cc-by-4.0`, `orc`, `ogl-1.0a`.
- `BuiltInContentSeeder` (idempotent, runs from `Program.cs` after migrations) seeds three
  bundles from real licensed content:
  - **D&D 5e SRD 5.1** (CC BY 4.0) — 313 chunks
  - **Pathfinder 1e PRD** (OGL v1.0a, with `OGL-1.0a.txt` + `SECTION-15.txt`) — 607 chunks
  - **Pathfinder 2e Remaster** (ORC) — 2 chunks (⚠️ intentionally incomplete; see below)
- Seeder hardened: logs a clear error per missing manifest file; warns (does not throw) when a
  bundle yields fewer than 10 chunks so the app starts cleanly with a partial bundle.

**Migration-chain repair (done):** The EF migration history was broken — several migrations
lacked `.Designer.cs` files (no `[Migration]` attribute) so `MigrateAsync` silently ignored
them, and three model tables (`GameStateSnapshots`, `AiActionProposals`, `GeneratedAdventures`)
had no migration at all. Consolidated into a single recognized `ConsolidateModelDrift` migration
with a correct snapshot. Also fixed a fresh-DB pgvector bug: Npgsql cached its type catalog
before `CREATE EXTENSION vector` ran, so embedding writes failed — `Program.cs` now calls
`ReloadTypesAsync()` after migrations, before seeding.

**Remaining:**
- **10.6 (in progress)** — `LicensesController`: `GET /api/library/licenses` (public, no auth),
  `GET /api/library/licenses/{documentId}/ogl-text`, `.../section-15`; extend rules-question
  citations with `LicenseKey` / `LicenseDisplayName` / short attribution.
- **10.7** — attribution UI (license modal, badges, Disable/Enable toggle, "Restore defaults").
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
  D&D SRD and PF1e PRD), then dropped into the existing `pf2e_remaster` bundle. No code is needed —
  the bundle structure, ORC license file, manifest, and seeder already work and the seeder handles
  the partial bundle gracefully.
- **Dev RAG now uses real embeddings (local dev only).** `appsettings.Development.json` was
  switched from the Fake embedding provider to **Ollama `nomic-embed-text`** (768-dim), and the
  dev DB was re-seeded so all ~922 chunks carry real vectors. Verified end-to-end: the "grapple"
  query returns 5 D&D SRD citations (all `cc-by-4.0`) and "combat maneuvers" returns 4 PF1e
  citations (all `ogl-1.0a`), correctly scoped by game system, with the 10.6 license metadata
  populated. Note: the shipped defaults are unchanged — non-Development `appsettings.json` still
  defaults `Embeddings:Provider=Fake`; Production already defaults to Ollama.

### ⚠️ Embedding provider coupling (important design constraint)

Embeddings are **locked to the provider + model that generated them** — queries must be embedded
with the same provider/model or retrieval silently returns wrong/empty results. Today only Ollama
(`nomic-embed-text`, 768-dim) and a Fake test provider exist; there is **no OpenAI embedding
provider**, and an AI *chat* key does not enable embeddings. The `DocumentChunk.Embedding` column
is fixed at `vector(768)`. Consequences: seeding with Ollama makes it a de facto dependency for
retrieval; removing/changing the embedding provider or model requires a **manual re-index/re-seed**;
there is no mismatch detection or one-click re-embed yet.

**Durable fix (backlog, P1):** ship text and embed on first run (never ship pre-computed vectors),
record provider/model/dimension alongside each embedding, add a query-time mismatch guard, provide
a first-class re-embed path, and make the column dimension flexible so cloud embedding providers
(e.g. OpenAI at 1536-dim) can be added. User-facing warnings are documented in
`docs/setup/ai-configuration.md` and `docs/known-limitations.md`.

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

- **No OCR** — scanned PDFs flagged as OCR-required but not processed.
- **LAN only** — no HTTPS on LAN, no internet tunnel yet, no persistent user accounts.
- **In-memory token revocation** — lost on server restart.
- **Only OpenAI + Ollama fully wired** — Azure/Bedrock/Grok fall back to Fake.
- **Default provider is Fake** — deterministic placeholder; no real AI without config.
- **No combat automation** — AI can request rolls and propose damage, but no initiative tracker, turn enforcement, or condition-duration tracking.
- **Encounter validation is placeholder logic.**
- **PDF character import is best-effort** — form-fillable + simple text-layer only.
- **No cloud/upload mode, no desktop wrapper, no mobile UI, PostgreSQL required (no SQLite).**

---

## Post-MVP Backlog

**P1 — Next meaningful features:**
- Pathfinder 2e adapter
- OCR pipeline (Tesseract, already stubbed)
- Internet tunnel / remote play (Cloudflare Tunnel)
- Desktop wrapper (Tauri or Electron)
- Advanced combat automation (initiative tracker, condition-duration, turn enforcement)

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
