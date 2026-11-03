import {
  type JobContext,
  type JobProcess,
  defineAgent,
  ServerOptions,
  cli,
  llm,
  voice,
} from "@livekit/agents";
import * as deepgram from "@livekit/agents-plugin-deepgram";
import * as silero from "@livekit/agents-plugin-silero";
import dotenv from "dotenv";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
dotenv.config({ path: path.join(__dirname, "../../.env") });

const log = (component: string, msg: string, data?: Record<string, unknown>) => {
  const timestamp = new Date().toISOString();
  const extra = data ? ` ${JSON.stringify(data)}` : "";
  console.log(`[${timestamp}] [${component}]  ${msg}${extra}`);
};

// Fake LLM that echoes back the last user message.
// In the real product, this is where you'd call .NET backend via POST /api/chat/turn.
class EchoLLM extends llm.LLM {
  label() {
    return "echo-llm";
  }

  chat({
    chatCtx,
    toolCtx,
    connOptions,
  }: {
    chatCtx: llm.ChatContext;
    toolCtx?: llm.ToolContext;
    connOptions?: { maxRetry: number; retryIntervalMs: number; timeoutMs: number };
    parallelToolCalls?: boolean;
    toolChoice?: llm.ToolChoice;
    extraKwargs?: Record<string, unknown>;
  }): llm.LLMStream {
    log("echo-llm", "chat() called", { messageCount: chatCtx.items.length });
    return new EchoLLMStream(this, chatCtx);
  }
}

class EchoLLMStream extends llm.LLMStream {
  constructor(echoLlm: EchoLLM, chatCtx: llm.ChatContext) {
    super(echoLlm, {
      chatCtx,
      connOptions: { maxRetry: 0, retryIntervalMs: 0, timeoutMs: 5000 },
    });
  }

  protected async run(): Promise<void> {
    const lastMsg = this.chatCtx.items.findLast(
      (m: any) => m.type === "message" && m.role === "user",
    ) as llm.ChatMessage | undefined;
    const text = lastMsg?.textContent ?? "I didn't catch that";

    log("echo-llm", "generating echo response", { userText: text });

    this.output.put({
      id: crypto.randomUUID(),
      delta: { role: "assistant", content: `You said: ${text}` },
    });
    this.output.close();
  }
}

// Echo agent — repeats back what the user said, no real LLM needed.
class EchoAgent extends voice.Agent {
  constructor() {
    super({
      instructions: "",
      stt: new deepgram.STT(),
      llm: new EchoLLM(),
      tts: new deepgram.TTS(),
    });
    log("agent", "EchoAgent created", {
      stt: "deepgram",
      tts: "deepgram",
      llm: "echo",
    });
  }
}

export default defineAgent({
  prewarm: async (proc: JobProcess) => {
    log("agent", "prewarm started, loading Silero VAD model");
    proc.userData.vad = await silero.VAD.load();
    log("agent", "prewarm complete, VAD model loaded");
  },
  entry: async (ctx: JobContext) => {
    log("agent", "job entry", {
      roomName: ctx.room.name,
      jobId: ctx.job.id,
    });

    const session = new voice.AgentSession({
      vad: ctx.proc.userData.vad! as silero.VAD,
    });

    const E = voice.AgentSessionEventTypes;

    session.on(E.UserInputTranscribed, (ev) => {
      log("session", "user input transcribed", {
        transcript: ev.transcript,
        isFinal: ev.isFinal,
      });
    });

    session.on(E.AgentStateChanged, (ev) => {
      log("session", "agent state changed", {
        from: ev.oldState,
        to: ev.newState,
      });
    });

    session.on(E.UserStateChanged, (ev) => {
      log("session", "user state changed", {
        from: ev.oldState,
        to: ev.newState,
      });
    });

    session.on(E.SpeechCreated, (ev) => {
      log("session", "speech created", {
        source: ev.source,
        userInitiated: ev.userInitiated,
      });
    });

    session.on(E.ConversationItemAdded, (ev) => {
      log("session", "conversation item added", {
        role: ev.item.role,
      });
    });

    session.on(E.MetricsCollected, (ev) => {
      log("session", "metrics", {
        metrics: ev.metrics,
      });
    });

    session.on(E.Error, (ev) => {
      log("session", "ERROR", {
        error: String(ev.error),
        source: String(ev.source),
      });
    });

    session.on(E.Close, (ev) => {
      log("session", "session closed", {
        reason: ev.reason,
        error: ev.error ? String(ev.error) : null,
      });
    });

    log("agent", "starting agent session");
    await session.start({
      agent: new EchoAgent(),
      room: ctx.room,
    });
    log("agent", "agent session started, waiting for user audio");
  },
});

cli.runApp(
  new ServerOptions({
    agent: fileURLToPath(import.meta.url),
    agentName: "echo-agent",
  }),
);
