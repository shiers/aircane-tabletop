# In-App Feedback ("Report a bug")

Aircane Tabletop ships with an in-app **Report a bug** button. A tester writes what went wrong;
the app auto-captures non-sensitive diagnostic context and the backend proxies a structured issue
to the Aircane GitHub repository's issue tracker via the GitHub REST API.

The GitHub token lives **only on the server**. The browser never calls GitHub directly, never
sees the token, and the token is never logged or stored in the database.

## 1. Create a GitHub token

Create a **fine-grained personal access token** scoped to the Aircane repository only:

1. GitHub → Settings → Developer settings → Fine-grained personal access tokens → **Generate new
   token**.
2. **Resource owner**: the owner (user or org) of the Aircane repository.
3. **Repository access**: *Only select repositories* → choose the Aircane repository only.
4. **Permissions** → Repository permissions → **Issues: Read and write**. No other scopes are
   needed.
5. Generate and copy the token (it looks like `github_pat_...`). You will not see it again.

Keep the scope as narrow as possible — a token limited to a single repository with Issues-only
write access cannot touch code or other repositories.

## 2. Configure the backend

The backend reads these settings (shown here with the environment-variable form; configuration
sources such as environment variables and .NET user secrets take precedence over
`appsettings.json`):

| Setting | Env var | Purpose |
|---------|---------|---------|
| `Feedback:GitHubToken` | `Feedback__GitHubToken` | The fine-grained PAT (secret). |
| `Feedback:GitHubOwner` | `Feedback__GitHubOwner` | Owner of the Aircane repo. |
| `Feedback:GitHubRepo` | `Feedback__GitHubRepo` | Name of the Aircane repo. |
| `Feedback:Assignee` | `Feedback__Assignee` | *(Optional)* GitHub login to assign issues to. Defaults to the owner; leave empty to omit the assignee. |

### Development (recommended: .NET user secrets)

From `src/backend/Aircane.Api/`:

```bash
dotnet user-secrets set "Feedback:GitHubToken" "github_pat_..."
dotnet user-secrets set "Feedback:GitHubOwner" "your-org-or-user"
dotnet user-secrets set "Feedback:GitHubRepo" "aircane-tabletop"
```

### Self-hosted / production (environment variables)

```bash
export Feedback__GitHubToken="github_pat_..."
export Feedback__GitHubOwner="your-org-or-user"
export Feedback__GitHubRepo="aircane-tabletop"
```

The committed `appsettings.json` ships with empty placeholders — never commit a real token there.
If `Feedback:GitHubToken` (or the owner/repo) is empty, the endpoint responds with HTTP 503 and
the message **"Feedback is not configured on this instance."** instead of attempting a submission.

### Labels must exist first

Each issue is created with the labels **`bug`** and **`beta-feedback`**. Both labels must already
exist on the repository before shipping — GitHub rejects label application for labels that do not
exist, which would fail the submission. Create them once under the repo's *Issues → Labels*.

## 3. Rate limiting

The feedback endpoint is anonymous and creates real GitHub issues, so it is rate limited in
**every** mode (LAN and internet), unlike the other limiters that only engage in internet mode.
The default is **5 submissions per IP per hour**, tunable via the `RateLimiting:Feedback` section
(`PermitLimit`, `WindowSeconds`, `QueueLimit`). Rejected requests return HTTP 429 with a
`Retry-After` header.

## What is captured

- The tester's summary, description, and optional steps/expected behaviour.
- Platform / OS string and app version.
- Active AI provider and model (name only — no API keys).
- The current route (active screen).
- Session and campaign IDs (identifiers only, no content).
- The last 10 session events as **type + timestamp + a short scalar summary** (e.g. a roll
  formula and total, or — for a chat message — the sender's display name only) — never the
  message text itself.
- The last 5 browser console errors (each stack trimmed to its first 3 lines).

## What is NOT captured

- Campaign content (scenes, narration text, notes).
- Character data or character JSON.
- PDF or source-document content.
- API keys or secrets of any kind.
- Chat message text. (A chat event's summary records only the sender's display name, as the
  key field — never the message body. No real-name lookup is performed; the display name is
  whatever a participant chose when joining.)

## Security notes

- The GitHub token is server-side only: it is never returned to any client, never logged, and
  never stored in the database.
- The browser's only submission surface is `POST /api/feedback`; it never contacts GitHub
  directly.
