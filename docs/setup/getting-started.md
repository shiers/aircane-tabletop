# Getting Started

This guide walks you through setting up Aircane Tabletop for local development or first-time use.

## Prerequisites

| Tool | Version | Purpose |
|------|---------|---------|
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | Latest | PostgreSQL + pgvector database |
| [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) | 8.0+ | Backend API |
| [Node.js](https://nodejs.org/) | 20+ | Frontend build tooling |
| [Git](https://git-scm.com/) | Any | Clone the repository |

Optional:
- [Ollama](https://ollama.ai/) — for fully local AI (no API key needed)

## 1. Clone the Repository

```bash
git clone https://github.com/your-org/aircane-tabletop.git
cd aircane-tabletop
```

## 2. Start the Database

```bash
docker compose up postgres -d
```

This starts PostgreSQL 16 with the pgvector extension on port 5432. The database is created automatically with:
- Database: `aircane`
- User: `aircane`
- Password: `aircane_dev`

Verify it's running:

```bash
docker compose ps
```

## 3. Run the Backend

```bash
cd src/backend/Aircane.Api
dotnet run
```

On first run, EF Core migrations apply automatically in Development mode. The API starts on `http://localhost:5000` by default.

Check the health endpoint:

```
GET http://localhost:5000/health
```

## 4. Run the Frontend

In a separate terminal:

```bash
cd src/frontend/aircane-web
npm install
npm run dev
```

The frontend starts on `http://localhost:5173` and proxies API requests to the backend.

## 5. Open the App

Navigate to **http://localhost:5173** in your browser. You should see the Aircane Tabletop UI.

## What's Next

- [Configure an AI provider](./ai-configuration.md) to enable rules lookup and AI DM features
- [Host a LAN session](./lan-hosting.md) so other players can join from their devices
- [Docker development environment](./docker.md) for running everything in containers

## Resetting Local Data

To wipe the database and start fresh:

```bash
docker compose down -v
docker compose up postgres -d
```

Then restart the backend — migrations will recreate the schema.

## Troubleshooting

**Port 5432 already in use**
Another PostgreSQL instance is running. Stop it, or change the port in `docker-compose.override.yml`.

**Backend fails to start**
Ensure Docker is running and the postgres container is healthy. Check `docker compose logs postgres`.

**Frontend can't reach the backend**
Make sure the backend is running on port 5000. The frontend's Vite config proxies `/api` requests there.

**Migrations fail**
If the database schema is out of date, try resetting: `docker compose down -v` and restart.
