"""LiveKit voice worker: audio pipeline + raw AgentEvent relays.

LiveKit dispatches a job per room; we parse room.metadata (JSON from .NET), run STT→.NET SSE→TTS,
and forward session events to .NET (HTTP) and the browser (data channel) unchanged.
"""

from __future__ import annotations

import asyncio
import logging
from typing import Any

from livekit.agents import (
    Agent,
    AgentSession,
    AgentServer,
    JobContext,
    JobProcess,
)
from livekit.agents.voice.room_io import RoomInputOptions
from livekit.plugins import elevenlabs, silero
from livekit.plugins.turn_detector.multilingual import MultilingualModel

from bot_factory_agent.agent_helpers import (
    agent_event as _agent_event,
    enriched_error_data as _enriched_error_data,
    metrics_to_data as _metrics_to_data,
    require_eleven_key as _require_eleven_key,
)
from bot_factory_agent.config import (
    dev_room_metadata,
    load_env_config,
    load_worker_server_config,
    parse_room_metadata,
)
from bot_factory_agent.llm_backend import BackendLLM
from bot_factory_agent.relays import BackendRelay, ClientRelay
from bot_factory_agent.turn_metrics import TurnMetricsTracker
from bot_factory_agent.types import RoomMetadata

logger = logging.getLogger("agent")
# ElevenLabs Scribe realtime STT expects ISO 639-3 (e.g. spa), not BCP-47 es-419.
ELEVENLABS_STT_LANGUAGE_CODE = "spa"
# TTS language for eleven_flash_v2_5 (BCP-47-style code).
ELEVENLABS_TTS_LANGUAGE = "es"
# Read once at import; AGENT_NAME must match agent dispatch on the room (set when .NET creates it).
_env = load_env_config()
_worker = load_worker_server_config()

server = AgentServer(
    # Default prod: ceil(cpu_count()) → 1 on our 1-vCPU ACI. Keeps one warm job process for
    # faster dispatch without holding 2–3 idle ML stacks in RAM on a 2 GB box.
    num_idle_processes=_worker.num_idle_processes,
    # Default: 500. Voice jobs (VAD + turn detector + ElevenLabs) often exceed 500 MB when healthy;
    # 1600 warns only when a job is genuinely heavy on a 2 GB container.
    job_memory_warn_mb=_worker.job_memory_warn_mb,
    # Default: 0 (no cap). Kills a runaway job process near container size (~1900 MB) instead of
    # OOM-killing the whole ACI and every active call.
    job_memory_limit_mb=_worker.job_memory_limit_mb,
    # Default prod: 0.7. Worker stops accepting new jobs above this load; 0.8 accepts slightly
    # more work before marking unavailable (Taina uses 0.8).
    load_threshold=_worker.load_threshold,
    # Default: 10 s. Per-job init still builds MultilingualModel + per-bot VAD after worker prewarm;
    # 60 s avoids false init timeouts on cold or busy hosts (Taina uses 300 s).
    initialize_process_timeout=_worker.initialize_process_timeout,
)


def prewarm(proc: JobProcess) -> None:
    # Download HF weights once per worker process. MultilingualModel() must be created inside
    # entrypoint (needs job context for the shared inference executor).
    from livekit.plugins.turn_detector.multilingual import _EUORunnerMultilingual

    _EUORunnerMultilingual._download_files()
    logger.info("turn detector model files ready")


server.setup_fnc = prewarm


# ─────────────────────────────────────────────────────────────────────────────
# Per-bot Silero VAD cache.
# ─────────────────────────────────────────────────────────────────────────────
#
# WHY THIS EXISTS
# ---------------
# Silero VAD (Voice Activity Detection) is the component that decides "is the
# user currently speaking?". Its sensitivity is controlled by three params:
#
#   • activation_threshold  (0.0 – 1.0, default 0.5)
#       Energy/probability threshold to flag audio as speech. Higher = LESS
#       sensitive. Bumping this up (e.g. 0.6–0.7) makes the bot ignore
#       background noise (chatter, fans, keyboard clicks) at the cost of
#       missing very quiet speech.
#
#   • min_speech_duration  (seconds)
#       Audio must look like speech for at least this long before we treat
#       it as a real utterance. Filters out short blips (claps, coughs,
#       door slams). 50 ms = aggressive defaults; 200–300 ms = stricter.
#
#   • min_silence_duration  (seconds)
#       Minimum silence after speech (VAD floor). End-of-turn also uses the
#       multilingual turn detector (prewarmed on the worker). `silence_end_ms`
#       from the UI ("Fin de silencio") is fed here.
#
# These params come from the Bot row in .NET (Bot.cs:VadActivationThreshold,
# Bot.cs:VadMinSpeechDurationMs, Bot.cs:SilenceEndMs) and arrive in this
# process via LiveKit room metadata (LiveKitService.cs:RoomMetadata →
# config.py:parse_room_metadata).
#
# WHY A CACHE INSTEAD OF prewarm()
# --------------------------------
# Originally there was a single VAD loaded in `prewarm()` and shared by every
# session on this worker. That cannot work once params are per-bot: bot A
# wants threshold 0.6, bot B wants 0.4 — they can't share an instance.
#
# Instead, we lazily build (and cache) one VAD per *bot id*, keyed by the
# tuple of params used to build it. On a new session:
#
#   • cache hit + same params  → reuse the existing VAD (zero-cost).
#   • cache miss / param drift → load a fresh VAD with the new params and
#                                replace the cached entry.
#
# So a bot that never changes its VAD config pays the load cost exactly
# once on this worker. Updating the bot's settings transparently rebuilds
# its VAD on the next conversation. Other bots are untouched.
#
# Memory: Silero ONNX model is ~2 MB; even 50 distinct bots running on one
# worker is 100 MB — fine. If that ever becomes a concern, swap the dict
# for an LRU with a bounded capacity.
#
# Concurrency: livekit-agents runs jobs concurrently in the same Python
# process (asyncio). The cache lookup + assignment below is not strictly
# atomic, but in the worst case two simultaneous first-time sessions for
# the same bot will both load a VAD and the second write will win. That
# briefly wastes ~2 MB; correctness is unaffected. Not worth a lock.
# ─────────────────────────────────────────────────────────────────────────────

_VadParams = tuple[float, int, int]  # (activation_threshold, min_speech_ms, min_silence_ms)
_vad_cache: dict[int, tuple[_VadParams, silero.VAD]] = {}


def get_vad_for_bot(meta: RoomMetadata) -> silero.VAD:
    params: _VadParams = (
        meta.vad_activation_threshold,
        meta.vad_min_speech_duration_ms,
        meta.silence_end_ms,
    )
    cached = _vad_cache.get(meta.bot_id)
    if cached is not None and cached[0] == params:
        logger.info("vad cache hit bot_id=%s params=%s", meta.bot_id, params)
        return cached[1]

    logger.info("vad cache miss bot_id=%s params=%s; loading", meta.bot_id, params)
    vad = silero.VAD.load(
        activation_threshold=params[0],
        min_speech_duration=params[1] / 1000.0,
        min_silence_duration=params[2] / 1000.0,
    )
    _vad_cache[meta.bot_id] = (params, vad)
    return vad


@server.rtc_session(agent_name=_env.agent_name)
async def entrypoint(ctx: JobContext) -> None:
    # ctx.room.metadata is the JSON string .NET attached when creating the room;
    # ctx.job.metadata carries the secrets, which only this worker receives.
    ctx.log_context_fields = {"room": ctx.room.name}
    logger.info("job entry room=%s job_id=%s", ctx.room.name, ctx.job.id)

    # Resolve room metadata with retries: at job-dispatch time the room snapshot
    # is sometimes empty even when LiveKit has the metadata server-side (the
    # job-assignment message races with the room state sync). Briefly poll
    # `ctx.room.metadata` after a short wait before falling back to dev
    # defaults; in practice the first or second retry succeeds.
    raw_metadata = ctx.job.room.metadata or ctx.room.metadata
    if not (raw_metadata or "").strip():
        for _ in range(20):  # ~2s total
            await asyncio.sleep(0.1)
            raw_metadata = ctx.job.room.metadata or ctx.room.metadata
            if (raw_metadata or "").strip():
                break

    try:
        meta = parse_room_metadata(raw_metadata, ctx.job.metadata)
        logger.info(
            "room metadata parsed bot_id=%s conversation_id=%s",
            meta.bot_id,
            meta.conversation_id,
        )
    except Exception as e:
        # Local dev: invalid/empty metadata falls back to ELEVEN_API_KEY and placeholder ids.
        logger.warning("room metadata invalid, using dev defaults: %s", e)
        meta = dev_room_metadata()

    # Per-bot Silero VAD (cached by bot_id; reloaded only if VAD params change). See comment
    # above get_vad_for_bot for the full rationale.
    vad = get_vad_for_bot(meta)
    turn_detector = MultilingualModel()

    bot_id = meta.bot_id
    conversation_id = meta.conversation_id
    el_key = _require_eleven_key(meta)

    # Same ElevenLabs key for STT and TTS (contract with .NET); no LiveKit Cloud inference.
    stt = elevenlabs.STT(
        api_key=el_key,
        model_id="scribe_v2_realtime",
        language_code=ELEVENLABS_STT_LANGUAGE_CODE,
    )
    tts = elevenlabs.TTS(
        api_key=el_key,
        voice_id=meta.voice_id,
        model="eleven_flash_v2_5",
        language=ELEVENLABS_TTS_LANGUAGE,
    )
    # Shares a turn id between the backend chat-turn call and the per-stage
    # pipeline metrics so both waterfalls stitch together in the feed.
    turn_tracker = TurnMetricsTracker()

    backend_relay = BackendRelay(
        backend_url=_env.backend_url,
        shared_secret=_env.backend_shared_secret,
    )
    client_relay = ClientRelay(room=ctx.room)

    def _relay_provider_error(payload: dict[str, Any]) -> None:
        data = {
            "error": payload.get("operatorMessage") or payload.get("userMessage"),
            "source": {"label": "llm-gemini"},
            "errorCode": payload.get("errorCode"),
            "provider": payload.get("provider", "gemini"),
            "stage": payload.get("stage", "llm"),
            "userMessage": payload.get("userMessage"),
            "operatorMessage": payload.get("operatorMessage"),
        }
        err_event = _agent_event("Error", bot_id, conversation_id, data)
        backend_relay.send(err_event)
        client_relay.send(err_event)

    backend_llm = BackendLLM(
        backend_url=_env.backend_url,
        conversation_id=conversation_id,
        turn_id_provider=turn_tracker.ensure_turn,
        on_provider_error=_relay_provider_error,
        shared_secret=_env.backend_shared_secret,
    )

    # How long without user audio before the session marks the user "away" (framework state).
    user_away_timeout_sec = max(1.0, meta.no_speech_timeout_ms / 1000.0)

    turn_handling: dict[str, Any] = {
        "interruption": {
            "enabled": meta.enable_barge_in,
            # Python SDK: seconds of user speech before interrupting the agent (not milliseconds).
            "min_duration": meta.interruption_min_duration_ms / 1000.0,
            "min_words": meta.interruption_min_words,
        }
    }
    logger.info(
        "interruption gating bot_id=%s enabled=%s min_duration_ms=%s min_words=%s",
        meta.bot_id,
        meta.enable_barge_in,
        meta.interruption_min_duration_ms,
        meta.interruption_min_words,
    )

    session = AgentSession(
        vad=vad,
        turn_detection=turn_detector,
        user_away_timeout=user_away_timeout_sec,
        turn_handling=turn_handling,
    )

    no_speech_task: asyncio.Task[None] | None = None
    utterance_task: asyncio.Task[None] | None = None

    def clear_timers() -> None:
        nonlocal no_speech_task, utterance_task
        for t in (no_speech_task, utterance_task):
            if t is not None and not t.done():
                t.cancel()
        no_speech_task = None
        utterance_task = None

    async def on_shutdown(_reason: str = "") -> None:
        logger.info("graceful shutdown started")
        clear_timers()
        await backend_llm.aclose()
        logger.info("graceful shutdown complete")

    ctx.add_shutdown_callback(on_shutdown)

    # Network events go to .NET only (browser can infer from its own LiveKit connection).
    def on_reconnecting() -> None:
        backend_relay.send(
            _agent_event("NetworkStateChanged", bot_id, conversation_id, {"state": "reconnecting"})
        )

    def on_reconnected() -> None:
        backend_relay.send(
            _agent_event("NetworkStateChanged", bot_id, conversation_id, {"state": "reconnected"})
        )

    def on_disconnected(reason: object) -> None:
        # Log only (parity with agent-js); no AgentEvent — session close handler covers telemetry.
        logger.error("room disconnected", extra={"reason": str(reason)})

    try:
        ctx.room.on("reconnecting", on_reconnecting)
        ctx.room.on("reconnected", on_reconnected)
        ctx.room.on("disconnected", on_disconnected)
    except Exception as e:
        logger.debug("room reconnect/disconnect handlers not registered: %s", e)

    @session.on("user_input_transcribed")
    def _on_transcribed(ev: Any) -> None:
        lang = getattr(ev, "language", None)
        lang_s = str(lang) if lang is not None else None
        payload = _agent_event(
            "UserInputTranscribed",
            bot_id,
            conversation_id,
            {
                "transcript": ev.transcript,
                "isFinal": ev.is_final,
                "language": lang_s,
            },
        )
        backend_relay.send(payload)
        client_relay.send(payload)
        # Anchor the turn and emit turn_started so downstream events can be
        # correlated by TurnId from the moment the user utterance is committed.
        if ev.is_final:
            turn_id = turn_tracker.ensure_turn()
            ts = _agent_event("TurnStarted", bot_id, conversation_id, {"turnId": turn_id})
            backend_relay.send(ts)
            client_relay.send(ts)

    @session.on("agent_state_changed")
    def _on_agent_state(ev: Any) -> None:
        old_s = str(ev.old_state)
        new_s = str(ev.new_state)
        payload = _agent_event(
            "AgentStateChanged",
            bot_id,
            conversation_id,
            {"oldState": old_s, "newState": new_s},
        )
        backend_relay.send(payload)
        client_relay.send(payload)
        # Heuristic: user speech cut off agent playback (exact barge-in semantics live in .NET/UI).
        if meta.enable_barge_in and old_s == "speaking" and new_s == "listening":
            b = _agent_event("BargeIn", bot_id, conversation_id)
            backend_relay.send(b)
            client_relay.send(b)
        # The turn ends when the agent returns to listening (after speaking, or
        # after a thinking→listening error path). Flush the accumulated pipeline
        # waterfall as a single per-turn event.
        if new_s == "listening":
            turn_data = turn_tracker.flush()
            if turn_data is not None:
                tm = _agent_event("TurnCompleted", bot_id, conversation_id, turn_data)
                backend_relay.send(tm)
                client_relay.send(tm)

    @session.on("user_state_changed")
    def _on_user_state(ev: Any) -> None:
        nonlocal no_speech_task, utterance_task
        session_log = logging.getLogger("session")
        session_log.info("user state %s -> %s", ev.old_state, ev.new_state)

        if ev.new_state == "away":
            # Layered on top of user_away_timeout: once "away", wait again then end the session.
            async def _no_speech() -> None:
                await asyncio.sleep(meta.no_speech_timeout_ms / 1000.0)
                session_log.warning("no speech timeout, shutting down")
                paused = _agent_event(
                    "SessionPaused",
                    bot_id,
                    conversation_id,
                    {"reason": "no_speech_timeout"},
                )
                backend_relay.send(paused)
                client_relay.send(paused)
                session.shutdown()

            if no_speech_task is not None and not no_speech_task.done():
                no_speech_task.cancel()
            no_speech_task = asyncio.create_task(_no_speech())
        else:
            if no_speech_task is not None and not no_speech_task.done():
                no_speech_task.cancel()
            no_speech_task = None

        if ev.new_state == "speaking":
            # Cap single utterance length; forces turn commit so STT/LLM can run.
            async def _max_ut() -> None:
                await asyncio.sleep(meta.max_utterance_ms / 1000.0)
                session_log.warning("max utterance exceeded, committing turn")
                await session.commit_user_turn()

            if utterance_task is not None and not utterance_task.done():
                utterance_task.cancel()
            utterance_task = asyncio.create_task(_max_ut())
        else:
            if utterance_task is not None and not utterance_task.done():
                utterance_task.cancel()
            utterance_task = None

    @session.on("speech_created")
    def _on_speech_created(ev: Any) -> None:
        # Backend only: useful for analytics; client does not need raw speech_created.
        backend_relay.send(
            _agent_event(
                "SpeechCreated",
                bot_id,
                conversation_id,
                {"source": ev.source, "userInitiated": ev.user_initiated},
            )
        )

    @session.on("conversation_item_added")
    def _on_conv_item(ev: Any) -> None:
        # Client only: lightweight transcript hints; .NET already gets full context via chat/telemetry.
        item = ev.item
        role = getattr(item, "role", None)
        role_s = str(role) if role is not None else None
        text_content = getattr(item, "text_content", None)
        client_relay.send(
            _agent_event(
                "ConversationItemAdded",
                bot_id,
                conversation_id,
                {"role": role_s, "textContent": text_content},
            )
        )

    @session.on("metrics_collected")
    def _on_metrics(ev: Any) -> None:
        # Raw per-stage metrics stay persisted (out of feed) for deep debugging.
        payload = _agent_event(
            "MetricsCollected",
            bot_id,
            conversation_id,
            {"metrics": _metrics_to_data(ev.metrics)},
        )
        backend_relay.send(payload)
        client_relay.send(payload)
        # Fold the stage latency into the current turn's aggregate.
        turn_tracker.record(ev.metrics)

    @session.on("error")
    def _on_error(ev: Any) -> None:
        data = _enriched_error_data(ev.error, ev.source)
        logger.error(
            "session error code=%s provider=%s stage=%s error=%s",
            data.get("errorCode"),
            data.get("provider"),
            data.get("stage"),
            data.get("error"),
        )
        payload = _agent_event("Error", bot_id, conversation_id, data)
        backend_relay.send(payload)
        client_relay.send(payload)

    @session.on("close")
    def _on_close(ev: Any) -> None:
        # Session end signal for .NET cleanup and browser UI.
        clear_timers()
        err = getattr(ev, "error", None)
        reason = getattr(ev.reason, "value", ev.reason)
        close_data: dict[str, Any] = {"reason": str(reason)}
        if err is not None:
            close_data.update(_enriched_error_data(err, getattr(ev, "source", None)))
        close_payload = _agent_event("Close", bot_id, conversation_id, close_data)
        backend_relay.send(close_payload)
        client_relay.send(close_payload)

    await session.start(
        agent=Agent(
            instructions="",  # All prompting lives in .NET; keep the LiveKit agent slot empty.
            stt=stt,
            llm=backend_llm,
            tts=tts,
        ),
        room=ctx.room,
        # Default: True. False keeps the session alive through brief WebRTC disconnects (mobile, tab background).
        room_input_options=RoomInputOptions(close_on_disconnect=False),
    )

    started = _agent_event("ConversationStarted", bot_id, conversation_id)
    backend_relay.send(started)
    client_relay.send(started)
    logger.info("agent session started, waiting for user audio")

    # Speak the bot's configured greeting (if any) so the caller hears the bot first.
    # Interruptible: an impatient caller can cut in and VAD will pick up normally.
    # Skip on resume — the conversation already started; context lives in .NET history.
    if meta.greeting_message and not meta.is_resume:
        logger.info(
            "speaking greeting bot_id=%s len=%s", meta.bot_id, len(meta.greeting_message)
        )
        try:
            await session.say(meta.greeting_message, allow_interruptions=True)
        except Exception as e:
            logger.error("greeting playback failed: %s", e)
            err_data = _enriched_error_data(e, {"label": "tts-elevenlabs"})
            err_event = _agent_event("Error", bot_id, conversation_id, err_data)
            backend_relay.send(err_event)
            client_relay.send(err_event)
            close_payload = _agent_event(
                "Close",
                bot_id,
                conversation_id,
                {"reason": "greeting_failed", **err_data},
            )
            backend_relay.send(close_payload)
            client_relay.send(close_payload)
            session.shutdown()
            await ctx.connect()
            return

    # Completes when the room/session ends; keeps the job alive until then.
    await ctx.connect()
