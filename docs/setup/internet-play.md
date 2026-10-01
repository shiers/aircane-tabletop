# Internet Play (Cloudflare Tunnel)

Aircane Tabletop can make a session reachable over the internet so remote players can
join without you configuring your router. It does this with a **Cloudflare Tunnel**
(`cloudflared`) bundled as a second sidecar in the [desktop app](./desktop.md).

> **Desktop app only.** Internet play needs the bundled `cloudflared` sidecar, so it is
> available only in the Aircane desktop wrapper. If you run the backend manually in a
> browser, use **Local network only** mode, or install and run `cloudflared` yourself.

## How it works

1. The host picks **Internet play** and clicks **Start tunnel** on the session screen.
2. The desktop wrapper spawns `cloudflared tunnel --url http://localhost:<port>`.
3. Cloudflare assigns a temporary public URL like `https://<random>.trycloudflare.com`
   and the wrapper reports it to the backend.
4. The backend enters **internet mode**, switching on the security hardening described
   below, and surfaces the URL (with a copy button and QR code) for you to share.
5. Remote players open the tunnel URL and join with the invite code, exactly as LAN
   players do.

The tunnel URL is **temporary** and changes every session (Cloudflare's free tier, no
account required). Share the fresh URL with your players at the start of each session,
the same way you share an invite code.

## What is and isn't sent over the tunnel

- Session traffic — chat, dice rolls, narration, character state — passes through
  Cloudflare's network.
- Your imported **PDFs and source files never leave your machine.** Only API responses
  are transmitted. The backend never serves source documents to clients.

For private groups who prefer no third-party involvement, use **Local network only** mode
and connect over the LAN.

## Security hardening (internet mode)

When a tunnel is active the backend applies protections that are unnecessary on a trusted
LAN and stay off otherwise. See [architecture/security.md](../architecture/security.md)
for details.

- **Rate limiting** (per client IP): a strict fixed-window limit on the join / approval /
  reconnect endpoints (defeats invite-code brute forcing) and a general sliding-window
  limit on all other endpoints. Configurable via the `RateLimiting` section of
  `appsettings.json`. LAN sessions are never throttled.
- **CSRF / cross-origin protection**: state-changing requests must come from the tunnel
  origin or localhost, be `Content-Type: application/json`, and carry
  `X-Requested-With: XMLHttpRequest`. Cross-origin attacker pages are rejected.
- **Persistent token revocation**: ending a session records every active participant
  token so a server restart cannot re-admit a revoked participant.

## Persistent (named) tunnels — future option

The free tunnel issues a new URL each session. If you want a stable URL, create a free
Cloudflare account and configure a named tunnel with `cloudflared`. That is out of scope
for the bundled experience today but is compatible with the same backend.

---

## Security smoke test checklist

Run these manually before relying on internet play. A green build proves the code compiles
and packages; it does not prove the runtime behaviour below.

```text
Internet play security smoke tests (run manually):

[ ] Rate limiting: send 11 rapid POST /api/sessions/{id}/join requests -> 12th returns 429
[ ] Rate limiting: LAN session (no tunnel) -> no rate limiting applied
[ ] CSRF: send POST with Origin: https://evil.com -> request rejected (403)
[ ] CSRF: send POST from the tunnel URL origin -> request accepted
[ ] CSRF: send POST from localhost origin -> request accepted
[ ] Token revocation: end session -> restart server -> attempt join with old token -> rejected
[ ] Token revocation: RevokedTokens table has entries after session end
[ ] Token cleanup job: tokens with ExpiresAt in the past are removed on next cleanup run
[ ] Tunnel starts within 30 seconds and the URL is displayed in the UI
[ ] Tunnel URL is reachable from a machine on a different network
[ ] Closing the app stops both the tunnel and the ASP.NET Core sidecar
[ ] Disclosure panel appears on first internet-play enable
[ ] Disclosure panel does not reappear after "Don't show again" is checked
```
