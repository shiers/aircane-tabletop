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

> **Implementation update:** The actionable P1 items below have now been implemented — the
> in-process background job infrastructure (channel queue + hosted worker + `GET /api/jobs/{id}`),
> combat automation surfacing (combat REST endpoints, `CombatTracker.vue` for host + player, and a
> live combat block in the AI DM prompt), the PF2e adapter (`Pf2eEncounterValidator` + validator
> selector, PF2e-aware generation prompts, elite/weak templates), configurable embedding dimensions
> with OpenAI/Azure OpenAI cloud embedding providers, library import UX (content-hash `ExactDuplicate`
> detection, game-system aliases, per-folder exclude patterns), and OCR follow-ups (full-page
> rasterization via Docnet/PDFium, opt-in tessdata download, and re-OCR endpoints + UI). The
> maintainer-gated items (real PF2e ORC text, internet tunnel, desktop wrapper, app license, PF1e
> content) remain as noted. The "Still outstanding" notes in the sections below predate this update.

### Pathfinder 2e Adapter
**Delivered:** A built-in `Pathfinder2eRemasterSeed` game-system definition captures the PF2e mechanics — four degrees of success (margin vs DC), three-action economy with multiple attack penalty, creature-level encounter budget, PF2e conditions, and proficiency ranks — and validates against `GameSystemDefinitionValidator`. A new `GameSystemDefinitionSeeder` persists all built-in definitions (D&D 5e, Freeform, PF2e Remaster) into `GameSystemDefinitions` on startup (this also fixed a gap where the built-in definitions were referenced by the migration service but never actually inserted).

**Still outstanding:**
- **PF2e-specific generation logic.** Adventure generation and encounter validation still use the D&D-5e-oriented `Dnd5eEncounterValidator`; add a PF2e encounter validator (creature-level budget) and PF2e-aware adventure generation.
- **Elite/weak creature adjustments.** The definition documents them in AI guidance but there is no mechanical applicator; add one if programmatic creature scaling is wanted.
- **Rules text.** The ORC-licensed PF2e rules-text bundle is a separate, user-blocked item (see above).

### Pathfinder 2e Remaster Built-in Content (real ORC text)
The `pf2e_remaster` built-in bundle currently ships a small placeholder (2 chunks) so the app runs; the seeder handles the partial bundle gracefully. To complete it, source the real PF2e Remaster rules text **from a genuine ORC-licensed release** and drop the Markdown files into `src/backend/Aircane.Workers/Resources/builtin/pf2e_remaster/` (adding entries to its `manifest.json`). No code changes needed — the bundle structure, `LICENSE-ORC.txt`, manifest, and seeder already work. **Licensing caution:** do NOT extract content from the Foundry VTT PF2e system data or the Obsidian TTRPG Community repo — that material is distributed under a private Paizo↔Foundry partnership agreement and Paizo's Community Use Policy, not the ORC License, and cannot be bundled as ORC content here. Use only text that is actually released under ORC. Follow the full ORC requirements (Notice, upstream Paizo product attribution, downstream ORC-Content-vs-Reserved-Material declaration, Product Identity exclusions, and the OGL/ORC split) documented in [`docs/licensing/open-content-compliance.md`](licensing/open-content-compliance.md).

### Open-Content Compliance Follow-ups
Close the compliance gaps captured in [`docs/licensing/open-content-compliance.md`](licensing/open-content-compliance.md).

**Delivered:** An in-app **About / Credits** panel (`/about`) prominently displays the ORC Notice and the CC BY / OGL attributions (reusing the license list), plus an explicit ORC-Content-vs-Reserved-Material downstream declaration and a trademark note.

**Still outstanding (maintainer decision):** Decide and state the **application code license** — the README currently says "License TBD". This is deliberately left to the repository owner and is kept distinct from the content licenses (ORC/OGL/CC BY). The About panel already points users at the README for the app license; once chosen, update the README and add a `LICENSE` file. See the decision table at the top of this file.

### OCR Pipeline
**Delivered:** A Tesseract-backed OCR pipeline (`IOcrEngine` / `TesseractOcrEngine`, using the `TesseractOCR` wrapper for Tesseract 5) runs behind the existing OCR-required detection. When a page has little/no extractable text, the extractor pulls the page's embedded raster images and feeds them to OCR; recognized text above a confidence threshold is chunked and embedded like any other content. OCR is **optional and off by default** (`Ocr:Enabled`), and the engine **gates cleanly** — a missing native library or `tessdata` reports the engine unavailable rather than crashing, and the document stays `OcrRequired`. Setup and licensing are documented in [`docs/setup/ocr.md`](setup/ocr.md).

**Still outstanding:**
- **Full-page rasterization.** OCR currently runs on images already embedded in the PDF (the common case for scanned rulebooks: one full-page image per page). PDFs that draw text as vector glyphs or that split pages into many small images are not rasterized; add a PDF-to-image renderer (PDFium/Docnet, Ghostscript) for full fidelity.
- **Bundled language data / auto-download.** Users must supply `tessdata` themselves; consider an opt-in downloader or a bundled English pack.
- **Re-run on enable.** Documents imported while OCR was off keep `OcrRequired`; a re-index/re-OCR action would let users process them without re-importing (ties into the Background Job Infrastructure item).

### Internet Tunnel / Remote Play
> **Not auto-built — security-sensitive; maintainer decision.** See the decision table at the top of this file.

Allow players to connect over the internet without router port forwarding. Options include Cloudflare Tunnel, ngrok, or a custom relay service. Requires HTTPS, rate limiting, CSRF protection, and persistent token revocation.

### Desktop Wrapper
> **Not auto-built — needs a new build toolchain; maintainer decision.** See the decision table at the top of this file.

Package the app as a Tauri (or Electron) desktop application that starts the ASP.NET Core server, opens the local UI, manages file paths, and displays the LAN URL and QR code - one-click launch for non-technical hosts.

### Advanced Combat Automation
**Backend delivered:** A pure `CombatEngine` (`Aircane.Domain/Combat/`) plus an `EncounterState` model persisted in campaign state drive initiative order, turn advancement (with round wrap + condition tick), per-instance condition durations, HP damage/healing (temp-HP absorption, downed-PC death saves, NPC death), and death-save resolution. Combat commands (StartEncounter, RollInitiative, AdvanceTurn, TickConditions, ApplyDamage, ApplyHealing, ApplyCondition, RemoveCondition, DeathSave) flow through the existing AI-proposes → validate → authority → approval pipeline, and turn changes broadcast over SignalR (`CombatTurnChanged`). Note: this also fixed a latent bug where ApplyDamage/ApplyHealing/ApplyCondition previously no-op'd (generic state merge).

**Still outstanding:**
- **Surface combat into the AI prompt.** `PlayerActionService` does not yet feed combatant HP / initiative / active turn into the AI DM prompt, so the AI can't reason over live combat state. Add this to the prompt context.
- **Frontend combat tracker UI.** No initiative/turn/HP tracker view yet; consume the `CombatTurnChanged` SignalR event and the encounter state.
- **Concentration checks.** Not modeled (would build on the condition + damage hooks).
- **Turn enforcement.** The engine tracks whose turn it is but does not reject out-of-turn actions; add enforcement if desired.

### Embedding Provider Portability (Embed-on-First-Run + Provider Metadata)
Make stored embeddings robust to provider/model changes.

**Delivered (core):**
- **Provenance recording.** Each `DocumentChunk` records `EmbeddingProvider`, `EmbeddingModel`, and `EmbeddingDimensions` when its embedding is generated (seeder + import job + re-embed).
- **Mismatch guard.** Vector search excludes chunks whose provenance does not match the active embedding provider and logs a "re-index required" warning, so a provider/model change no longer silently returns meaningless results. Null provenance (pre-tracking) is treated as compatible.
- **Re-embed path.** `POST /api/library/documents/reembed-all` and `.../{id}/reembed` regenerate embeddings with the current provider and refresh provenance.

**Still outstanding:**
- **Dimension flexibility.** The `DocumentChunk.Embedding` column is hardwired to `vector(768)` (Ollama `nomic-embed-text` / Fake). Supporting a provider with a different dimension (e.g. OpenAI `text-embedding-3-small` at 1536) requires either a configurable/migrated column dimension, a second column, or a per-dimension strategy — deferred to a future release. Path when needed: change the column dimension, add a migration, and run a full re-embed.
- **OpenAI (and other cloud) embedding providers.** Only Ollama and Fake exist today. Add cloud embedding providers behind the existing `IEmbeddingProvider` abstraction, gated by the dimension work above.

The "ship text, embed on first run" contract is delivered and documented in `docs/architecture/overview.md`. Making re-embed (and import/reindex/folder-scan) asynchronous depends on the shared background-job infrastructure below.

### Background Job Infrastructure
The app has no real background-job runner yet: `DocumentImportJob`, `FolderScanJob`, reindex, and the re-embed endpoints all execute **synchronously** on the request thread, and `IDocumentImportService` (enqueue/status) is defined but unimplemented. Introduce a single background-job mechanism — either Hangfire/Quartz (per the tech steering) or a lightweight in-process hosted-service + channel queue for local-first mode — and route document import, folder scan, reindex, and re-embed through it with a shared job-status/progress surface. This is a cross-cutting foundation; several features (large-library import, `reembed-all`, folder rescans) want it, so build it once rather than per-feature.

### Library Import UX (duplicate safeguards, suggestions, canonicalization)
**Delivered:**
- **Review-before-import for watched folders.** "Review & import" analyzes a folder and opens a modal listing every discovered file with a suggested title/ruleset, duplicate grouping, and advisory flags (possible duplicate, already imported, likely-not-rules). Nothing is imported until the host confirms a selection, and nothing is pre-selected among duplicate variants (the host always picks the copy to keep). Backend is the source of truth: `GET /api/library/folders/{id}/scan/preview` and `POST /api/library/folders/{id}/scan/import` back the flow (`IScanCandidateAnalyzer`, `FilenameNormalizer`, `IFolderScanJob.PreviewFolderAsync` / `ImportSelectionAsync`).
- **Quick import (Option B).** A clearly-labeled secondary action imports all clean, unflagged files in one step, skipping anything flagged (duplicates, already-imported, likely-not-rules) and telling the host to use "Review & import" for those. It never auto-picks a duplicate winner.
- **Filename-based title & ruleset suggestions** on upload (strip extension + qualifiers, expand common abbreviations, detect a year for the ruleset).
- **Game-system canonicalization** on write (upload, classification update, folder scan) against known Game System Definitions, plus a datalist of known systems/rulesets on the upload form to prevent free-text drift.
- **SourceType enum alignment fix** — the frontend `SourceType` numeric values were off-by-one vs the backend, so all documents (including the built-in rulebooks) displayed the wrong "Type"; corrected so the API's integer enum maps correctly.

**Still outstanding:**
- **Content-based duplicate detection.** Dedup is filename-heuristic only (no content hash / unique constraint on `SourceDocument`). Add a content hash column + a cross-folder duplicate check if same-content-different-name detection is wanted; would need an EF migration + backfill.
- **Game-system alias table.** Canonicalization matches definition name/identifier only. A small alias/synonym map (e.g. "D&D 5e" → the D&D definition) would catch common short-hands that don't match a definition name.
- **Configurable not-useful signals.** The likely-not-rules filename list is a small hardcoded set; consider making it an editable per-folder setting.

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

*Last updated: MVP release.*
