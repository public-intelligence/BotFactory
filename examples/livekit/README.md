# LiveKit Voice Agent — Minimal Example

Echo agent: speaks back whatever you say. Tests the full pipeline (VAD → STT → TTS) using Deepgram for STT/TTS and a hardcoded echo instead of a real LLM.

## Prerequisites

- Docker & Docker Compose
- Node.js 20+
- npm
- A Deepgram API key ([console.deepgram.com](https://console.deepgram.com))

## Setup

```bash
# 1. Configure env
cp .env.example .env
# Edit .env and add your DEEPGRAM_API_KEY

# 2. Start LiveKit Server + Redis
docker compose up -d

# 3. Install and build the agent
cd agent
npm install
npm run build

# 4. Run the agent (registers with LiveKit, waits for participants)
# Also serves the web client at http://localhost:3000
npm run dev

# 5. Open http://localhost:3000 in your browser
# Click "Connect & Start Talking" and grant mic access
```

## What's happening

1. Browser connects to LiveKit room via WebSocket, publishes mic audio
2. LiveKit dispatches the agent to join the room
3. Agent: VAD detects speech → Deepgram STT transcribes → EchoLLM returns "You said: ..." → Deepgram TTS speaks it back
4. You hear the agent repeat what you said

## Next steps

Replace `EchoLLM` with a custom LLM adapter that calls `POST /api/chat/turn` on the .NET backend.
