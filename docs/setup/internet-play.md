# Internet Play (Cloudflare Tunnel)

Aircane Tabletop can make a session reachable over the internet so remote players can
join without you configuring your router. It does this with a **Cloudflare Tunnel**
(`cloudflared`) bundled as a second sidecar in the [desktop app](./desktop.md).

> **Desktop app only.** Internet play needs the bundled `cloudflared` sidecar, so it is
> available only in the Aircane desktop wrapper. If you run the backend manually in a
> browser, use **Local network only** mode. Running your own tunnel or reverse proxy is
> possible but only supported under the conditions in
> [Using your own tunnel or reverse proxy](#using-your-own-tunnel-or-reverse-proxy).

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

## Who counts as the host

Authorization is enforced in every mode. The one person who needs no token is the **host**, and
the backend recognises them only by a request coming **straight from the machine it runs on**:
a loopback peer address, `localhost` as the host name, and no proxy header naming another client.
Everyone else, including every player who arrives through the tunnel, needs a participant token
and is a Player.

This matters because the tunnel runs on the host's machine: `cloudflared` connects to the backend
from `127.0.0.1`, so a check on the peer address alone would make the whole internet look like the
host. Cloudflare's edge adds `CF-Connecting-IP` and `X-Forwarded-For` (naming the real client) to
every request, and the public `*.trycloudflare.com` name arrives as the `Host`. Any one of those
tells the backend the request is not the host's own, and a request that is not the host's own
cannot use host-only features such as ending the session, approving players or changing settings.

The consequence for you: use the app on the host machine at `http://localhost:<port>` (the desktop
window already does), not through your public tunnel URL.

## Using your own tunnel or reverse proxy

The bundled `cloudflared` is the supported setup. If you run a different tunnel (ngrok, a named
Cloudflare tunnel, Tailscale Funnel) or a reverse proxy (nginx, Caddy) on the same machine as the
backend, it is **supported only if it does at least one of these for every request**:

- passes the client address on in `X-Forwarded-For` or `Forwarded` (nginx `proxy_set_header
  X-Forwarded-For $remote_addr`, Caddy does this by default), or
- leaves the public host name in the `Host` header (do not rewrite it to `localhost`).

A tool that does neither, that is, one that connects from `127.0.0.1`, adds no forwarding header
and rewrites `Host` to `localhost`, makes remote players indistinguishable from the host's own
browser and **would give them host access. That setup is unsupported**; do not use it. If you
are unsure, test it: from a device that is not the host, open the tunnel URL and try to open
the AI settings page, which must be refused.

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
[ ] Host-only access is refused through the tunnel: GET <tunnel-url>/api/ai/settings with no token -> 401
[ ] Host-only access is refused for a joined player: same request with a Player token -> 403
[ ] The host's own window (http://localhost:<port>) still reaches AI settings while the tunnel is up
[ ] Closing the app stops both the tunnel and the ASP.NET Core sidecar
[ ] Disclosure panel appears on first internet-play enable
[ ] Disclosure panel does not reappear after "Don't show again" is checked
```
