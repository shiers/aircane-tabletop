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
| **Internet Tunnel / Remote Play** | **Decision made — Cloudflare Tunnel. Delivered.** Tunnel bundled as a second Tauri sidecar (checksum-verified at build time), with the security hardening prerequisites — per-IP rate limiting, SPA CSRF/origin checks, and persistent token revocation — implemented first. See "Completed P1 Work" below. | Nothing — delivered. Follow-up (persistent named tunnels via a Cloudflare account) is optional and tracked informally. |
| **Desktop Wrapper (Tauri/Electron)** | **Decision made — Tauri.** Wrapper implemented: backend runs as a Tauri sidecar, native webview, LAN URL/QR in the tray, Ollama awareness. See "Completed P1 Work" below. | Nothing — delivered. Remaining follow-ups (code signing/notarisation, auto-update, bundled database) are tracked as separate deferred items. |
| **Application source-code license** | Decision made. **Proprietary** license chosen for the application source code (distinct from the content licenses ORC/CC BY, which are unaffected). | Update the README and add a `LICENSE` file reflecting the proprietary terms. The in-app About panel already points at the README for this. |
| **PF1e built-in content** | Removed for commercialization — OGL v1.0a adds a Section 15 attribution chain and a content-identification burden that adds legal complexity for a commercial product regardless of the source-code license chosen. D&D 5e SRD (CC BY 4.0) and PF2e Remaster (ORC) are commercial-friendly and retained. | Nothing to unblock — this is a deliberate removal, not deferred work. Users may still import their own PF1e PDFs via the folder-watching Library; only the bundled OGL rules text was removed. |

---

## Recently Delivered (not yet in a priority bucket)

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
backend 2125 unit + 96 integration green, frontend `vue-tsc` + 317 vitest green. **Caveat:** the D&D
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
> **Decision made — Proprietary.** The application source code is licensed under a **Proprietary** license, kept distinct from the content licenses (ORC/OGL/CC BY), which are unaffected. See the decision table at the top of this file.

The remaining work is documentation: update the README (currently "License TBD") and add a `LICENSE` file reflecting the proprietary terms. The in-app About / Credits panel (`/about`) already points users at the README for the app license. (The compliance surface itself — ORC Notice, CC BY / OGL attributions, ORC-Content-vs-Reserved-Material downstream declaration, and trademark note — is delivered in the About panel.)

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

> **Delivered and moved out of P2:** *D&D Beyond / VTT Integration* and *Pathbuilder 2e Character
> Import* shipped together as the source-specific character import adapters — see "Recently
> Delivered" above (PR #15). The one standing caveat is that the D&D Beyond URL path relies on an
> unofficial API that may change.

---

## Priority 3 - Quality of Life

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

*Last updated: 2026-10-04 — source-specific character import adapters (D&D Beyond URL+file, Foundry VTT 5e/PF2e, Roll20, Pathbuilder 2e, Generic VTT) delivered and merged to `dev` via PR #15; the P2 items "D&D Beyond / VTT Integration" and "Pathbuilder 2e Character Import" moved to Recently Delivered. Earlier (2026-10-04): in-app "Report a bug" feedback feature delivered and merged to `dev` (moved from In Progress to Recently Delivered). Earlier (2026-10-01): added the feedback feature as an In Progress item. Earlier (2026-10-01): Internet Tunnel / Remote Play (Cloudflare Tunnel) delivered and moved to Completed P1 Work, including its security-hardening prerequisites; removed the now-shipped P3 "Rate Limiting" and "Persistent Token Revocation" items. Earlier (2026-10-01): moved D&D Beyond / VTT Integration and Pathbuilder 2e Character Import from P3 to P2. Earlier (2026-09-29): native mobile companion app technology decided — Flutter.*
