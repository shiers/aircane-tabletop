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

- **D&D 5e 2014 only.** Pathfinder 2e support is planned but not yet implemented.
- **No full combat automation.** The AI can request rolls and propose damage/healing, but there is no automated initiative tracker, turn enforcement, or condition duration tracking.
- **Encounter validation is basic.** The encounter difficulty checker uses placeholder logic and may not accurately assess all encounters.

## AI

- **AI quality depends on the provider and model.** Smaller local models (via Ollama) produce lower-quality narration and rules answers than cloud models like GPT-4o.
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
