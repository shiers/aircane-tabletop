# Security Architecture

This document describes Aircane Tabletop's runtime security posture, with emphasis on the
hardening that makes **internet play** (Cloudflare Tunnel) safe to expose. Aircane is
local-first: each host runs its own backend on its own machine. On a trusted LAN the threat
model is minimal, so several protections are deliberately **only active in internet mode** —
i.e. when the host has started a Cloudflare tunnel and the backend is reachable from
arbitrary origins.

## Access modes and "internet mode"

A session's `SessionAccessMode` (`Solo`, `LocalLan`, `InternetTunnel`, `Cloud`) records how
players connect. At the process level, the backend tracks a single runtime flag through
`ITunnelStateService`:

- `IsInternetModeActive` is `true` when a tunnel is active (the desktop wrapper has reported a
  public URL via `POST /api/sessions/tunnel-url`).
- `TunnelUrl` holds the current public origin (e.g. `https://xxx.trycloudflare.com`).

This flag is the switch for the internet-only hardening below. It is **in-memory and
non-persistent**: the free Cloudflare tunnel issues a new URL each session, so a restarted
backend returns to LAN-only mode until the host re-enables internet play. (This is the
runtime equivalent of the `TunnelSettings:Enabled` configuration flag referenced in the
task spec — a live flag is used instead of static config because the tunnel is started and
stopped at runtime by the host.)

## Rate limiting

ASP.NET Core rate limiting (`Microsoft.AspNetCore.RateLimiting`) throttles requests **per
client IP**, and only when internet mode is active. On the LAN the limiters resolve to an
unlimited partition, so trusted local play is never throttled.

Two policies are configured (tunable via the `RateLimiting` section of `appsettings.json`):

| Policy | Type | Default | Applies to |
|--------|------|---------|-----------|
| `join` | Fixed window | 10 / minute | `POST /api/sessions/{id}/join`, `POST /api/sessions/{id}/approve-participant`, `POST /api/sessions/{id}/reconnect` (any invite-code / token endpoint) |
| `api`  | Sliding window | 200 / minute (6 segments) | Every other API endpoint (global limiter) |

The `join` policy prevents brute-forcing invite codes; the `api` policy prevents the API from
being hammered from the public internet. On rejection the middleware returns
`429 Too Many Requests` with a `Retry-After` header and logs the hit (remote IP, endpoint,
session ID) via the structured logger.

**Client IP resolution.** Behind Cloudflare Tunnel the socket peer is the local `cloudflared`
process. The real client IP is read from `CF-Connecting-IP` (falling back to the first
`X-Forwarded-For` entry, then the socket address). These forwarding headers are only consulted
in internet mode.

## CSRF / cross-origin protection

### Why cookie-based antiforgery is not used

The standard ASP.NET Core antiforgery mechanism issues an antiforgery **cookie** plus a
request token, and relies on the browser automatically attaching the cookie. That model
targets **cookie-authenticated** apps, where a cross-site page can trigger an authenticated
request because the browser sends the session cookie automatically.

Aircane's SPA authenticates with **JWT bearer tokens** carried in the `Authorization` header,
not cookies. A malicious cross-origin page:

- cannot read another origin's bearer token (it lives in the SPA's own storage, protected by
  the same-origin policy), and
- the browser never attaches the token automatically.

So the classic cookie/token antiforgery pair adds no value here. Instead we defend against a
hostile page issuing "simple" or scripted cross-origin requests to the tunnel URL.

### SPA-oriented equivalent

`CsrfProtectionMiddleware` runs on every **state-mutating** request (any method other than
GET/HEAD/OPTIONS/TRACE) **when internet mode is active**, and requires all of:

1. **Origin / Referer check** — the `Origin` header (or `Referer` fallback) must match the
   active tunnel origin or a localhost/loopback origin. Attacker-controlled origins are
   rejected.
2. **`Content-Type: application/json`** — a browser cannot set this on a "simple" cross-origin
   request without triggering a CORS preflight, which the CORS policy rejects for untrusted
   origins.
3. **`X-Requested-With: XMLHttpRequest`** — a secondary custom-header check that likewise
   cannot be forged on a simple cross-origin request.

Rejected requests return `403 Forbidden` with a problem+json body and are logged. All API
responses carry `Vary: Origin` so shared caches never serve an origin-specific response to a
different origin.

On the LAN (internet mode off) these checks are skipped: the host and players are same-origin
on a trusted network.

## Participant token revocation

Session participant tokens are short-lived signed JWTs (`sid`, `pid`, `dname`, `role`, and a
unique `jti` token id). Revocation is enforced through two layers:

- **In-memory fast path** — `ITokenRevocationService` tracks revoked sessions in memory for a
  cheap first check. This survives request lifetimes but not a process restart.
- **Persistent authoritative store** — the `RevokedTokens` table records the `jti`, session,
  revocation time, and token expiry. Token validation queries this table (after the in-memory
  check) so a **server restart does not re-admit** a participant whose token was revoked before
  the restart.

When the host ends a session, every active participant token id is written to `RevokedTokens`
(bulk insert) in addition to the in-memory session revocation. A background cleanup job runs on
startup and daily to delete rows whose `ExpiresAt` is in the past, preventing unbounded table
growth. Persistent revocation applies to **all** sessions (LAN and internet), improving the
baseline posture rather than only the tunnel case.

## Source document protection

Independent of internet mode, `SourceFileAccessBlockerMiddleware` rejects any request that
targets a source document file extension (`.pdf`, `.epub`, …) or contains path traversal.
Aircane never serves imported source files to clients — they are read server-side for indexing
and retrieval only. Over the tunnel, only API responses (narration, dice, chat, character
state) are transmitted; imported PDFs never leave the host machine.
