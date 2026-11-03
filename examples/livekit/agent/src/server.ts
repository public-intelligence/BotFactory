import { AccessToken } from "livekit-server-sdk";
import { RoomAgentDispatch, RoomConfiguration } from "@livekit/protocol";
import dotenv from "dotenv";
import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
dotenv.config({ path: path.join(__dirname, "../../.env") });

const log = (component: string, msg: string, data?: Record<string, unknown>) => {
  const timestamp = new Date().toISOString();
  const extra = data ? ` ${JSON.stringify(data)}` : "";
  console.log(`[${timestamp}] [${component}]  ${msg}${extra}`);
};

const webDir = path.join(__dirname, "../../web");
const server = http.createServer(async (req, res) => {
  const url = new URL(req.url!, `http://${req.headers.host}`);

  // Token endpoint — generates a LiveKit JWT for the web client
  if (url.pathname === "/api/token") {
    const identity = `web-user-${Math.random().toString(36).slice(2, 6)}`;
    const roomName = `room-${Math.random().toString(36).slice(2, 8)}`;

    log("http", "generating token", { identity, roomName });

    const token = new AccessToken(
      process.env.LIVEKIT_API_KEY!,
      process.env.LIVEKIT_API_SECRET!,
      { identity },
    );
    token.addGrant({
      roomJoin: true,
      room: roomName,
      roomCreate: true,
      canPublish: true,
      canSubscribe: true,
    });
    token.roomConfig = new RoomConfiguration({
      agents: [new RoomAgentDispatch({ agentName: "echo-agent" })],
    });
    const jwt = await token.toJwt();
    res.writeHead(200, {
      "Content-Type": "application/json",
      "Access-Control-Allow-Origin": "*",
    });
    res.end(JSON.stringify({ token: jwt, url: process.env.LIVEKIT_URL }));
    return;
  }

  // Static files
  const filePath = path.join(webDir, url.pathname === "/" ? "index.html" : url.pathname);
  const resolved = path.resolve(filePath);
  if (!resolved.startsWith(path.resolve(webDir))) {
    res.writeHead(403);
    res.end();
    return;
  }
  const ext = path.extname(resolved);
  const mime: Record<string, string> = {
    ".html": "text/html",
    ".js": "application/javascript",
    ".css": "text/css",
  };
  fs.readFile(resolved, (err, data) => {
    if (err) {
      res.writeHead(404);
      res.end("Not found");
      return;
    }
    res.writeHead(200, {
      "Content-Type": mime[ext] || "application/octet-stream",
    });
    res.end(data);
  });
});
server.listen(3000, () =>
  log("http", "web client serving", { url: "http://localhost:3000" }),
);
