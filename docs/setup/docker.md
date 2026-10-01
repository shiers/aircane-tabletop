# Docker Development Environment

Aircane Tabletop uses Docker Compose for local development. The setup provides PostgreSQL with pgvector, an optional Redis service, and containerised backend and frontend dev servers.

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (or Docker Engine + Compose plugin on Linux)
- Docker Compose v2 (`docker compose` - note: no hyphen)

---

## Services

| Service    | Image / Build                  | Port  | Profile  |
|------------|-------------------------------|-------|----------|
| `postgres` | `pgvector/pgvector:pg16`      | 5432  | _(always)_ |
| `redis`    | `redis:7-alpine`              | 6379  | `redis`  |
| `backend`  | `src/backend/Aircane.Api`     | 5000  | `app`    |
| `frontend` | `src/frontend/aircane-web`    | 5173  | `app`    |

The `postgres` service starts by default. The `redis`, `backend`, and `frontend` services are profile-gated so you can start only what you need.

---

## Common Commands

### Start just the database (most common for local development)

```bash
docker compose up postgres -d
```

The backend and frontend can then be run natively with `dotnet run` and `npm run dev`.

### Start everything (database + backend + frontend)

```bash
docker compose --profile app up
```

Add `-d` to run in the background:

```bash
docker compose --profile app up -d
```

### Start everything including Redis

```bash
docker compose --profile app --profile redis up
```

### Stop all services

```bash
docker compose down
```

### Reset local data (removes the PostgreSQL volume)

```bash
docker compose down -v
```

> **Warning:** `down -v` permanently deletes all local database data. Use this to start fresh.

### View logs

```bash
# All services
docker compose logs -f

# A specific service
docker compose logs -f postgres
```

### Rebuild images after code changes

```bash
docker compose --profile app build
```

---

## Local Connection String

When running PostgreSQL via Docker Compose, use this connection string in your local `appsettings.Development.json` or user secrets:

```
Host=localhost;Port=5432;Database=aircane;Username=aircane;Password=aircane_dev
```

Example `appsettings.Development.json` entry:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=aircane;Username=aircane;Password=aircane_dev"
  }
}
```

> `appsettings.Development.json` is excluded from version control. Never commit credentials.

---

## Local Overrides

A `docker-compose.override.yml` template is included at the repository root. This file is excluded from version control and is automatically merged by Docker Compose. Use it to customise ports, mount source volumes for hot-reload, or inject local API keys without touching the shared `docker-compose.yml`.

---

## Troubleshooting

**Port 5432 already in use**
Another PostgreSQL instance is running locally. Either stop it or override the host port in `docker-compose.override.yml`:

```yaml
services:
  postgres:
    ports:
      - "5433:5432"
```

Then update your connection string to use port `5433`.

**`docker compose` command not found**
You may have the older `docker-compose` (v1) installed. Install Docker Desktop or the Compose plugin for Docker Engine. The project requires Compose v2.

**pgvector extension not available**
The `pgvector/pgvector:pg16` image includes the extension. If you are connecting to an external PostgreSQL instance, install pgvector manually and run `CREATE EXTENSION IF NOT EXISTS vector;` in the `aircane` database.
