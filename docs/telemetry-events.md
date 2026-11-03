# Telemetry Events

Every significant thing that happens during a conversation is recorded as a row in the `telemetry_events` table. This document describes every event, where it comes from, and what it means.

## Row structure

| Column | Description |
|---|---|
| `EventName` | The event identifier (listed below) |
| `Source` | Who emitted it: `Agent` (the Python voice agent) or `Backend` (the .NET API) |
| `Stage` | Which pipeline phase it belongs to: `lifecycle`, `stt`, `llm`, `tts`, `eou`, `vad`, `pipeline`, or `retrieval` |
| `TurnId` | A UUID that groups all events belonging to the same user turn. Null for session-level events that don't belong to any specific turn. |
| `OccurredAt` | UTC timestamp with microsecond precision, set by the emitter at the moment the event happens |
| `Data` | A JSON blob with event-specific fields (described per event below) |
| `Summary` | A short human-readable description pre-computed at ingestion time, visible in the feed |

---

## What is a "turn"?

A turn is one full exchange: the user says something, the system processes it, the agent speaks a response. Every turn gets a unique `TurnId`. Events that belong to a turn carry that ID so you can filter and measure exactly what happened during that exchange.

Not all events have a `TurnId` — session-level events (connection, pause, errors) happen outside of any specific turn.

---

## Turn lifecycle

A complete turn produces events in this order:

```
user_input_transcribed      (no TurnId)  — the user's speech has been converted to text
turn_started                TurnId: X    — the system commits to processing this turn
agent_state_changed         (no TurnId)  — agent transitions from listening to thinking
llm_started                 TurnId: X    — the AI model begins generating a response
  [llm_first_token]         TurnId: X    — the first word of the response arrives
  [kb_retrieved × N]        TurnId: X    — knowledge base was queried (one per lookup)
  [llm_error]               TurnId: X    — something went wrong during response generation
backend_completed           TurnId: X    — the backend is done generating and saving the response
agent_state_changed         (no TurnId)  — agent transitions from thinking to speaking
agent_state_changed         (no TurnId)  — agent transitions from speaking to listening
[barge_in]                  (no TurnId)  — the user interrupted the agent mid-sentence
turn_completed              TurnId: X    — the agent has finished speaking; turn is fully done
```

Events in `[brackets]` are conditional — they only appear when that situation occurs.

---

## Session events

These fire once per conversation and are not tied to any specific turn.

### `conversation_started`
**Source:** Agent

Emitted when the voice agent has fully connected to the session and is ready to listen. This is the first event of every conversation — it means the agent is live and waiting for the user to speak.

### `conversation_ended`
**Source:** Backend

Emitted when the conversation is closed, regardless of the reason. This could be the user hanging up, a session timeout, or the system shutting the conversation down explicitly. Nothing further will be recorded after this event.

### `session_paused`
**Source:** Agent

Emitted when the voice session is put on hold — either because the network connection dropped and the agent is waiting to reconnect, or because a pause was requested explicitly. The conversation is not ended; it can be resumed.

### `session_resumed`
**Source:** Backend

Emitted when a paused session comes back to life and the agent is listening again.

---

## Turn events

These all carry a `TurnId` and together describe the full arc of one exchange.

### `turn_started`
**Source:** Agent | **Stage:** `lifecycle`

Emitted the moment the user's speech-to-text transcription is finalised and committed — right before the system starts thinking about a response. This is the earliest point at which the `TurnId` exists, making it the reliable start anchor for measuring how long any part of the turn took.

### `llm_started`
**Source:** Backend | **Stage:** `llm`

Emitted just before the backend sends the user's message to the AI language model (Gemini). By this point, the backend has already loaded the conversation history and built the full context. The time between `turn_started` and `llm_started` represents the overhead of that setup work.

### `llm_first_token`
**Source:** Backend | **Stage:** `llm`

Emitted when the very first word (token) of the AI's response streams back. The time from `llm_started` to `llm_first_token` is the Time To First Token (TTFT) — a key latency indicator for how quickly the model started responding.

### `backend_completed`
**Source:** Backend | **Stage:** `llm`

Emitted when the backend has finished everything it needs to do for this turn: all AI response tokens received, any tool calls executed, and the assistant's message saved to the database. The `Data` field contains the full breakdown of what happened: each Gemini inference span, each tool call duration, total time, time to first token, number of characters generated, and the outcome (success, error, etc.).

### `turn_completed`
**Source:** Agent | **Stage:** `pipeline`

Emitted when the agent finishes speaking the response and returns to listening — the true end of the turn. The `Data` field contains the agent's view of the full pipeline: how long end-of-utterance detection took (`eouMs`), speech-to-text (`sttMs`), the AI model call (`llmMs`), and text-to-speech (`ttsMs`). These timings are measured from the agent's perspective and complement the backend waterfall in `backend_completed`.

---

## Speech-to-text

### `user_input_transcribed`
**Source:** Agent | **Stage:** `stt`

Emitted when speech-to-text has finished converting the user's audio into text. The voice agent continuously transcribes as the user speaks, producing partial drafts along the way — those are discarded and never stored. Only the final, complete transcript is recorded here. `Data.transcript` contains the recognised text.

### `stt_failed`
**Source:** Agent | **Stage:** `stt`

Emitted when the speech-to-text service (ElevenLabs) returned an error and could not produce a transcript. The turn may still continue if the system falls back gracefully.

---

## Agent state

### `agent_state_changed`
**Source:** Agent | **Stage:** `lifecycle`

Emitted every time the voice agent transitions between internal states. The `Summary` field shows the transition in the form `"old → new"`. The four states are:

- **`initializing`** — the agent is starting up and not yet ready
- **`listening`** — the agent is waiting for the user to speak
- **`thinking`** — the user has finished speaking; the system is generating a response
- **`speaking`** — the agent is playing the response back to the user

### `barge_in`
**Source:** Agent | **Stage:** `lifecycle`

Emitted when the user cuts off the agent while it is still speaking — the agent transitions from `speaking` back to `listening` because it detected user speech. This is a synthetic event generated alongside the corresponding `agent_state_changed`; it exists as a distinct signal so the feed can highlight interruptions clearly. Only emitted when the `enableBargeIn` setting is true for the bot.

---

## AI model and knowledge base

### `llm_error`
**Source:** Backend or Agent | **Stage:** `llm`

Emitted when the AI language model call fails. When the backend emits it, it means Gemini returned an error or the maximum number of tool-call iterations was exhausted; this always appears before `backend_completed` on the same turn. When the agent emits it, it is a separate detection path on the agent side.

### `kb_retrieved`
**Source:** Backend | **Stage:** `retrieval`

Emitted each time the system queries the knowledge base as part of generating a response. `Data` contains the search query, the number of chunks returned, and their relevance scores. A single turn can produce multiple `kb_retrieved` events if the AI queries the knowledge base more than once.

---

## External tools

One terminal event per configured-tool execution attempt, sharing the turn's `TurnId`. `Data` always carries `tool`, `outcome`, `httpStatus`, `latencyMs`, `responseBytes`, `retryCount`, `errorCategory`, the redacted `requestSummary`, and `executionId` (the `tool_executions` governance row). Built-in tools (`kb_search`) keep their own events and never emit these.

### `tool_call_completed`
**Source:** Backend | **Stage:** `tool`

The external service answered successfully and its (redacted, size-capped) result was returned to the model.

### `tool_call_failed`
**Source:** Backend | **Stage:** `tool`

The execution failed: invalid model-proposed arguments (`errorCategory: validation`), timeout, network/connector error, or a non-2xx HTTP response. The model receives a structured error payload and is instructed to say the service is unavailable — never to invent a result.

### `tool_call_blocked`
**Source:** Backend | **Stage:** `tool`

The deterministic policy layer denied the call before any network egress (tool unpublished, connection disabled, authentication required for an anonymous conversation, or per-conversation call cap reached). `errorCategory` carries the machine reason code.

### `tts_failed`
**Source:** Agent | **Stage:** `tts`

Emitted when the text-to-speech service (ElevenLabs) failed to convert the response text into audio.

### `agent_error`
**Source:** Agent | **Stage:** `agent`

Emitted for generic agent-level errors that cannot be attributed to a specific pipeline stage like STT or TTS.

---

## Network

### `network_degraded`
**Source:** Agent | **Stage:** `lifecycle`

Emitted when the LiveKit connection between the agent and the session begins to reconnect after a disruption. The conversation is not ended; the agent will attempt to restore the connection.

### `network_recovered`
**Source:** Agent | **Stage:** `lifecycle`

Emitted when the LiveKit connection is successfully restored after a `network_degraded` event.

---

## Internal events (not shown in the feed)

These events are stored in the database for diagnostics but are intentionally excluded from the conversation feed UI to avoid clutter.

### `metrics_collected`
**Source:** Agent | **Stage:** `stt` / `llm` / `tts` / `eou` / `vad`

Low-level latency samples emitted by LiveKit for each pipeline stage as they complete. These are high-volume — there can be many per turn. The pipeline timing summary shown in `turn_completed` (`eouMs`, `sttMs`, etc.) is derived by aggregating these raw samples. Useful for deep latency debugging but not meaningful on their own.

### `speech_created`
**Source:** Agent | **Stage:** `tts`

Emitted when the text-to-speech engine has finished synthesising the audio buffer and it is ready to be played. Fires just before the agent transitions to the `speaking` state.

### `conversation_item_added`
**Source:** Agent | **Stage:** `lifecycle`

Emitted when LiveKit appends a new item to the internal conversation context (e.g. a user message or assistant reply). This is a low-level LiveKit bookkeeping event, not meaningful at the product level.
