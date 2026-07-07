// YGO Duel Simulator — WebSocket relay for cross-network multiplayer.
//
// A stateless Worker routes `wss://<host>/room/<CODE>?role=host|join` to a
// Durable Object named by <CODE>. That Durable Object (RoomRelay) holds the two
// peers' WebSockets and forwards every message from one to the other. It keeps
// no game state and never inspects payloads — it is a dumb pipe that punches
// through both players' NATs, since both sides dial *out* to Cloudflare.
//
// Roles:
//   host  — creates the room and waits. Rejected (409) if the code is taken.
//   join  — joins an existing room. Rejected (404) if no host is waiting,
//           (409) if the room is already full.
// When the second peer arrives the room sends a `{type:"__relay",event:"start"}`
// control frame to both, which is each client's signal that the link is live.

/** Matches a 1–12 char alphanumeric room code, e.g. /room/AB4K */
const ROOM_PATH = /^\/room\/([A-Za-z0-9]{1,12})$/;

const START_FRAME = JSON.stringify({ type: "__relay", event: "start" });

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const match = url.pathname.match(ROOM_PATH);
    if (!match) return new Response("not found", { status: 404 });

    if (request.headers.get("Upgrade") !== "websocket") {
      return new Response("expected a websocket upgrade", { status: 426 });
    }

    const code = match[1].toUpperCase();
    const id = env.ROOM.idFromName(code);
    return env.ROOM.get(id).fetch(request);
  },
};

/**
 * One room: pairs a host and a joiner and relays messages between them.
 * Uses the WebSocket Hibernation API so an idle room costs nothing while two
 * players sit in a lobby — the sockets survive eviction and `getWebSockets()`
 * still returns them, so no in-memory state is needed.
 */
export class RoomRelay {
  constructor(state) {
    this.state = state;
  }

  async fetch(request) {
    const role = new URL(request.url).searchParams.get("role");
    const hosts = this.state.getWebSockets("host");
    const joiners = this.state.getWebSockets("join");

    if (role === "host") {
      if (hosts.length > 0) return new Response("room code already in use", { status: 409 });
    } else if (role === "join") {
      if (hosts.length === 0) return new Response("no such room", { status: 404 });
      if (joiners.length > 0) return new Response("room is full", { status: 409 });
    } else {
      return new Response("missing ?role=host|join", { status: 400 });
    }

    const [client, server] = Object.values(new WebSocketPair());
    this.state.acceptWebSocket(server, [role]);

    // The joiner is always the second peer, so its arrival completes the pair.
    if (role === "join") {
      server.send(START_FRAME);
      for (const host of this.state.getWebSockets("host")) host.send(START_FRAME);
    }

    return new Response(null, { status: 101, webSocket: client });
  }

  webSocketMessage(ws, message) {
    for (const peer of this.state.getWebSockets()) {
      if (peer !== ws) {
        try { peer.send(message); } catch { /* peer gone; its own close tears down the room */ }
      }
    }
  }

  webSocketClose(ws) {
    this.teardown();
  }

  webSocketError(ws) {
    this.teardown();
  }

  // Either side leaving ends the match, so drop the whole room.
  teardown() {
    for (const peer of this.state.getWebSockets()) {
      try { peer.close(1000, "peer left"); } catch { /* already closing */ }
    }
  }
}
