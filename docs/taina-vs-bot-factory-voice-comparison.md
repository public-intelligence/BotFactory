# taina_ogtic vs bot_factory — Voice bot configuration comparison

**Date:** 2026-05-19  
**taina_ogtic:** [public-intelligence/taina_ogtic](https://github.com/public-intelligence/taina_ogtic) (`main` at time of review)  
**bot_factory:** this repository (`apps/agent/`, `apps/backend/`)

Side-by-side comparison of voice pipeline configuration, latency optimizations, and UX tuning. Each section links to the exact files and line ranges where the behavior was observed.

---

## Summary

| Category | Count |
|----------|------:|
| High-impact gaps in bot_factory | 0 |
| High-impact gaps skipped (infra, intentional architecture, or deferred) | 4 |
| Medium-impact gaps | 0 |
| Medium-impact addressed or skipped | 3 |
| Minor differences | 0 (no action; bot_factory preferred) |
| Areas where bot_factory is stronger | 4 |

---

## High-impact findings

### 1. Multilingual turn detector ✅ *(implemented in bot_factory, 2026-05-19)*

| | bot_factory | taina_ogtic |
|---|-------------|-------------|
| Turn detection | `MultilingualModel` + per-bot Silero VAD | `MultilingualModel` + Silero VAD |
| How end-of-turn is decided | Semantic turn model + VAD `min_silence_duration` floor (`SilenceEndMs`, default 700 ms) | ML model predicts semantic turn completion |
| Plugin | `livekit-plugins-silero` + `livekit-plugins-turn-detector` | `livekit-plugins-turn-detector` |

**Why it matters:** Silence-only endpointing either cuts users off during natural pauses or adds dead air after every utterance. The multilingual turn detector uses linguistic cues, not only silence.

**bot_factory implementation notes:** Weights are pre-downloaded in worker `prewarm` and in the agent Docker image (`bot_factory_agent download-files`). `MultilingualModel()` is created per session in the job entrypoint (LiveKit Agents 1.5.x requires job context for the inference executor).

#### References — taina_ogtic

- Import: [`backend/src/main.py` L17](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/main.py#L17) — `from livekit.plugins.turn_detector.multilingual import MultilingualModel`
- Load + attach to session: [`backend/src/main.py` L283–296](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/main.py#L283-L296) — `MultilingualModel()` and `turn_detection=turn_detector` on `AgentSession`
- Dependency: [`backend/requirements.txt` L136](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/requirements.txt#L136) — `livekit-plugins-turn-detector==1.3.4`
- Docs (design intent): [`taina-gitbook-ogtic/.../integrations/livekit.md`](https://github.com/public-intelligence/taina_ogtic/blob/master/taina-gitbook-ogtic/taina-asistente-ia/index/integrations/livekit.md)

#### References — bot_factory

- Turn detector + session: [`apps/agent/src/bot_factory_agent/agent.py` L37–46](apps/agent/src/bot_factory_agent/agent.py#L37-L46) — `prewarm` downloads model files; [`L274–279`](apps/agent/src/bot_factory_agent/agent.py#L274-L279) — `AgentSession(..., turn_detection=MultilingualModel())`
- Per-bot silence → VAD: [`apps/agent/src/bot_factory_agent/agent.py` L98–114](apps/agent/src/bot_factory_agent/agent.py#L98-L114) — `get_vad_for_bot()` sets `min_silence_duration` from `silence_end_ms`
- Default silence 700 ms: [`apps/backend/BotFactory.Core/Entities/Bot.cs` L12](apps/backend/BotFactory.Core/Entities/Bot.cs#L12) — `SilenceEndMs = 700`
- Dependency: [`apps/agent/pyproject.toml` L7–12](apps/agent/pyproject.toml#L7-L12) — `livekit-plugins-turn-detector==1.5.1`
- Docker model bake: [`apps/agent/Dockerfile`](apps/agent/Dockerfile) — `python -m bot_factory_agent download-files`

---

### 2. Noise cancellation ⏭️ *(skipped for bot_factory, 2026-05-19)*

| | bot_factory | taina_ogtic |
|---|-------------|-------------|
| Noise cancellation | None (intentional) | BVC (Background Voice Cancellation) |
| Applied via | N/A | `RoomInputOptions(noise_cancellation=noise_cancellation.BVC())` |
| Plugin | N/A | `livekit-plugins-noise-cancellation` |

**Why it matters:** Background noise can trigger false VAD positives and garbled STT before the pipeline reaches the LLM.

**Why skipped:** Taina's BVC integration uses [`livekit-plugins-noise-cancellation`](https://pypi.org/project/livekit-plugins-noise-cancellation/), which **requires LiveKit Cloud** (Krisp-backed enhanced NC on the media path). Bot Factory runs **self-hosted** LiveKit (`bf-livekit.example.com`, ACI + `livekit-server` in `infra/modules/livekit.bicep` (Azure deployment, removed); dev: [`docker-compose.dev.yml`](docker-compose.dev.yml)). Enabling BVC against that deployment is expected to fail at runtime (e.g. *"audio filter cannot be enabled: LiveKit Cloud is required"*). Porting taina parity here would mean migrating realtime media to LiveKit Cloud (or a hybrid), which is out of scope for this repo's current Azure/ACI layout.

**Mitigations already in bot_factory:** Per-bot Silero VAD tuning (`activation_threshold`, `min_speech_duration`, `silence_end_ms`) and interruption gating reduce some false triggers from short noise; they do not replace full NC.

**If self-hosted NC is needed later (not taina parity):** Options that do not use the Cloud-only plugin include (a) **browser/client-side** Krisp via LiveKit's [`useKrispNoiseFilter`](https://docs.livekit.io/reference/components/react/hook/usekrispnoisefilter/) before publish, (b) community **OSS agent plugins** (e.g. DTLN / RNNoise wrappers for `RoomInputOptions.noise_cancellation` as a custom `FrameProcessor`), or (c) a **custom `AudioInput`** in the agent that runs a local denoiser before VAD/STT. Each is a separate design/effort from this comparison item.

#### References — taina_ogtic

- Import: [`backend/src/main.py` L14](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/main.py#L14) — `noise_cancellation`
- Session start: [`backend/src/main.py` L330–339](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/main.py#L330-L339) — `RoomInputOptions(noise_cancellation=noise_cancellation.BVC(), ...)`
- Dependency: [`backend/requirements.txt` L131](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/requirements.txt#L131) — `livekit-plugins-noise-cancellation==0.2.5`

#### References — bot_factory

- `session.start()` without `room_input_options`: [`apps/agent/src/bot_factory_agent/agent.py` L444–452](apps/agent/src/bot_factory_agent/agent.py#L444-L452)
- No noise-cancellation plugin in deps: [`apps/agent/pyproject.toml` L7–11](apps/agent/pyproject.toml#L7-L11)

---

### 3. STT provider and configuration ✅ *(language aligned in bot_factory, 2026-05-19)*

| | bot_factory | taina_ogtic |
|---|-------------|-------------|
| Provider | ElevenLabs | Deepgram |
| Model | `scribe_v2_realtime` | `nova-3` |
| Language | `es-419` (Latin American Spanish) | `es-419` (Latin American Spanish) |
| Sample rate | SDK default (`16000` in plugin) | `16000` explicit |

**Why it matters:** `es-419` can improve Caribbean/Latin American Spanish accuracy vs generic `es`. bot_factory still uses ElevenLabs, not Deepgram Nova-3.

**bot_factory implementation notes:** Fixed `es-419` for ElevenLabs STT/TTS in the agent; Gemini chat turns append a Latin American Spanish system-instruction suffix in .NET (`SpeechLanguagePrompt`). No per-bot language selector in v1.

#### References — taina_ogtic

- Active STT config: [`backend/src/main.py` L240–245](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/main.py#L240-L245) — `deepgram.STT(model="nova-3", language="es-419", sample_rate=16000)`
- Commented alternatives (Cartesia, Google): [`backend/src/main.py` L234–262](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/main.py#L234-L262)
- Env vars: [`backend/.env.example` L147–161](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/.env.example#L147-L161) — `DEEPGRAM_API_KEY`, `DEEPGRAM_LANGUAGE`, `DEEPGRAM_MODEL`
- Dependency: [`backend/requirements.txt` L126](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/requirements.txt#L126) — `livekit-plugins-deepgram==1.4.6`
- Docs: [`taina-gitbook-ogtic/.../integrations/deepgram.md`](https://github.com/public-intelligence/taina_ogtic/blob/master/taina-gitbook-ogtic/taina-asistente-ia/index/integrations/deepgram.md)

#### References — bot_factory

- ElevenLabs STT/TTS: [`apps/agent/src/bot_factory_agent/agent.py`](apps/agent/src/bot_factory_agent/agent.py) — `language_code` / `language` = `es-419` (`SPEECH_LANGUAGE_CODE`)
- Gemini locale: [`SpeechLanguagePrompt.cs`](apps/backend/BotFactory.Core/Services/SpeechLanguagePrompt.cs) + [`LiveKitEndpoints.cs`](apps/backend/BotFactory.Api/Endpoints/LiveKitEndpoints.cs) — master prompt + Latin American Spanish suffix
- Same API key for STT + TTS (comment): [`apps/agent/src/bot_factory_agent/agent.py` L224](apps/agent/src/bot_factory_agent/agent.py#L224)

---

### 4. LLM latency — direct API vs HTTP-to-backend proxy ⏭️ *(skipped / intentional for bot_factory, 2026-05-19)*

| | bot_factory | taina_ogtic |
|---|-------------|-------------|
| LLM integration | `BackendLLM` → POST .NET → SSE | `google.LLM` in agent process |
| Extra hops | Agent → .NET → Gemini → back | Agent → Gemini |
| Temperature | In .NET / Gemini service | `0.2` in agent |
| HTTP timeout | 30s read, 15s connect (per turn) | LiveKit Google plugin |

**Why it matters:** The .NET hop adds latency (connection setup, prompt assembly, SSE framing) on every turn. taina avoids that by calling Gemini inside the agent worker.

**Why skipped (taina parity):** Moving Gemini into the agent like taina would remove one network hop, but it conflicts with bot_factory’s deliberate split: the agent is a **thin audio bridge**; **all LLM/RAG, tenancy, `MasterPrompt`, KB tools, message persistence, and grounded-only handling** live in .NET ([`planning/architecture-design.md`](planning/architecture-design.md) §3.4, §4.3). The same `POST /livekit/chat/turn` pipeline also serves text chat—duplicating it in Python would fork business logic. Streaming SSE is already in place, so time-to-first-token is dominated by Gemini and DB work, not fixable by a small config change. **No plan to adopt in-process `google.LLM` for voice.**

**Optional micro-optimization (still on bot_factory architecture):** [`BackendLLM`](apps/agent/src/bot_factory_agent/llm_backend.py) currently creates a **new `httpx.AsyncClient` per utterance** (`async with httpx.AsyncClient(...)` around each `POST /livekit/chat/turn`), which repeats TCP/TLS setup to the API on every turn. A modest win—typically tens of ms when agent and backend are co-located—is to **reuse one client per LiveKit job**: create it in `BackendLLM.__init__`, call `client.stream(...)` each turn, and `await client.aclose()` on session shutdown ([`agent.py`](apps/agent/src/bot_factory_agent/agent.py) already registers `backend_llm.aclose()` in `on_shutdown`). This does **not** remove the proxy hop; it only pools connections agent → .NET.

#### References — taina_ogtic

- In-process Gemini LLM: [`backend/src/main.py` L264–268](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/main.py#L264-L268) — `google.LLM(model=..., temperature=0.2)`
- Agent instructions on `ProductionAssistant`: [`backend/src/main.py` L61–77](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/main.py#L61-L77) — `instructions=COMBINED_PROMPT` + function tools
- `LLM_MODEL` env: [`backend/.env.example` L167–172](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/.env.example#L167-L172)
- LangChain client (offline/ingest): [`backend/src/llm/gemini_client.py` L25–28](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/llm/gemini_client.py#L25-L28)

#### References — bot_factory

- Custom LLM adapter: [`apps/agent/src/bot_factory_agent/llm_backend.py` L41–71](apps/agent/src/bot_factory_agent/llm_backend.py#L41-L71) — `BackendLLM`
- HTTP + SSE to backend: [`apps/agent/src/bot_factory_agent/llm_backend.py` L106–114](apps/agent/src/bot_factory_agent/llm_backend.py#L106-L114) — `POST .../livekit/chat/turn`, `httpx.Timeout(30.0, connect=15.0)`, new client per turn
- Empty LiveKit agent instructions (prompt in .NET): [`apps/agent/src/bot_factory_agent/agent.py` L445–447](apps/agent/src/bot_factory_agent/agent.py#L445-L447)
- Chat turn endpoint: [`apps/backend/BotFactory.Api/Endpoints/LiveKitEndpoints.cs` L23–100](apps/backend/BotFactory.Api/Endpoints/LiveKitEndpoints.cs#L23-L100) — streams `bot.MasterPrompt` via `GeminiChatService`
- Gemini options: [`apps/backend/BotFactory.Core/Services/GeminiChatOptions.cs` L9–18](apps/backend/BotFactory.Core/Services/GeminiChatOptions.cs#L9-L18) — `gemini-2.5-flash`, 30s timeout

---

### 5. TTS text sanitization ⏭️ *(skipped for bot_factory, 2026-05-19)*

| | bot_factory | taina_ogtic |
|---|-------------|-------------|
| Text sanitization | None — SSE text goes to TTS as-is | `sanitize_text_for_tts()` on tool/RAG payloads |
| Phones | Not handled in agent | Digit-by-digit in `_parse_contact` + prompt rules |
| URLs | Not handled | `_parse_digital` → `enlace_lectura` |
| Currency | Not handled | `RD$` / `USD` regex in `sanitize_text_for_tts` |
| Markdown | Not stripped in agent | `**` stripped in `sanitize_text_for_tts` |

**Why it matters:** Markdown, URLs, and symbols are often spoken literally by TTS unless normalized first.

**Why skipped:** No production evidence yet that Gemini output or KB-grounded replies routinely produce unlistenable TTS (markdown read aloud, URLs spelled character-by-character, etc.). Revisit with a .NET `TtsTextSanitizer` on the `/livekit/chat/turn` SSE path if operators report those failures.

#### References — taina_ogtic

- Core sanitizer: [`backend/src/tools.py` L259–286](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/tools.py#L259-L286) — `sanitize_text_for_tts()`
- Used on tool results, e.g.: [`backend/src/tools.py` L532](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/tools.py#L532), [L590–594](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/tools.py#L590-L594)
- Phone readout helper: [`backend/src/tools.py` L371–380](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/tools.py#L371-L380) — `telefonos_lectura`
- URL readout helper: [`backend/src/tools.py` L403–407](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/tools.py#L403-L407) — `enlace_lectura`
- Docs mirror: [`taina-gitbook-ogtic/.../estructura-proyecto/catalogo-servicios.md`](https://github.com/public-intelligence/taina_ogtic/blob/master/taina-gitbook-ogtic/taina-asistente-ia/estructura-proyecto/catalogo-servicios.md) (search `sanitize_text_for_tts`)

#### References — bot_factory

- No sanitization in SSE stream path: [`apps/backend/BotFactory.Api/Endpoints/LiveKitEndpoints.cs` L96–100](apps/backend/BotFactory.Api/Endpoints/LiveKitEndpoints.cs#L96-L100) — whitespace normalization only, not voice-specific
- Chunks forwarded unchanged: [`apps/agent/src/bot_factory_agent/llm_backend.py` L134–138](apps/agent/src/bot_factory_agent/llm_backend.py#L134-L138)

---

### 6. Voice-optimized system prompt ⏭️ *(skipped for bot_factory, 2026-05-19)*

| Voice rule | taina_ogtic | bot_factory |
|------------|:-----------:|:-------------:|
| Plain text, no Markdown/asterisks | Yes | Per-bot `MasterPrompt` only |
| Phone numbers digit-by-digit | Yes | No global rule in agent |
| URLs spoken verbally | Yes | No global rule in agent |
| Times as spoken words (`08:00:00` → “8 de la mañana”) | Yes | No global rule in agent |
| Prices as spoken words | Yes | No global rule in agent |
| Conversational, concise | Yes | No global rule in agent |

**Why skipped:** Taina ships a fixed `FORMATO DE VOZ` block in code; bot_factory relies on per-bot `MasterPrompt` plus the Latin American Spanish suffix in [`SpeechLanguagePrompt.cs`](apps/backend/BotFactory.Core/Services/SpeechLanguagePrompt.cs). Add a shared voice-format preamble only if real calls show awkward phrasing that per-bot prompts do not fix.

#### References — taina_ogtic

- `FORMATO DE VOZ` block: [`backend/src/prompts.py` L56–60](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/prompts.py#L56-L60)
- Time/price conversion instructions: [`backend/src/prompts.py` L154–157](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/prompts.py#L154-L157)
- Combined prompt wired into agent: [`backend/src/prompts.py` L291–301](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/prompts.py#L291-L301) — `COMBINED_PROMPT`
- Used in assistant: [`backend/src/main.py` L66](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/main.py#L66)
- Session opener protocol: [`backend/src/prompts.py` L246–257](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/prompts.py#L246-L257) — `SESSION_INSTRUCTIONS`
- Docs: [`taina-gitbook-ogtic/.../index/api/system-prompt.md`](https://github.com/public-intelligence/taina_ogtic/blob/master/taina-gitbook-ogtic/taina-asistente-ia/index/api/system-prompt.md)

#### References — bot_factory

- Empty agent instructions: [`apps/agent/src/bot_factory_agent/agent.py` L446](apps/agent/src/bot_factory_agent/agent.py#L446) — `instructions=""`
- Per-bot prompt in DB: [`apps/backend/BotFactory.Core/Entities/Bot.cs` L10](apps/backend/BotFactory.Core/Entities/Bot.cs#L10) — `MasterPrompt`
- Sent to Gemini on each turn: [`apps/backend/BotFactory.Api/Endpoints/LiveKitEndpoints.cs` L82–84](apps/backend/BotFactory.Api/Endpoints/LiveKitEndpoints.cs#L82-L84)

---

## Medium-impact findings

### 7. Worker process configuration ✅ *(implemented in bot_factory, 2026-05-19)*

| Setting | bot_factory | taina_ogtic |
|---------|-------------|-------------|
| Worker API | `AgentServer` + `cli.run_app(server)` | `WorkerOptions` + `cli.run_app(worker_options)` |
| Memory warning | **1600 MB** (env `AGENT_JOB_MEMORY_WARN_MB`) | 1800 MB |
| Memory limit | **1900 MB** (env `AGENT_JOB_MEMORY_LIMIT_MB`) | 2500 MB |
| Load threshold | **0.8** (env `AGENT_LOAD_THRESHOLD`) | 0.8 |
| Idle processes | **1** on 1 vCPU ACI (env `AGENT_NUM_IDLE_PROCESSES`) | 3 |
| Shutdown timeout | Default | 1200 s |
| Init timeout | **60 s** (env `AGENT_INITIALIZE_PROCESS_TIMEOUT`) | 300 s |
| Drain timeout | Default | 3600 s |
| Max retry | Default | `max_retry_attempts * 5` (15 with default 3) |

**bot_factory implementation notes:** Explicit `AgentServer(...)` tuned for **2 GB / 1 vCPU** ACI (`infra/modules/agent.bicep` (Azure deployment, removed)). Per-job memory cap avoids container-wide OOM; higher warn/init thresholds match voice ML stack weight. Long drain/shutdown timeouts not copied (no deploy-kill evidence).

#### References — taina_ogtic

- `CONFIG` defaults: [`backend/src/main.py` L39–55](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/main.py#L39-L55)
- `WorkerOptions`: [`backend/src/main.py` L464–488](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/main.py#L464-L488)
- Env documentation: [`backend/.env.example` L176–191](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/.env.example#L176-L191)

#### References — bot_factory

- `AgentServer` tuning: [`apps/agent/src/bot_factory_agent/agent.py`](apps/agent/src/bot_factory_agent/agent.py) — `load_worker_server_config()` from [`config.py`](apps/agent/src/bot_factory_agent/config.py)
- Env overrides: [`apps/agent/.env.example`](apps/agent/.env.example) — `AGENT_*`

---

### 8. RoomInputOptions / RoomOutputOptions ✅ *(partial — self-hosted only, 2026-05-19)*

| | bot_factory | taina_ogtic |
|---|-------------|-------------|
| `RoomInputOptions` | `close_on_disconnect=False` | `BVC()` *(Cloud-only; skipped in bot_factory)*, `close_on_disconnect=False` |
| `RoomOutputOptions` | Not passed (defaults) | `audio_enabled=True` |
| `close_on_disconnect` | **False** | `False` — session survives brief disconnects |

**bot_factory implementation notes:** `RoomInputOptions(close_on_disconnect=False)` on `session.start()`. BVC not enabled (LiveKit Cloud). `RoomOutputOptions` omitted — plugin defaults sufficient.

#### References — taina_ogtic

- [`backend/src/main.py` L330–339](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/main.py#L330-L339)

#### References — bot_factory

- [`apps/agent/src/bot_factory_agent/agent.py`](apps/agent/src/bot_factory_agent/agent.py) — `room_input_options=RoomInputOptions(close_on_disconnect=False)`

---

### 9. TTS model selection ⏭️ *(skipped for bot_factory, 2026-05-19)*

| | bot_factory | taina_ogtic |
|---|-------------|-------------|
| Provider | ElevenLabs | ElevenLabs |
| Model | `eleven_flash_v2_5` (explicit) | Not set in code (plugin default) |
| Language | `es-419` | Not set in code |
| Voice tuning | `voice_id` only | Docs suggest `stability`, `similarity_boost` |

**Why skipped:** bot_factory already uses explicit `eleven_flash_v2_5` and `es-419`; Taina’s stability/similarity_boost appear in gitbook only, not runtime. No measured TTS quality gap to justify new tuning surface.

#### References — taina_ogtic

- Runtime TTS: [`backend/src/main.py` L270–276](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/main.py#L270-L276) — `elevenlabs.TTS(voice_id=...)`
- Docs (recommended params): [`taina-gitbook-ogtic/.../integrations/elevenlabs.md` L109–120](https://github.com/public-intelligence/taina_ogtic/blob/master/taina-gitbook-ogtic/taina-asistente-ia/index/integrations/elevenlabs.md#L109-L120)

#### References — bot_factory

- [`apps/agent/src/bot_factory_agent/agent.py`](apps/agent/src/bot_factory_agent/agent.py) — `model="eleven_flash_v2_5"`, `language="es-419"`

---

## Minor differences *(no action — bot_factory already preferred)*

| Area | bot_factory | taina_ogtic | Notes |
|------|-------------|-------------|-------|
| Prewarm | Lazy per-bot VAD cache | Eager `prewarm_process` + shared VAD | bot_factory better for multi-tenant VAD params |
| Greeting | `session.say(greeting)` from metadata | LLM-driven via `SESSION_INSTRUCTIONS` (direct `say` commented out) | bot_factory avoids extra LLM round-trip |

**Why no changes:** Both areas favor bot_factory’s design; no Taina parity work planned.

#### References — taina_ogtic (prewarm)

- [`backend/src/main.py` L109–167](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/main.py#L109-L167) — `prewarm_process`, `silero.VAD.load()` at L133–134

#### References — bot_factory (prewarm / VAD cache)

- Design comment + cache: [`apps/agent/src/bot_factory_agent/agent.py` L36–116](apps/agent/src/bot_factory_agent/agent.py#L36-L116)

#### References — taina_ogtic (greeting)

- Commented `session.say`: [`backend/src/main.py` L346–353](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/main.py#L346-L353)
- `SESSION_INSTRUCTIONS`: [`backend/src/prompts.py` L246–257](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/prompts.py#L246-L257)

#### References — bot_factory (greeting)

- [`apps/agent/src/bot_factory_agent/agent.py` L459–468](apps/agent/src/bot_factory_agent/agent.py#L459-L468)
- Default greeting on entity: [`apps/backend/BotFactory.Core/Entities/Bot.cs` L28–31](apps/backend/BotFactory.Core/Entities/Bot.cs#L28-L31)
- Passed via room metadata: [`apps/backend/BotFactory.Api/Endpoints/BotEndpoints.cs` L183–197](apps/backend/BotFactory.Api/Endpoints/BotEndpoints.cs#L183-L197)

---

## Where bot_factory is already better

### Per-bot VAD tuning

Taina loads a single default VAD in prewarm (`silero.VAD.load()` with no per-session params). bot_factory builds Silero VAD per bot from room metadata.

- **taina:** [`backend/src/main.py` L133–134](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/main.py#L133-L134), [L281](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/main.py#L281)
- **bot_factory:** [`apps/agent/src/bot_factory_agent/agent.py` L98–114](apps/agent/src/bot_factory_agent/agent.py#L98-L114), [`apps/backend/BotFactory.Core/Entities/Bot.cs` L33–39](apps/backend/BotFactory.Core/Entities/Bot.cs#L33-L39), [`apps/backend/BotFactory.Infrastructure/Services/LiveKitService.cs` L14–27](apps/backend/BotFactory.Infrastructure/Services/LiveKitService.cs#L14-L27)

### Interruption (barge-in) gating

No `turn_handling` / interruption settings were found in taina’s `backend/` code. bot_factory exposes `enable_barge_in`, `interruption_min_duration_ms`, and `interruption_min_words`.

- **bot_factory:** [`apps/agent/src/bot_factory_agent/agent.py` L244–264](apps/agent/src/bot_factory_agent/agent.py#L244-L264), [`apps/backend/BotFactory.Core/Entities/Bot.cs` L41–45](apps/backend/BotFactory.Core/Entities/Bot.cs#L41-L45)
- **taina:** grep `turn_handling` / `interruption` under `backend/` → no matches (reviewed 2026-05-19)

### Centralized LLM / RAG in .NET

Voice uses `BackendLLM` → `/livekit/chat/turn` so prompts, KB search, and transcripts stay in one multi-tenant backend (see §4 — intentional vs taina’s in-agent `google.LLM`).

- **bot_factory:** [`apps/agent/src/bot_factory_agent/llm_backend.py`](apps/agent/src/bot_factory_agent/llm_backend.py), [`apps/backend/BotFactory.Api/Endpoints/LiveKitEndpoints.cs`](apps/backend/BotFactory.Api/Endpoints/LiveKitEndpoints.cs)
- **taina:** [`backend/src/main.py` L264–268](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/main.py#L264-L268)

### Multi-tenant architecture

bot_factory passes per-bot voice, timeouts, VAD, interruption, and greeting through `RoomMetadata` when creating a LiveKit room.

- [`apps/backend/BotFactory.Api/Endpoints/BotEndpoints.cs` L183–199](apps/backend/BotFactory.Api/Endpoints/BotEndpoints.cs#L183-L199)
- [`apps/agent/src/bot_factory_agent/config.py` L55–112](apps/agent/src/bot_factory_agent/config.py#L55-L112) — `parse_room_metadata`
- **taina:** single `ProductionAssistant` + env-based voice ID ([`backend/src/main.py` L61–77](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/main.py#L61-L77), [L145–148](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/main.py#L145-L148))

---

## Recommended action plan

| Priority | Action | Effort | Impact | Primary references (taina) |
|----------|--------|--------|--------|----------------------------|
| ~~P0~~ | ~~Add `MultilingualModel` turn detector~~ ✅ Done | Small | High | bot_factory: [`agent.py` L37–46, L274–279](apps/agent/src/bot_factory_agent/agent.py); taina: [`main.py` L283–296](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/src/main.py#L283-L296) |
| ~~P0~~ | ~~Add BVC noise cancellation~~ ⏭️ Skipped | Small | High | Requires LiveKit Cloud; bot_factory uses self-hosted LiveKit — see §2 |
| ~~P1~~ | ~~Voice-format preamble in .NET `MasterPrompt`~~ ⏭️ Skipped | Small | High | Deferred until production issues — see §6 |
| ~~P1~~ | ~~TTS text sanitization~~ ⏭️ Skipped | Medium | High | Deferred until production issues — see §5 |
| ~~P2~~ | ~~Evaluate Deepgram STT (`nova-3`, `es-419`)~~ ⏭️ Skipped | Medium | Medium–High | No STT quality issue yet; ElevenLabs already on `es-419` — see §3 |
| ~~P2~~ | ~~`close_on_disconnect=False`~~ ✅ Done | Small | Medium | [`agent.py`](apps/agent/src/bot_factory_agent/agent.py) — §8 |
| ~~P2~~ | ~~In-process Gemini LLM (taina parity)~~ ⏭️ Skipped | — | — | Intentional .NET proxy — see §4 |
| ~~P3~~ | ~~Reuse `httpx.AsyncClient` in `BackendLLM`~~ ✅ Done | Small | Low–Medium | [`llm_backend.py`](apps/agent/src/bot_factory_agent/llm_backend.py) — per-job pooled client |
| ~~P3~~ | ~~Worker memory / load limits~~ ✅ Done | Small | Low–Medium | [`agent.py`](apps/agent/src/bot_factory_agent/agent.py) — §7 |

---

## Architecture snapshot

```text
taina_ogtic (agent process)
  LiveKit room
    → BVC (input)
    → Deepgram STT (nova-3, es-419)
    → Silero VAD + Multilingual turn detector
    → google.LLM (COMBINED_PROMPT + tools → ChromaDB)
    → elevenlabs.TTS
    → LiveKit audio out

bot_factory
  LiveKit room (self-hosted)
    → (no BVC — skipped; Cloud-only plugin)
    → ElevenLabs STT (scribe_v2_realtime, es-419)
    → Per-bot Silero VAD + Multilingual turn detector
    → BackendLLM → POST /livekit/chat/turn → Gemini (MasterPrompt + es-419 suffix in .NET)
    → ElevenLabs TTS (eleven_flash_v2_5, es-419)
    → LiveKit audio out
```

---

## Version notes

| | taina_ogtic | bot_factory |
|---|-------------|-------------|
| `livekit-agents` | 1.4.6 ([`requirements.txt` L121](https://github.com/public-intelligence/taina_ogtic/blob/master/backend/requirements.txt#L121)) | 1.5.1 ([`pyproject.toml` L8](apps/agent/pyproject.toml#L8)) |
| Worker pattern | `WorkerOptions` + `prewarm_fnc` | `AgentServer` + `@server.rtc_session` |

Plugin APIs may differ slightly between 1.4.x and 1.5.x when porting taina patterns into bot_factory.
