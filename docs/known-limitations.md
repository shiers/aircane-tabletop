# Known Limitations (MVP)

This document lists current limitations of the Aircane Tabletop MVP. These are planned for future releases.

## Document Processing

- **No OCR support.** Scanned/image-only PDFs are detected and marked as "OCR required" but cannot be processed. Only text-layer PDFs and form-fillable PDFs are supported.
- **No advanced PDF layout parsing.** Complex multi-column layouts, tables, and sidebars may not extract cleanly. Best results come from simple text-flow PDFs.
- **No automatic folder watching.** Folder scans must be triggered manually. There is no filesystem watcher that auto-detects new files.

## Networking

- **LAN only.** There is no internet/tunnel hosting. Players must be on the same local network as the host.
- **No HTTPS on LAN.** Traffic between players and the host is unencrypted. This is acceptable for trusted home networks but not suitable for public networks.
- **No persistent user accounts.** Players are identified by short-lived session tokens, not login credentials.

## Game Systems

- **System-agnostic framework is in place, but built-in definitions are limited.** The Game System Definition engine supports any TTRPG declaratively, but only D&D 5e 2014 and a generic freeform system ship as starter templates. Custom system definitions can be created but are not yet battle-tested across all mechanics.
- **No full combat automation.** The AI can request rolls and propose damage/healing, but there is no automated initiative tracker, turn enforcement, or condition duration tracking.
- **Encounter validation is basic.** The encounter difficulty checker uses placeholder logic and may not accurately assess all encounters.

## AI

- **OpenAI and Ollama are implemented as chat providers.** Azure OpenAI, AWS Bedrock, and Grok are planned but not yet wired up. Configuring an unimplemented provider will silently fall back to the Fake provider.
- **The default provider is Fake.** Out of the box the app returns deterministic placeholder responses so it runs with no setup. Switch to OpenAI or Ollama in Settings → AI Provider for real AI.
- **AI quality depends on the provider and model.** Smaller local models (via Ollama) will produce lower-quality narration and rules answers than cloud models like GPT-4o.
- **Embeddings are tied to the provider and model that generated them.** Retrieval (RAG) only works when queries are embedded with the *same* embedding provider and model that embedded the stored chunks — embeddings from different providers/models are not comparable. The app now records embedding provenance and guards against silent mismatches (see below), but the underlying coupling still applies:
  - **Only Ollama produces usable built-in RAG today.** The embedding providers are Ollama (`nomic-embed-text`, 768-dim) and a deterministic Fake provider used for tests. There is no cloud (e.g. OpenAI) embedding provider yet. An AI *chat* API key does not enable real embeddings — the chat provider and the embedding provider are configured separately.
  - **Mismatches are detected, not silent.** Each chunk records the provider, model, and dimension that produced its embedding. At query time, chunks whose provenance does not match the active embedding provider are excluded from vector search and a "re-index required" warning is logged, so a provider/model change degrades honestly (keyword search still works) instead of returning meaningless results. Chunks embedded before provenance tracking have null provenance and are treated as compatible.
  - **Re-index is a first-class operation.** After changing the embedding provider or model (or removing Ollama), regenerate embeddings with `POST /api/library/documents/reembed-all` (or `POST /api/library/documents/{id}/reembed` for one document). This re-embeds with the current provider and refreshes provenance. It is still a manual step, but no longer a full reset/re-seed.
  - **The embedding column dimension is fixed at 768.** The database stores `vector(768)`, matching Ollama/Fake. Running a provider with a different dimension (e.g. OpenAI at 1536) alongside or in place of the current one still requires a schema change and is deferred — see the [backlog](backlog.md#embedding-provider-portability-embed-on-first-run--provider-metadata).
- **No streaming narration in all modes.** Some AI responses may appear all at once rather than streaming token-by-token, depending on the provider.
- **Context window limits.** Very long sessions or large document libraries may exceed the AI's context window. The RAG pipeline mitigates this but cannot eliminate it.
- **No multi-turn memory beyond session events.** The AI does not have persistent memory across sessions beyond what is stored in campaign state.

## Characters

- **PDF character import is best-effort.** Only form-fillable PDFs and simple text-layer PDFs produce reliable results. Complex character sheet layouts may require manual correction.
- **No D&D Beyond or VTT integration.** Characters must be imported via PDF or JSON, or created manually.

## Library

- **Source documents are never copied.** If the host moves or deletes a file from a registered folder, the app marks it as "source unavailable." Re-indexing requires the file to be accessible again.
- **No cloud/upload mode yet.** Documents must exist on the host's local filesystem in registered folders.

## Platform

- **No desktop wrapper.** The app runs as a web server + browser. A Tauri/Electron wrapper for one-click launch is planned.
- **No mobile-optimized UI.** The frontend works on mobile browsers but is not specifically designed for small screens.
- **PostgreSQL required.** There is no SQLite option for lightweight installations. Docker is needed to run the database.

## Security

- **No rate limiting.** The API does not throttle requests. This is acceptable for LAN use but would need addressing for internet hosting.
- **Token revocation is in-memory.** If the server restarts, revoked tokens may become valid again until they expire naturally (24-hour default).

---

These limitations represent deliberate MVP scope decisions. See the [backlog](backlog.md) and project tasks for planned improvements.
