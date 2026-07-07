# YGO Duel Simulator — relay

A tiny Cloudflare Worker + Durable Object that relays the duel WebSocket between
two players on different networks. Both peers dial out to Cloudflare, so it works
through home routers/NAT with no port forwarding. Free-plan compatible.

## Deploy

From this `relay/` folder:

```powershell
npx wrangler login     # one-time: opens a browser to authorize wrangler
npx wrangler deploy    # publishes to https://ygo-duel-relay.<your-subdomain>.workers.dev
```

`deploy` prints the live URL. Put its `wss://` form into `NetProtocol.RelayBaseUrl`
in the C# app.

## How it works

- `wss://<host>/room/<CODE>?role=host` — create a room and wait for a joiner.
- `wss://<host>/room/<CODE>?role=join` — join an existing room by code.

The Durable Object named `<CODE>` holds both sockets and forwards every message
from one peer to the other. When the joiner connects, it sends both peers a
`{"type":"__relay","event":"start"}` frame; either side disconnecting closes the
room. No payloads are inspected and nothing is stored.

## Local test

```powershell
npx wrangler dev       # serves on http://localhost:8787
```
