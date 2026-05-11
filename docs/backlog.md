# Post-MVP Backlog

This document lists planned features and improvements beyond the Aircane Tabletop MVP, organized by priority.

---

## Priority 1 — High Impact / Frequently Requested

### Pathfinder 2e Adapter
Add a ruleset adapter supporting three-action economy, degrees of success, encounter XP budgets, conditions, proficiency scaling, and creature elite/weak adjustments. The core domain model is already system-agnostic; this adds the game-specific validation and generation logic.

### OCR Pipeline
Integrate Tesseract (or equivalent) to process scanned/image-only PDFs that the MVP marks as "OCR required." Enables hosts with older or scan-only rulebooks to index their full library.

### Internet Tunnel / Remote Play
Allow players to connect over the internet without router port forwarding. Options include Cloudflare Tunnel, ngrok, or a custom relay service. Requires HTTPS, rate limiting, CSRF protection, and persistent token revocation.

### Desktop Wrapper
Package the app as a Tauri (or Electron) desktop application that starts the ASP.NET Core server, opens the local UI, manages file paths, and displays the LAN URL and QR code — one-click launch for non-technical hosts.

### Advanced Combat Automation
Add initiative tracker, turn enforcement, condition duration tracking, automatic damage/healing application, death save management, and concentration checks. The MVP AI can request rolls and propose changes but does not enforce turn order.

---

## Priority 2 — Significant Enhancements

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

## Priority 3 — Quality of Life

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

### Multi-Turn AI Memory
Persistent AI memory across sessions beyond what is stored in campaign state — long-term NPC relationship tracking, world knowledge graphs, and player preference learning.

### Rate Limiting and Abuse Protection
Add request throttling and abuse detection for internet-hosted sessions.

### Persistent Token Revocation
Move the in-memory token revocation list to a durable store (Redis or database) so revocations survive server restarts.

### Session Export and Replay
Export full session logs, event history, and summaries in a portable format for archival or sharing.

---

## Priority 4 — Exploratory / Long-Term

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
