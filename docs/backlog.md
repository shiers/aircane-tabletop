# Post-MVP Backlog

This document lists planned features and improvements beyond the Aircane Tabletop MVP, organized by priority.

---

## Intentionally Not Auto-Built (Maintainer Decision Required)

The following backlog items were **deliberately left for a human maintainer** rather than
implemented autonomously, because each is blocked on a decision, carries security risk, or
needs a toolchain/content that cannot be safely chosen without owner input. They remain in
the priority lists below with full detail; this section records *why* they were skipped and
*what unblocks them*.

| Item | Why not auto-built | What unblocks it |
|------|--------------------|------------------|
| **PF2e Remaster real ORC text** | Content/licensing decision. The obvious data sources (Foundry PF2e, Obsidian TTRPG Community) are **not** ORC-licensed — they fall under a private Paizo↔Foundry agreement and Paizo's Community Use Policy, so bundling them as ORC content would be a licensing violation. | Maintainer sources text from a genuine ORC release and drops it into the existing `pf2e_remaster` bundle (no code changes needed). |
| **Internet Tunnel / Remote Play** | Security-sensitive. Exposing a local-first server to the public internet needs deliberate choices about the tunneling provider, HTTPS, auth hardening, rate limiting, CSRF, and durable token revocation — not something to enable silently. | Maintainer chooses the tunnel approach and signs off on the hardening checklist. |
| **Desktop Wrapper (Tauri/Electron)** | Toolchain decision. Requires adding a new build toolchain and packaging/signing pipeline (Rust+Tauri or Node+Electron), plus per-OS installer decisions. | Maintainer picks the wrapper stack and accepts the added build/release tooling. |
| **Application source-code license** | Owner decision. The README says "License TBD"; the code license is distinct from the content licenses (ORC/CC BY) and is the repository owner's call to make. | Maintainer chooses a license and updates the README + `LICENSE` file. The in-app About panel already points at the README for this. |
| **PF1e built-in content** | Removed for commercialization — OGL v1.0a adds a Section 15 attribution chain and a content-identification burden that adds legal complexity for a commercial product regardless of the source-code license chosen. D&D 5e SRD (CC BY 4.0) and PF2e Remaster (ORC) are commercial-friendly and retained. | Nothing to unblock — this is a deliberate removal, not deferred work. Users may still import their own PF1e PDFs via the folder-watching Library; only the bundled OGL rules text was removed. |

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
> **Not auto-built — owner decision.** See the decision table at the top of this file.

Decide and state the **application code license** — the README currently says "License TBD". This is kept distinct from the content licenses (ORC/OGL/CC BY). The in-app About / Credits panel (`/about`) already points users at the README for the app license; once chosen, update the README and add a `LICENSE` file. (The compliance surface itself — ORC Notice, CC BY / OGL attributions, ORC-Content-vs-Reserved-Material downstream declaration, and trademark note — is delivered in the About panel.)

### Internet Tunnel / Remote Play
> **Not auto-built — security-sensitive; maintainer decision.** See the decision table at the top of this file.

Allow players to connect over the internet without router port forwarding. Options include Cloudflare Tunnel, ngrok, or a custom relay service. Requires HTTPS, rate limiting, CSRF protection, and persistent token revocation.

### Desktop Wrapper
> **Not auto-built — needs a new build toolchain; maintainer decision.** See the decision table at the top of this file.

Package the app as a Tauri (or Electron) desktop application that starts the ASP.NET Core server, opens the local UI, manages file paths, and displays the LAN URL and QR code - one-click launch for non-technical hosts.

---

## Completed P1 Work (delivered — kept for reference)

The following Priority-1 items were implemented and verified (backend build clean; full backend
unit + integration suites and the frontend vitest suite green). They are recorded here rather than
in the active list above.

- **Background Job Infrastructure.** In-process `IBackgroundJobQueue` (`ChannelBackgroundJobQueue`) + a `BackgroundJobWorker` hosted service dispatching to `IJobHandler<T>` in fresh DI scopes, an in-memory job-status store, and `GET /api/jobs/{id}`. Document import, folder scan, and re-embed now enqueue and return `202 Accepted`; `ImportStatusUpdated` carries the `jobId`. A `BackgroundJobs:Runner` switch is reserved for a future Hangfire swap.
- **Advanced Combat Automation (surfacing).** The backend `CombatEngine`/`EncounterState` (initiative, turns, conditions, HP, death saves) is now surfaced end-to-end: a `CombatController` exposes host combat controls, `CombatTracker.vue` renders initiative/HP/conditions (interactive for the host, read-only with rough HP bands for players) driven by the `CombatTurnChanged` SignalR event, and `PlayerActionService` feeds a compact live-combat block into the AI DM prompt (via `IAiContextAdapter.BuildCombatContext`). *Still open (lower priority):* concentration checks and out-of-turn action enforcement.
- **Pathfinder 2e Adapter (generation logic).** `Pf2eEncounterValidator` (creature-level XP budget, Trivial/Low/Moderate/Severe/Extreme tiers) selected per campaign via `IEncounterValidatorSelector`; PF2e-aware adventure generation prompts (three-action economy, degrees of success, PF2e tiers/conditions, treasure-by-level); and an `EliteWeakAdjuster` with `ApplyEliteTemplate`/`ApplyWeakTemplate` commands. (The mechanics definition was delivered earlier; the ORC rules text remains a separate maintainer-gated item above.)
- **Embedding Provider Portability (dimensions + cloud providers).** Configurable embedding dimension with a startup compatibility check that disables vector search (keyword search still works) and logs a clear warning on a provider/column mismatch; `OpenAiEmbeddingProvider` (`text-embedding-3-small`, 1536) and `AzureOpenAiEmbeddingProvider` behind the existing `IEmbeddingProvider` abstraction. Provenance recording + mismatch guard + re-embed endpoints were delivered previously. Migration path documented in `docs/setup/ai-configuration.md`.
- **Library Import UX follow-ups.** Content-based duplicate detection (`SourceDocument.ContentHash` SHA-256 + `ExactDuplicate` flag), a `GameSystemAlias` table with seeded common aliases + canonicalizer alias lookup + management API, and configurable per-folder `WatchedFolder.ExcludePatterns` (glob) driving the likely-not-rules signal. (The review-before-import flow, quick import, filename suggestions, and canonicalization were delivered previously.)
- **OCR follow-ups.** Full-page rasterization via `IPdfRasterizer`/`DocnetPdfRasterizer` (PDFium, gated by `Ocr:FullPageRasterization`), an opt-in tessdata auto-downloader (`Ocr:AutoDownloadTessdata`), and re-OCR endpoints (`POST /api/library/documents/reocr[-all]`) with a "Re-run OCR" library UI button — all routed through the background job queue. (The Tesseract engine + embedded-image OCR were delivered previously.) See `docs/setup/ocr.md`.

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

---

## Priority 3 - Quality of Life

### Streaming Narration (All Providers)
Ensure token-by-token streaming works consistently across all AI providers and UI modes.

### Mobile-Optimized UI
Responsive redesign targeting phone and tablet screens for player-side usage.

### Automatic Folder Watching
Add a filesystem watcher that detects new or changed files in registered folders and triggers re-indexing without manual scans.

### Advanced PDF Layout Parsing
Handle multi-column layouts, tables, sidebars, and complex formatting for more accurate text extraction from rulebooks.

### D&D Beyond / VTT Integration
Import characters directly from D&D Beyond, Foundry VTT, or Roll20 via API or export formats.

### Pathbuilder 2e Character Import
Import Pathfinder 2e characters built in [Pathbuilder 2e](https://pathbuilder2e.com/app.html?v=110a). Pathbuilder exports a character as JSON (its "Export to JSON" feature), so this maps to the existing JSON character import path: add a Pathbuilder-2e-to-canonical field mapping/adapter and expose it as a source in the import flow. Respect the licensing/Product Identity rules in `docs/licensing/open-content-compliance.md` — import only the user's own character data, not bundled rules content.

### Multi-Turn AI Memory
Persistent AI memory across sessions beyond what is stored in campaign state - long-term NPC relationship tracking, world knowledge graphs, and player preference learning.

### Rate Limiting and Abuse Protection
Add request throttling and abuse detection for internet-hosted sessions.

### Persistent Token Revocation
Move the in-memory token revocation list to a durable store (Redis or database) so revocations survive server restarts.

### Session Export and Replay
Export full session logs, event history, and summaries in a portable format for archival or sharing.

---

## Priority 4 - Exploratory / Long-Term

### Reranking in RAG Pipeline
Add a reranking step after initial vector retrieval to improve context relevance for AI prompts.

### Deterministic Adventure Outline Templates
Reduce token cost and improve consistency in adventure generation by combining LLM calls with structured outline templates.

### Plugin / Extension System
Allow community-contributed rulesets, character sheet layouts, and AI prompt templates.

### Multi-Language Support
Localize the UI and support non-English source documents.

---

*Last updated: 2026-09-28 — Priority-1 engineering items completed and removed; only maintainer-gated P1 items remain.*
