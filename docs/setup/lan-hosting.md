# LAN Hosting Guide

Aircane Tabletop is designed for local network play. The host runs the server, and players join from their own devices on the same network.

## Security model

**Authorization is enforced in every mode.** There is no development or desktop setting that
lets another device on your network act as the host. Whoever is not the host needs a
participant token, which they get by joining with the invite code, and a token makes them a
**Player**. A request counts as *the host* only when it comes straight from the machine running
the backend, with no token needed: it must come from a loopback address (`127.0.0.1`/`::1`),
use `localhost` as the host name, and carry no proxy header that names another client
(`X-Forwarded-For`, `Forwarded`, `X-Real-IP`, `CF-Connecting-IP`, …).

This applies to the desktop app, to `dotnet run` (the Development environment) and to hosted
deployments alike. In practice:

- On the host machine, open the app at **`http://localhost:<port>`**. Opening it through the
  machine's own LAN address (`http://192.168.x.x:<port>`) is a request from a LAN address, so it
  needs a token like any other device.
- Players use `http://<your-lan-ip>:<port>` (or the dev server, below), enter the invite code, and
  are Players. They cannot end the session, approve players, assign characters, change AI
  settings or manage the library, however they reach the server.
- If you put your own reverse proxy in front of the backend on the same machine, it must pass the
  client address on (`X-Forwarded-For` or `Forwarded`) or the public `Host` name; see
  [Internet play](./internet-play.md#using-your-own-tunnel-or-reverse-proxy). A proxy that does
  neither makes every player look like the host.

## How It Works

1. The host starts the Aircane backend and frontend on their machine
2. Players connect to the host's LAN IP address from their browser
3. Players enter a display name and the session invite code
4. The host approves players and assigns characters
5. Everyone plays in real-time via SignalR

## Step-by-Step

### 1. Start the Server

Follow the [Getting Started](./getting-started.md) guide to start the backend and frontend.

### 2. Find Your LAN IP Address

**Windows:**
```bash
ipconfig
```
Look for your Wi-Fi or Ethernet adapter's IPv4 address (e.g., `192.168.1.42`).

**macOS:**
```bash
ipconfig getifaddr en0
```

**Linux:**
```bash
hostname -I | awk '{print $1}'
```

### 3. Configure the Backend for LAN Access

By default, the backend binds to `localhost`. To accept connections from other devices, set the URLs to bind to all interfaces:

```bash
cd src/backend/Aircane.Api
dotnet run --urls "http://0.0.0.0:5000"
```

Or set the environment variable:

```bash
export ASPNETCORE_URLS="http://0.0.0.0:5000"
```

Binding to all interfaces does not make other devices hosts: they are held to the rules in
[Security model](#security-model) above. For players to join, the backend needs a token signing
key. Set one for your run, for example with user secrets (a long random value, never committed):

```bash
cd src/backend/Aircane.Api
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -hex 32)"
```

(The desktop app generates and stores its own key; you do not set one there. Delete
`jwt-signing-key` from the app data directory to rotate it, which signs everyone out.)

### 4. Configure CORS for LAN

Add your LAN IP to the allowed origins in `appsettings.Development.json` or via environment variable:

```json
{
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:5173",
      "http://192.168.1.42:5173"
    ]
  }
}
```

If running the frontend in dev mode, start Vite with host binding:

```bash
cd src/frontend/aircane-web
npm run dev:lan
```

`npm run dev:lan` is `vite --host`. Prefer it to `npm run dev -- --host`: in **PowerShell** the
`--host` after `--` is not passed through to Vite, so the dev server stays on `localhost` and other
devices cannot reach it (it works in Git Bash, cmd and most other shells). If Vite prints only a
`Local:` address and no `Network:` address, the flag did not get through.

**Testing on a phone: use a production build.** The dev server's client keeps a WebSocket open to the
dev server for hot reload. A phone that is switched to another app has that socket cut, and when the
player comes back the client reloads the whole page. The app remembers a joined player across such a
reload, but a reload is still a reload, so for anything but editing the UI serve the built app instead:

```bash
cd src/frontend/aircane-web
npm run build
npx vite preview --host --port 4173
```

`vite preview` serves the same proxy settings (`vite.config.ts`) as the dev server, has no hot-reload
client, and never reloads the page by itself.

Players then load the app from the dev server, which forwards their requests to the backend. The
dev server is configured (`xfwd` in `vite.config.ts`) to pass each browser's address on, which is
how the backend tells your own browser on the host machine (the host, no token) from a player's
phone (a Player). Keep that setting if you edit the proxy.

### 5. Create a Session

1. Open the app on the host machine
2. Create or select a campaign
3. Click **Start Session**
4. The app generates an **invite code** and displays the **join URL**

#### Group adventures

A group adventure (generated with the **Group** mode) is started from its card, on the Adventure
Forge or on the campaign's page, with **Start Group Session**:

1. The app creates a local-network play session for the campaign and opens the **lobby**. The
   invite code, join link and QR code are at the top. The link uses your computer's network address,
   not `localhost`, so it works on players' devices.
2. Players join with the code and appear under **Awaiting Approval**. Approve each one (the list
   refreshes by itself) and, if you like, assign them a character. **Dismiss** only hides a request
   on your screen; the player stays unapproved and cannot take part.
3. The **turn order** starts in the order you approved people. Use the arrows to change it.
4. **Start adventure** is enabled once at least one player is approved.
5. During the adventure you see the current scene, whose turn it is and the round. **Pass the
   turn** moves play to the next player; the turn never moves by itself, so if the acting player has
   disconnected they keep it until you pass it. You can also move the scene and abandon the
   adventure.

You run the game as the host and do not take a turn. To play too, join as a player from another
browser with the invite code.

The invite code is kept in this browser (local storage, for up to three days or until you end the
session) because the server shows it only once. If you clear site data or use a different browser,
the lobby cannot show it again; players who already have it can still join.

### 6. Share the Join URL

Players need two things:
- **URL:** `http://<your-lan-ip>:5173` (or the port your frontend runs on)
- **Invite code:** displayed on the host's session screen

The app also generates a **QR code** that players can scan with their phone to open the join URL directly.

### 7. Players Join

On their device, players:
1. Open the URL in a browser
2. Enter a display name
3. Enter the invite code
4. Wait for host approval (if approval mode is enabled)

### 8. Host Approves and Assigns Characters

The host sees pending join requests and can:
- Approve or deny each player
- Assign a character to each approved player

Once approved, players see the session screen with their character, dice tray, and chat.

---

## Network Requirements

- All devices must be on the **same local network** (Wi-Fi or Ethernet)
- The host's firewall must allow inbound connections on the backend port (default: 5000) and frontend port (default: 5173)
- No internet connection is required if using Ollama for AI

## Firewall Tips

**Windows:** Allow the ports through Windows Firewall, or temporarily disable it for the local network profile.

**macOS:** System Settings → Network → Firewall → allow incoming connections for the app.

**Linux:**
```bash
sudo ufw allow 5000/tcp
sudo ufw allow 5173/tcp
```

## Troubleshooting

**Players can't reach the host**
- Verify you're on the same network
- Check the host's firewall settings
- Try pinging the host IP from a player device

**Connection drops during play**
- SignalR automatically reconnects on brief network interruptions
- If a player's token is still valid, they rejoin without re-approval

**QR code not showing**
- The QR code is generated from the LAN join URL. Ensure the backend is accessible on the displayed IP.

---

## Limitations (MVP)

- LAN play is for trusted networks; use [Internet play](./internet-play.md) (Cloudflare Tunnel) for remote players
- No HTTPS on LAN (acceptable for trusted local networks)
- Session tokens expire after 24 hours by default
- No persistent user accounts - players are identified by session tokens
