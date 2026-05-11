# Aircane Tabletop

An AI-assisted tabletop RPG engine for any TTRPG. Import rules, characters, and adventures; generate solo or group content; and run sessions with an AI assistant, co-DM, or full DM.

Supports any game system through data-driven Game System Definitions - from D&D 5e and Pathfinder 2e to Powered by the Apocalypse, FATE, Shadowrun, and beyond. No code changes needed to add a new system.

Runs locally on your machine with LAN multiplayer. No cloud account required.

---

## Quick Start

```bash
# 1. Start the database
docker compose up postgres -d

# 2. Run the backend
cd src/backend/Aircane.Api
dotnet run

# 3. Run the frontend (separate terminal)
cd src/frontend/aircane-web
npm install
npm run dev
```

Open **http://localhost:5173** in your browser.

See the [full setup guide](docs/setup/getting-started.md) for prerequisites and details.

---

## Features

- **System-Agnostic** - Define any TTRPG's mechanics declaratively. Built-in support for d20 systems, dice pools, PbtA, FATE, percentile, and more.
- **Library Management** - Register folders containing your PDFs. The app indexes them for semantic search without copying files.
- **AI DM Runtime** - Configure the AI as assistant, co-DM, or full DM with adjustable authority levels. The AI adapts to your game system's mechanics automatically.
- **Rules Lookup** - Ask rules questions grounded in your imported sources with citations.
- **Adventure Generation** - Generate solo or group adventures scaled to your party's level and composition, with system-appropriate encounter balancing.
- **Character Management** - Dynamic character sheets driven by your game system's schema. Import from PDF/JSON, create manually, or use templates.
- **Dice Roller** - Supports any dice convention: d20+modifier, dice pools, Fudge dice, exploding dice, step dice, and more.
- **LAN Multiplayer** - Host a session, share an invite code or QR code, and play together on the local network.
- **Campaign State** - Event-sourced state with undo, audit trail, and session continuity.

---

## Documentation

| Guide | Description |
|-------|-------------|
| [Getting Started](docs/setup/getting-started.md) | Prerequisites, installation, first run |
| [AI Configuration](docs/setup/ai-configuration.md) | Configure OpenAI, Azure, Bedrock, Ollama, or Grok |
| [LAN Hosting](docs/setup/lan-hosting.md) | Host a session for local network players |
| [Docker Environment](docs/setup/docker.md) | Docker Compose services and commands |
| [Architecture Overview](docs/architecture/overview.md) | Tech stack, data flows, project structure |
| [Known Limitations](docs/known-limitations.md) | Current MVP boundaries |

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Frontend | Vue 3, TypeScript, Vite, Pinia, Tailwind CSS |
| Backend | ASP.NET Core 8, C#, SignalR |
| Database | PostgreSQL 16 + pgvector |
| AI Providers | OpenAI, Azure OpenAI, AWS Bedrock, Ollama, Grok |
| Dev Environment | Docker Compose |

---

## Project Structure

```
src/
  backend/          ASP.NET Core API, Domain, Application, Infrastructure, Workers
  frontend/         Vue 3 + TypeScript + Vite web app
tests/
  backend/          xUnit unit and integration tests
  e2e/              Playwright end-to-end tests
docs/
  architecture/     Architecture overview
  setup/            Setup and configuration guides
  adr/              Architecture Decision Records
```

---

## Development

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 20+](https://nodejs.org/)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)

### Common Commands

```bash
# Backend tests
dotnet test

# Frontend tests
cd src/frontend/aircane-web
npm run test

# E2E tests
cd tests/e2e/playwright
npx playwright test

# Reset database
docker compose down -v
docker compose up postgres -d
```

---

## License

License TBD.
