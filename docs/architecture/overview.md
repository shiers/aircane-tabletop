# Architecture Overview

Aircane Tabletop is a local-first web application with a host-run server. The host's machine runs both the backend API and serves the frontend. Players connect via browser over the local network.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Frontend | Vue 3, TypeScript, Vite, Pinia, Tailwind CSS |
| Backend | ASP.NET Core 8, C#, SignalR |
| Database | PostgreSQL 16 + pgvector |
| AI | Provider abstraction (OpenAI, Azure OpenAI, AWS Bedrock, Ollama, Grok) |
| Real-time | SignalR WebSocket hub |
| Background Jobs | Hangfire / hosted services |
| PDF Processing | UglyToad.PdfPig |
| Testing | xUnit, Vitest, Playwright |
| Dev Environment | Docker Compose |

## High-Level Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Host Machine                          │
│                                                         │
│  ┌──────────────┐    ┌──────────────────────────────┐   │
│  │  Vue 3 SPA   │◄──►│   ASP.NET Core 8 API         │   │
│  │  (Vite dev)  │    │                              │   │
│  │  Port 5173   │    │  • REST Controllers          │   │
│  └──────────────┘    │  • SignalR Session Hub        │   │
│                      │  • Background Import Jobs     │   │
│                      │  • AI Provider Adapter        │   │
│                      │  Port 5000                    │   │
│                      └──────────┬───────────────────┘   │
│                                 │                       │
│                      ┌──────────▼───────────────────┐   │
│                      │  PostgreSQL + pgvector        │   │
│                      │  Port 5432                    │   │
│                      └──────────────────────────────┘   │
│                                                         │
│  ┌──────────────────────────────────────────────────┐   │
│  │  User's Filesystem (registered folder paths)     │   │
│  │  PDFs, rulebooks, adventures — never copied      │   │
│  └──────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
         ▲              ▲              ▲
         │              │              │
    ┌────┴───┐    ┌────┴───┐    ┌────┴───┐
    │Player 1│    │Player 2│    │Player 3│
    │Browser │    │Browser │    │Phone   │
    └────────┘    └────────┘    └────────┘
```

## Backend Architecture (Clean Architecture)

```
src/backend/
├── Aircane.Api/              → Controllers, Hubs, Middleware, Auth
├── Aircane.Application/      → Use cases, interfaces, DTOs, validation
├── Aircane.Domain/           → Entities, value objects, domain rules
├── Aircane.Infrastructure/   → EF Core, AI providers, PDF parsing, file I/O
└── Aircane.Workers/          → Background document import/indexing jobs
```

- **Domain** — pure entities and business rules, no external dependencies
- **Application** — orchestrates use cases, defines service interfaces
- **Infrastructure** — implements interfaces (database, AI, file system)
- **API** — HTTP surface, SignalR hubs, authorization middleware
- **Workers** — long-running background tasks (document import, embedding generation)

## Frontend Architecture (Feature-Oriented)

```
src/frontend/aircane-web/src/
├── features/
│   ├── library/              → Document import, folder management
│   ├── campaigns/            → Campaign CRUD, state
│   ├── characters/           → Character management, import
│   ├── sessions/             → Session hosting, player screens
│   ├── dice/                 → Dice roller, roll log
│   ├── ai/                   → AI settings, rules Q&A
│   └── adventure-generation/ → Adventure creation pipeline
├── shared/
│   ├── components/           → Reusable UI components
│   ├── api/                  → API client, SignalR connection
│   └── stores/               → Shared Pinia stores
└── router/                   → Vue Router configuration
```

## Key Data Flows

### Document Import
1. Host registers a folder path
2. Backend scans folder, finds PDFs
3. Import job extracts text, chunks pages, generates embeddings
4. Chunks stored in PostgreSQL with pgvector embeddings
5. Documents become searchable via semantic + keyword search

### AI DM Loop
1. Player submits an action
2. Backend loads campaign state + retrieves relevant rules/adventure chunks (RAG)
3. AI provider generates narration + structured proposed actions
4. If authority requires approval → queued for host
5. Approved actions applied to campaign state via validated commands
6. State updates broadcast to all players via SignalR

### Session Real-Time
- SignalR hub handles: join/leave, chat, dice rolls, AI narration streaming, state updates
- Participant tokens (JWT-style) enforce roles server-side
- Reconnection restores identity without re-approval

## Design Principles

1. **Local-first** — no cloud account required, all data stays on the host machine
2. **Server-authoritative** — the backend validates all state changes
3. **AI proposes, humans approve** — AI never directly mutates state without validation
4. **Source documents are never copied** — the app reads from registered folder paths
5. **Provider abstraction** — AI and embedding providers are swappable
6. **Append-only events** — campaign state changes are auditable and undoable
