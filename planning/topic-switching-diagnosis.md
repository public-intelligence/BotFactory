# Diagnosis: "Pura Vida IA RNP" gets stuck on topics, ignores questions, is verbose

**Date:** 2026-07-22
**Trigger:** Tester feedback (WhatsApp) on the *Pura Vida IA RNP* voice bot:
1. *"te ignora en algunas preguntas como si no te oyera"* — ignores some questions as if it didn't hear you.
2. *"es muy explícita, lo da todo o las respuestas son muy largas"* — too explicit / answers too long.
3. *"cuando le cambio la pregunta sigue con la anterior… hasta que le digo 'no es de eso', entonces pide disculpas"* — on a topic switch it keeps answering the previous topic until told "that's not it". The app developer independently observed this **correlates strongly with tool/function calls**.

**Method:** 5 parallel code-audit agents (history/contents, concurrency, model-config/prompt, audio, prior-art) + adversarial synthesis, with every headline claim re-verified by hand against the code and git history. Ultracode multi-agent run.

> **Status (2026-07-22) — implemented on branch `fix/voice-topic-switching-and-verbosity` (off updated `main`):**
> - **#1** — removed `firstUserText = null` (renamed to `currentUserText`) so the question stays in every pass. `ChatTurnService.cs`.
> - **#2** — added `generationConfig` (`temperature 0.3`, `maxOutputTokens 512`, `thinkingBudget 0`), all tunable via `GeminiChatOptions`. `GeminiChatService.cs` / `GeminiChatOptions.cs`.
> - **#3** — added a voice-behavior clause (answer latest question, drop prior topic, be brief) to `SpeechLanguagePrompt.SystemInstructionSuffix`.
> - **#5** — trailing unanswered user turns (from a cancelled/superseded turn) are now dropped from history before the next turn is built, so the model no longer sees two consecutive user turns and answer the abandoned question. `ChatTurnService.cs`.
> - **Tests** — new unit test (`GeminiChatServiceTests`, ordering invariant) + two end-to-end regression tests (`ExternalToolsChatTurnTests.ChatTurn_PostToolAnswerPass_IncludesCurrentUserQuestion`, `LiveKitChatTurnTests.ChatTurn_DanglingUserFromCancelledTurn_DroppedFromHistory`). Build clean; 12 non-DB unit tests pass locally. Integration tests need Docker/Testcontainers (unavailable locally) — they run in Azure DevOps PR CI.
> - **Not implemented (needs decisions/tuning):** #4 audio gating (per-bot DB config, not code — see below), #6 per-conversation serialization + history window.

---

## TL;DR

The developer's instinct — "it's how message history is sent/interpreted" — is **half right, and the exact fault is a one-line regression**.

- **Symptom 3 (stuck on previous topic, correlated with tool calls) has a deterministic root cause:** on the *second* Gemini pass after any tool call, the user's **current question is missing from the request**. The code carries the question only in `firstUserText`, then sets `firstUserText = null` after the first tool call ([ChatTurnService.cs:346](../apps/backend/BotFactory.Infrastructure/Services/ChatTurnService.cs)). This was **fixed in May 2026 (commit `e42fff8`) and silently reintroduced** when the orchestrator moved into `ChatTurnService` (commit `f1491fb`). The `{solicitud, resultado}` anchoring added later is a band-aid for exactly this and, per the team's own [conversation-quality-review.md](conversation-quality-review.md) §2.8, *"reduced but did not eliminate"* it.
- **Symptom 2 (too long)** is a separate cause: **no `generationConfig` is ever sent** — no `maxOutputTokens`, no `temperature`, no `thinkingConfig`. `gemini-2.5-flash` runs unbounded, high-temperature, thinking-on.
- **Symptom 1 (ignored)** is substantially an **audio-layer** issue: `VadActivationThreshold = 0.80` (vs library default 0.5) and `InterruptionMinDurationMs = 1500` (Bot.cs) block soft/short utterances and quick corrections — and a dropped utterance never reaches .NET at all.

So: one deterministic orchestration bug + several independent config/prompt gaps. Not one thing.

---

## Root causes, ranked

### #1 — Post-tool user-utterance drop  ·  explains: stuck, (ignores)  ·  confidence high  ·  **REGRESSION**

**Mechanism.** Per turn, `ChatTurnService.ExecuteAsync`:
1. loads history **before** saving the incoming user message ([lines 78–83](../apps/backend/BotFactory.Infrastructure/Services/ChatTurnService.cs) vs 91–92) → history never contains the current question;
2. carries the current question only via `firstUserText` (line 104);
3. after the first tool call, sets `firstUserText = null` (line 346);
4. `BuildStreamRequest` appends `newUserText` **only when non-empty** ([GeminiChatService.cs:154](../apps/backend/BotFactory.Infrastructure/Services/GeminiChatService.cs)).

Result — the answer-generation pass (iteration 2+) sends:
```
contents = [ …prior-turn history (previous topic)… ] + [ model: functionCall ] + [ user: functionResponse{solicitud, resultado} ]
```
with **no standalone user turn for the current question**. The last natural-language user utterance in the whole prompt is the *previous* turn's question, so the model keeps answering the previous topic. This fires on **every** tool call → precisely the developer's "correlates strongly with tool calls." Turns with no tool call keep `firstUserText` and don't drift.

**Verified regression.** `git show e42fff8` message: *"Fix Gemini 400: newUserText must precede extraContents… and **firstUserText must not be nulled out so subsequent tool iterations include the original user turn**"* — its diff removes `firstUserText = null`. `f1491fb` reintroduced it at ChatTurnService.cs:346. No running CI caught it (see Gaps).

**Fix.** Delete line 346 (keep `firstUserText` across iterations). The current ordering already puts `newUserText` before `extraContents`, so the result is the canonical, correctly-alternating shape:
```
[ …history… ] + [ user: question ] + [ model: fc1, user: fr1, model: fc2, user: fr2 ]
```
The question appears once; no duplicate turn, no Gemini 400. The `{solicitud, resultado}` band-aid then becomes redundant (leave it or simplify separately). **Must ship with** a regression test that passes *both* a non-null `newUserText` and `extraContents` — today no test does, so the regression is invisible.

### #2 — No `generationConfig`  ·  explains: verbose, (stuck, ignores)  ·  confidence high  ·  quick win

`BuildStreamRequest` emits only `{systemInstruction, contents, tools, toolConfig}` ([GeminiChatService.cs:172–185](../apps/backend/BotFactory.Infrastructure/Services/GeminiChatService.cs)); repo-wide grep finds zero `temperature`/`maxOutputTokens`/`thinkingConfig`/`topP`. Unbounded length → long answers. Default temperature (~1.0) → topic-drift variance. Thinking-on → reasoning latency before the first delta on every generation (tool turns run *two* generations), feeding the "as if it didn't hear you" silence (secondary to the ~1.7 s EOU that dominates per the quality review).

**Fix.** Add a `generationConfig`: `maxOutputTokens` sized for spoken replies (~200–400), `temperature` ~0.2–0.4, `thinkingConfig.thinkingBudget = 0` (or small). Make each configurable via `GeminiChatOptions`.

### #3 — No platform-level voice-behavior instruction  ·  explains: verbose, stuck  ·  confidence medium  ·  quick win

The assembled system prompt is `ToolInstructionPrompt.Append(SpeechLanguagePrompt.BuildSystemInstruction(MasterPrompt), EnabledTools)`. The Spanish suffix ([SpeechLanguagePrompt.cs:10](../apps/backend/BotFactory.Core/Services/SpeechLanguagePrompt.cs)) only enforces language; `ToolInstructionPrompt` is appended **only when `EnabledTools > 0`**. Pura Vida is KB-only (`kb_search` is built-in, not in `EnabledTools`) → the entire system prompt is just `bot.MasterPrompt` + Spanish suffix. Nothing instructs *answer the latest question*, *drop the prior topic on a subject change*, or *be brief for voice*.

**Fix.** Add a short voice-behavior clause to the suffix (latest-question focus, drop prior topic, brevity). Separately, **read the production `MasterPrompt`** for this bot — for a KB-only bot it *is* the whole system prompt, so if it encourages exhaustiveness it is the dominant driver of Symptom 2. (Not in source; `DbSeeder` seeds only an empty-prompt sample bot.)

### #4 — Audio gating too aggressive  ·  explains: ignores, stuck  ·  confidence medium  ·  quick win (per-bot config)

Production defaults ([Bot.cs:40–47](../apps/backend/BotFactory.Core/Entities/Bot.cs)): `VadActivationThreshold = 0.80`, `VadMinSpeechDurationMs = 400`, `InterruptionMinDurationMs = 1500`, `InterruptionMinWords = 1`.
- **VAD 0.80** is well above the 0.5 library default; the agent's own comment ([agent.py:86](../apps/agent/src/bot_factory_agent/agent.py)) warns it "misses very quiet speech." A soft WhatsApp caller's short utterance can fail to trip VAD → no STT → no turn commit → **no POST to .NET at all**. The only channel that delivers user speech to .NET is the LLM-node POST on turn commit ([llm_backend.py:129](../apps/agent/src/bot_factory_agent/llm_backend.py)); `UserInputTranscribed` is telemetry-only. So a dropped utterance is *invisible*, not delayed — this is the cleanest explanation of "ignores you as if it didn't hear you."
- **Barge-in 1500 ms**: a ~1 s Spanish correction ("no, es de la cédula") can't interrupt a long answer, so the bot keeps talking until the user speaks long enough ("no es de eso") to cross the gate, then apologizes and switches. Correlates with tool calls because tool-backed answers are long, widening the no-barge-in window.

**Fix (config-only, per-bot).** Lower `VadActivationThreshold` toward 0.5–0.6, `VadMinSpeechDurationMs` toward 200–300, `InterruptionMinDurationMs` toward 400–600. Tune against real recordings. **Confirm first** the livekit-agents 1.5.1 `discard_audio_if_uninterruptible` behavior — it decides whether the gate causes outright loss (Symptom 1) or mere delay (Symptom 3).

### #5 — Dangling user message on cancelled turns  ·  explains: stuck, ignores  ·  confidence medium

A cancelled voice turn saves the user message ([lines 85–93](../apps/backend/BotFactory.Infrastructure/Services/ChatTurnService.cs)) but **not** the assistant reply (guarded by `!wasCancelled`, line 434). Next turn, history ends on a dangling user question and the new question is appended → two consecutive user turns; the model tends to answer the earlier one. The voice path cancels solely on HTTP `RequestAborted` (no `ChatTurnRegistry` for voice), and tool latency widens the barge-in cancellation window — a *second*, tool-correlated path to stickiness. Lower base rate than #1 (needs a mid-turn re-ask).

**Fix.** Persist user+assistant atomically at turn end, or tombstone the user row on the `wasCancelled` path, or defensively drop a trailing user turn with no following assistant in `BuildStreamRequest`. Add a test for the interrupt-during-tool-call sequence.

### #6 — No per-conversation serialization + unbounded history  ·  explains: stuck, verbose  ·  confidence low  ·  amplifier

Each `POST /livekit/chat/turn` gets its own scoped `DbContext`/`ChatTurnService` (Program.cs:50, 237); nothing serializes turns per `conversationId` (the singleton `ChatTurnRegistry` is text-path/`turnId`-only). While turn N is blocked in a tool call (up to 60 s) it can't observe `RequestAborted`, so a re-ask can run concurrently and write rows out of order. Separately, the history query has no window/summarization, so verbose prior answers accumulate and add topic inertia.

**Fix.** Per-`conversationId` async gate that deterministically cancels the superseded in-flight turn; history window (last N turns); overlap/dangling telemetry to measure real frequency.

> **Refuted / de-prioritized:** message *ordering* is reliable (`Id` IDENTITY tiebreaker, so coarse `CreatedAt` can't interleave). Cross-turn exclusion of `Tool`-role rows is benign on its own. Empty/whitespace silent-turn guards are well tested. `max_utterance`/`no_speech_timeout` are edge-only.

---

## Recommended sequencing

**Phase 1 — quick wins (low risk, high leverage; mostly config/prompt):**
- Add `generationConfig` (#2) — caps verbosity, cuts latency, lowers drift variance.
- Add voice-behavior clause to the system suffix (#3) and read the production `MasterPrompt`.
- Lower VAD + barge-in thresholds for this bot (#4), after confirming SDK discard behavior.

**Phase 2 — the deterministic fix (needs a test + manual suite run since no CI):**
- Remove `firstUserText = null` (#1) and add the missing regression test asserting `newUserText` precedes `extraContents` with both present.

**Phase 3 — hardening:**
- Dangling-user-message fix (#5) + defensive same-role collapse / user-first guarantee in `BuildStreamRequest`.
- Per-conversation serialization + history window (#6).
- Stand up real CI (below).

---

## How to verify

- **#1 (decisive):** capture the raw Gemini request bodies on a two-turn repro — turn 1 topic A (fires `kb_search`), turn 2 switch to topic B (fires a tool). Assert turn 2's *second-pass* `contents` does **not** contain the literal topic-B question as a user part (only inside `solicitud`). Apply the fix; confirm it's present and the answer is on-topic. Run the full local `dotnet test` and confirm no Gemini 400 (ordering) regression.
- **#2:** same RNP question with/without `maxOutputTokens`; measure streamed answer length; confirm no mid-sentence truncation.
- **#4:** on a real phone/WhatsApp path, a soft ~1 s utterance with VAD 0.80 → confirm no POST fires (agent + backend logs); lower to 0.55 → confirm a turn commits. During a long answer, a ~1 s correction can't barge in at 1500 ms but can at ~500 ms.
- **#5:** force a barge-in during a slow tool turn, send a new turn; query `messages` for two consecutive `User` rows with no assistant between; confirm the next answer targets the OLD question; confirm the fix removes the double-user state.

## Test & CI gaps (why this shipped)

- **No test locks the #1 invariant.** `GeminiChatServiceTests.StreamAsync_ExtraContents_AppearInRequestPayload` passes `newUserText = null` and *asserts* `contents[0].role == "model"` — it encodes the malformed shape. `ExternalToolsChatTurnTests.ChatTurn_FunctionResponse_IncludesSolicitudAndResultado` only checks `EXP-9` appears via `solicitud`, never the literal question. Both pass with the bug present.
- **No test** covers the mid-turn cancellation path (user saved, assistant not).
- **CI now runs the tests (as of `0d5a91a`, on `main`).** The `.github/workflows/backend-tests.yml` GitHub Actions workflow is still inert (origin is Azure DevOps), but `.azure/backend.yml` now runs `dotnet test apps/backend/BotFactory.slnx` on every PR to `main` (deploy steps skipped on PRs) — so the new regression tests above will execute in CI. The `firstUserText = null` regression predates this pipeline step, which is why it shipped uncaught; it would now be caught by the added test.

## Open questions

- Production `MasterPrompt` for *Pura Vida IA RNP* is not in source; needed to confirm Symptom 2's dominant driver (read-only DB).
- livekit-agents 1.5.1 `discard_audio_if_uninterruptible` behavior (loss vs delay) — not installed in this checkout; confirm from the deploy env or a live session log.
- Real-world frequency of turn cancellation/overlap — needs production telemetry to weight #5/#6 vs the deterministic #1.
