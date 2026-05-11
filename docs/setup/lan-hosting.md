# LAN Hosting Guide

Aircane Tabletop is designed for local network play. The host runs the server, and players join from their own devices on the same network.

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
npm run dev -- --host
```

### 5. Create a Session

1. Open the app on the host machine
2. Create or select a campaign
3. Click **Start Session**
4. The app generates an **invite code** and displays the **join URL**

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

- LAN only — no internet/tunnel hosting yet
- No HTTPS on LAN (acceptable for trusted local networks)
- Session tokens expire after 24 hours by default
- No persistent user accounts — players are identified by session tokens
