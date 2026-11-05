import type { MessageResponse } from '../../api/models/message-response';
import type { TelemetryEventResponse } from '../../api/models/telemetry-event-response';

export interface ToolDisplayItem {
  id: string;
  label: string;
  body: string;
}

export interface ToolInvocationData {
  id: number;
  name: string;
  summary: string | null;
  latencyMs: number | null;
  items: ToolDisplayItem[];
}

/**
 * Tokens Gemini reported (`usageMetadata`). `promptTokens` already includes the
 * cached part, so the cache share is `cachedTokens / promptTokens`.
 */
export interface TokenCounts {
  promptTokens: number;
  outputTokens: number;
  cachedTokens: number;
  thoughtsTokens: number;
}

/** The tokens of a whole turn: the sum over every Gemini call it made. */
export interface TurnTokens extends TokenCounts {
  calls: number;
  /** Calls for which Gemini sent no usageMetadata (their tokens are not in the sums). */
  unreportedCalls: number;
  totalTokens: number;
  cachedPercent: number;
}

/** One span in a turn's backend waterfall — a Gemini inference or a tool call. */
export interface TurnSegment {
  type: 'gemini' | 'tool';
  name?: string;
  ms: number;
  ttftMs?: number | null;
  /** Gemini spans only; null when Gemini reported no usage for the call. */
  tokens?: TokenCounts | null;
}

/** Parsed `turn_completed` event: the per-turn backend latency breakdown. */
export interface TurnSummaryData {
  id: number;
  turnId: string | null;
  ttftMs: number | null;
  totalMs: number | null;
  outcome: string | null;
  toolCount: number | null;
  charCount: number | null;
  /** Null for turns recorded before token reporting existed. */
  tokens: TurnTokens | null;
  segments: TurnSegment[];
}

/** One stage in a turn's agent-pipeline waterfall (EOU → STT → LLM → TTS). */
export interface TurnPipelineStage {
  key: string;
  label: string;
  ms: number;
}

/** Parsed `turn_completed` event (agent): the per-turn agent-pipeline latency breakdown. */
export interface TurnMetricsData {
  id: number;
  turnId: string | null;
  stages: TurnPipelineStage[];
  llmTtftMs: number | null;
  ttsTtfbMs: number | null;
}

/** A token count as the panel shows it: thousands separated, e.g. `58,632`. */
export function formatTokenCount(n: number): string {
  return n.toLocaleString('es-DO');
}

/** The tokens of a whole conversation (the API's `tokens`), in one line for the view's header. */
export function tokenUsageLabel(u: {
  totalTokens: number;
  promptTokens: number;
  cachedTokens: number;
  outputTokens: number;
  thoughtsTokens: number;
}): string {
  const pct = u.promptTokens > 0 ? Math.round((100 * u.cachedTokens) / u.promptTokens) : 0;
  const parts = [
    `${formatTokenCount(u.totalTokens)} tokens`,
    `entrada ${formatTokenCount(u.promptTokens)} (${pct} % caché)`,
    `salida ${formatTokenCount(u.outputTokens)}`,
  ];
  if (u.thoughtsTokens > 0) parts.push(`razonamiento ${formatTokenCount(u.thoughtsTokens)}`);
  return parts.join(' · ');
}

export type ConversationItem =
  | { kind: 'message'; occurredAt: string; data: MessageResponse }
  | { kind: 'event'; occurredAt: string; data: TelemetryEventResponse }
  | { kind: 'tool-invocation'; occurredAt: string; data: ToolInvocationData }
  | { kind: 'turn-summary'; occurredAt: string; data: TurnSummaryData }
  | { kind: 'turn-metrics'; occurredAt: string; data: TurnMetricsData };

export function messageItem(m: MessageResponse): ConversationItem {
  return { kind: 'message', occurredAt: m.createdAt, data: m };
}

export function eventItem(e: TelemetryEventResponse): ConversationItem {
  return { kind: 'event', occurredAt: e.occurredAt, data: e };
}

/**
 * Build a tool-invocation feed item from a tool telemetry event whose `Data`
 * payload includes a `display: { name, items }` block emitted by the backend
 * tool registry. The human summary lives on the event itself (`e.summary`),
 * shared with non-tool events.
 *
 * Returns null only when this isn't a tool event (no `display` block at all).
 * Empty `items` is allowed — the visible-items filter decides whether to render
 * the row in user mode.
 */
export function toolInvocationItem(e: TelemetryEventResponse): ConversationItem | null {
  if (!e.data) return null;
  try {
    const parsed = JSON.parse(e.data) as {
      display?: {
        name?: string;
        items?: Array<{ id: string; label: string; body: string }>;
      } | null;
    };
    const display = parsed.display;
    if (!display || !Array.isArray(display.items)) return null;
    return {
      kind: 'tool-invocation',
      occurredAt: e.occurredAt,
      data: {
        id: e.id,
        name: display.name ?? e.eventName,
        summary: e.summary,
        latencyMs: e.latencyMs,
        items: display.items,
      },
    };
  } catch {
    return null;
  }
}

const asCount = (v: unknown): number => (typeof v === 'number' && Number.isFinite(v) ? v : 0);

/** Reads the four Gemini counts from a payload; null when it carries none. */
function readTokenCounts(raw: unknown): TokenCounts | null {
  if (!raw || typeof raw !== 'object') return null;
  const r = raw as Record<string, unknown>;
  if (typeof r['promptTokens'] !== 'number') return null;
  return {
    promptTokens: asCount(r['promptTokens']),
    outputTokens: asCount(r['outputTokens']),
    cachedTokens: asCount(r['cachedTokens']),
    thoughtsTokens: asCount(r['thoughtsTokens']),
  };
}

function readTurnTokens(raw: unknown): TurnTokens | null {
  const counts = readTokenCounts(raw);
  if (!counts) return null;
  const r = raw as Record<string, unknown>;
  return {
    ...counts,
    calls: asCount(r['calls']),
    unreportedCalls: asCount(r['unreportedCalls']),
    totalTokens: asCount(r['totalTokens']),
    cachedPercent: asCount(r['cachedPercent']),
  };
}

/**
 * Build a turn-summary waterfall item from a `backend_completed` event. The
 * backend ships the full per-turn breakdown (Gemini spans + tool calls, and the
 * tokens Gemini reported) in the event's `data` blob. Returns null for any other
 * event.
 */
export function turnSummaryItem(e: TelemetryEventResponse): ConversationItem | null {
  if (e.eventName !== 'backend_completed' || !e.data) return null;
  try {
    const parsed = JSON.parse(e.data) as {
      turnId?: string | null;
      ttftMs?: number | null;
      totalMs?: number | null;
      outcome?: string | null;
      toolCount?: number | null;
      charCount?: number | null;
      tokens?: unknown;
      segments?: Array<{
        type?: string;
        name?: string;
        ms?: number;
        ttftMs?: number | null;
        tokens?: unknown;
      }>;
    };
    const segments: TurnSegment[] = (parsed.segments ?? [])
      .filter((s) => s.type === 'gemini' || s.type === 'tool')
      .map((s) => ({
        type: s.type as 'gemini' | 'tool',
        name: s.name,
        ms: s.ms ?? 0,
        ttftMs: s.ttftMs ?? null,
        tokens: readTokenCounts(s.tokens),
      }));
    return {
      kind: 'turn-summary',
      occurredAt: e.occurredAt,
      data: {
        id: e.id,
        turnId: parsed.turnId ?? e.turnId ?? null,
        ttftMs: parsed.ttftMs ?? e.latencyMs ?? null,
        totalMs: parsed.totalMs ?? e.latencyMs ?? null,
        outcome: parsed.outcome ?? null,
        toolCount: parsed.toolCount ?? null,
        charCount: parsed.charCount ?? null,
        tokens: readTurnTokens(parsed.tokens),
        segments,
      },
    };
  } catch {
    return null;
  }
}

/**
 * Build a turn-metrics waterfall item from a `turn_completed` event. The agent
 * ships the per-stage pipeline latencies (EOU/STT/LLM/TTS) in `data`. Returns
 * null for any other event. The pipeline `llm` stage ≈ the backend
 * `backend_completed` total, so the two waterfalls share a turn id.
 */
export function turnMetricsItem(e: TelemetryEventResponse): ConversationItem | null {
  if (e.eventName !== 'turn_completed' || !e.data) return null;
  try {
    const d = JSON.parse(e.data) as Record<string, unknown>;
    const num = (k: string): number | null =>
      typeof d[k] === 'number' ? (d[k] as number) : null;
    const order: Array<{ key: string; label: string }> = [
      { key: 'eouMs', label: 'eou' },
      { key: 'sttMs', label: 'stt' },
      { key: 'llmMs', label: 'llm' },
      { key: 'ttsMs', label: 'tts' },
    ];
    const stages: TurnPipelineStage[] = order
      .map(({ key, label }) => ({ key: label, label, ms: num(key) ?? -1 }))
      .filter((s) => s.ms >= 0);
    if (stages.length === 0) return null;
    return {
      kind: 'turn-metrics',
      occurredAt: e.occurredAt,
      data: {
        id: e.id,
        turnId: (typeof d['turnId'] === 'string' ? (d['turnId'] as string) : null) ?? e.turnId ?? null,
        stages,
        llmTtftMs: num('llmTtftMs'),
        ttsTtfbMs: num('ttsTtfbMs'),
      },
    };
  } catch {
    return null;
  }
}

/**
 * What each answer cost, by message id. A turn is everything between two visitor messages: its
 * summary (`backend_completed`) carries the tokens the turn spent, and the answer is the last
 * assistant message of the same stretch. Taking the turn as a stretch, not as a pair, keeps this
 * right whichever of the answer and the summary arrives first. An answer without a summary (the
 * greeting, turns recorded before tokens were) is simply absent. Expects the items sorted.
 */
export function tokensByAnswer(items: ConversationItem[]): Map<number, TurnTokens> {
  const byAnswer = new Map<number, TurnTokens>();
  let answerId: number | null = null;
  let spent: TurnTokens | null = null;

  const close = () => {
    if (answerId !== null && spent !== null) byAnswer.set(answerId, spent);
    answerId = null;
    spent = null;
  };

  for (const item of items) {
    if (item.kind === 'message') {
      if (item.data.role === 'User') close();
      else answerId = item.data.id;
    } else if (item.kind === 'turn-summary' && item.data.tokens) {
      const t = item.data.tokens;
      if (spent === null) {
        spent = t;
      } else {
        const promptTokens = spent.promptTokens + t.promptTokens;
        const cachedTokens = spent.cachedTokens + t.cachedTokens;
        spent = {
          promptTokens,
          outputTokens: spent.outputTokens + t.outputTokens,
          cachedTokens,
          thoughtsTokens: spent.thoughtsTokens + t.thoughtsTokens,
          calls: spent.calls + t.calls,
          unreportedCalls: spent.unreportedCalls + t.unreportedCalls,
          totalTokens: spent.totalTokens + t.totalTokens,
          cachedPercent: promptTokens > 0 ? Math.round((100 * cachedTokens) / promptTokens) : 0,
        };
      }
    }
  }
  close();
  return byAnswer;
}

// Returns microseconds since epoch to preserve sub-millisecond ordering.
// JS Date only has ms precision; extra fractional digits are parsed separately.
function toUtcMs(iso: string): number {
  const normalized = /Z|[+-]\d{2}:?\d{2}$/.test(iso) ? iso : `${iso}Z`;
  const ms = new Date(normalized).getTime();
  const frac = normalized.match(/\.(\d+)/);
  if (!frac) return ms * 1000;
  const digits = frac[1].padEnd(6, '0').slice(0, 6);
  const us = parseInt(digits, 10) % 1000;
  return ms * 1000 + us;
}

export function sortItems(items: ConversationItem[]): ConversationItem[] {
  return [...items].sort((a, b) => {
    const ta = toUtcMs(a.occurredAt);
    const tb = toUtcMs(b.occurredAt);
    if (ta !== tb) return ta - tb;
    // Tie-break: messages first, then tool invocations, then events, then the
    // per-turn summaries (they close the turn); same kind by id.
    if (a.kind !== b.kind) {
      const order = {
        message: 0,
        'tool-invocation': 1,
        event: 2,
        'turn-metrics': 3,
        'turn-summary': 4,
      } as const;
      return order[a.kind] - order[b.kind];
    }
    return a.data.id - b.data.id;
  });
}

/**
 * Stable identity key for dedupe + Angular track-by.
 * Backend never reissues message or event ids, so kind+id is sufficient.
 */
export function itemKey(item: ConversationItem): string {
  return `${item.kind}:${item.data.id}`;
}
