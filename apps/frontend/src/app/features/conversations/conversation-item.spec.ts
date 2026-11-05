import { describe, expect, it } from 'vitest';
import type { MessageResponse } from '../../api/models/message-response';
import type { TelemetryEventResponse } from '../../api/models/telemetry-event-response';
import {
  type ConversationItem,
  eventItem,
  formatTokenCount,
  itemKey,
  messageItem,
  sortItems,
  tokensByAnswer,
  tokenUsageLabel,
  toolInvocationItem,
  turnMetricsItem,
  turnSummaryItem,
} from './conversation-item';

describe('conversation-item helpers', () => {
  const message = (id: number, createdAt: string): MessageResponse => ({
    id,
    role: 'User',
    content: `m${id}`,
    createdAt,
    provider: null,
    latencyMs: null,
  });

  const event = (
    id: number,
    name: string,
    occurredAt: string,
    data?: string,
  ): TelemetryEventResponse => ({
    id,
    eventName: name,
    source: 'Backend',
    botId: 1,
    conversationId: 1,
    userId: 'u',
    occurredAt,
    stage: null,
    latencyMs: null,
    provider: null,
    errorMessage: null,
    summary: null,
    data: data ?? null,
    turnId: null,
  });

  it('messageItem wraps message payload', () => {
    const item = messageItem(message(1, '2026-01-01T00:00:00Z'));
    expect(item.kind).toBe('message');
    expect(item.data.id).toBe(1);
  });

  it('toolInvocationItem parses display block', () => {
    const data = JSON.stringify({
      display: {
        name: 'consultar',
        items: [{ id: '1', label: 'ok', body: 'result' }],
      },
    });
    const item = toolInvocationItem(event(2, 'tool_call_completed', '2026-01-01T00:00:01Z', data));
    expect(item?.kind).toBe('tool-invocation');
    if (item?.kind === 'tool-invocation') {
      expect(item.data.name).toBe('consultar');
      expect(item.data.items).toHaveLength(1);
    }
  });

  it('turnSummaryItem parses backend_completed segments', () => {
    const data = JSON.stringify({
      turnId: 't1',
      totalMs: 900,
      segments: [{ type: 'gemini', ms: 400 }, { type: 'tool', name: 'kb_search', ms: 100 }],
    });
    const item = turnSummaryItem(event(3, 'backend_completed', '2026-01-01T00:00:02Z', data));
    expect(item?.kind).toBe('turn-summary');
    if (item?.kind === 'turn-summary') {
      expect(item.data.segments).toHaveLength(2);
    }
  });

  it('turnSummaryItem reads the turn tokens and the tokens of each Gemini segment', () => {
    const data = JSON.stringify({
      turnId: 't2',
      totalMs: 2100,
      tokens: {
        calls: 2,
        unreportedCalls: 0,
        promptTokens: 6200,
        outputTokens: 45,
        cachedTokens: 3000,
        thoughtsTokens: 5,
        totalTokens: 6250,
        cachedPercent: 48,
      },
      segments: [
        {
          type: 'gemini',
          ms: 800,
          tokens: { promptTokens: 3000, outputTokens: 15, cachedTokens: 0, thoughtsTokens: 0 },
        },
        { type: 'tool', name: 'kb_search', ms: 100 },
        {
          type: 'gemini',
          ms: 900,
          tokens: { promptTokens: 3200, outputTokens: 30, cachedTokens: 3000, thoughtsTokens: 5 },
        },
      ],
    });
    const item = turnSummaryItem(event(20, 'backend_completed', '2026-01-01T00:00:04Z', data));
    expect(item?.kind).toBe('turn-summary');
    if (item?.kind !== 'turn-summary') return;

    expect(item.data.tokens).toEqual({
      calls: 2,
      unreportedCalls: 0,
      promptTokens: 6200,
      outputTokens: 45,
      cachedTokens: 3000,
      thoughtsTokens: 5,
      totalTokens: 6250,
      cachedPercent: 48,
    });
    expect(item.data.segments[0].tokens?.promptTokens).toBe(3000);
    expect(item.data.segments[1].tokens).toBeNull();
    expect(item.data.segments[2].tokens?.cachedTokens).toBe(3000);
  });

  it('turnSummaryItem tolerates turns recorded before tokens were reported', () => {
    const data = JSON.stringify({ segments: [{ type: 'gemini', ms: 400 }] });
    const item = turnSummaryItem(event(21, 'backend_completed', '2026-01-01T00:00:05Z', data));
    expect(item?.kind).toBe('turn-summary');
    if (item?.kind !== 'turn-summary') return;
    expect(item.data.tokens).toBeNull();
    expect(item.data.segments[0].tokens).toBeNull();
  });

  it('turnSummaryItem treats null tokens (Gemini sent no usageMetadata) as not reported', () => {
    const data = JSON.stringify({
      tokens: { calls: 1, unreportedCalls: 1, promptTokens: 0, outputTokens: 0, cachedTokens: 0, thoughtsTokens: 0, totalTokens: 0, cachedPercent: 0 },
      segments: [{ type: 'gemini', ms: 400, tokens: null }],
    });
    const item = turnSummaryItem(event(22, 'backend_completed', '2026-01-01T00:00:06Z', data));
    if (item?.kind !== 'turn-summary') throw new Error('expected a turn summary');
    expect(item.data.segments[0].tokens).toBeNull();
    expect(item.data.tokens?.unreportedCalls).toBe(1);
  });

  it('turnMetricsItem parses turn_completed pipeline', () => {
    const data = JSON.stringify({ eouMs: 10, sttMs: 20, llmMs: 300, ttsMs: 40, llmTtftMs: 120 });
    const item = turnMetricsItem(event(4, 'turn_completed', '2026-01-01T00:00:03Z', data));
    expect(item?.kind).toBe('turn-metrics');
    if (item?.kind === 'turn-metrics') {
      expect(item.data.stages.map((s) => s.key)).toEqual(['eou', 'stt', 'llm', 'tts']);
    }
  });

  it('sortItems orders by time then kind tie-break', () => {
    const items = sortItems([
      eventItem(event(10, 'x', '2026-01-01T00:00:00.500Z')),
      messageItem(message(5, '2026-01-01T00:00:00.500Z')),
      turnSummaryItem(
        event(
          11,
          'backend_completed',
          '2026-01-01T00:00:00.500Z',
          JSON.stringify({ segments: [] }),
        ),
      )!,
    ]);
    expect(items.map((i) => i.kind)).toEqual(['message', 'event', 'turn-summary']);
  });

  it('itemKey is stable for dedupe', () => {
    expect(itemKey(messageItem(message(7, '2026-01-01T00:00:00Z')))).toBe('message:7');
  });

  describe('tokens of a conversation', () => {
    const turnTokens = (total: number, prompt = total - 10, output = 10, cached = 0) =>
      JSON.stringify({
        turnId: 't',
        tokens: {
          calls: 2,
          unreportedCalls: 0,
          promptTokens: prompt,
          outputTokens: output,
          cachedTokens: cached,
          thoughtsTokens: 0,
          totalTokens: total,
          cachedPercent: 0,
        },
      });
    const msg = (id: number, role: 'User' | 'Assistant', at: string): ConversationItem =>
      messageItem({ id, role, content: `m${id}`, createdAt: at, provider: null, latencyMs: null });
    const summary = (id: number, at: string, data: string): ConversationItem =>
      turnSummaryItem(event(id, 'backend_completed', at, data))!;

    it('formatTokenCount separates thousands the Dominican way, even for four digits', () => {
      expect(formatTokenCount(58632)).toBe('58,632');
      expect(formatTokenCount(1250)).toBe('1,250');
      expect(formatTokenCount(0)).toBe('0');
    });

    it('tokenUsageLabel gives the total first, then input with its cache share and output', () => {
      expect(
        tokenUsageLabel({ totalTokens: 58632, promptTokens: 58510, cachedTokens: 29255, outputTokens: 122, thoughtsTokens: 0 }),
      ).toBe('58,632 tokens · entrada 58,510 (50 % caché) · salida 122');
      expect(
        tokenUsageLabel({ totalTokens: 1015, promptTokens: 1000, cachedTokens: 0, outputTokens: 10, thoughtsTokens: 5 }),
      ).toBe('1,015 tokens · entrada 1,000 (0 % caché) · salida 10 · razonamiento 5');
    });

    it('each answer gets the tokens of its own turn; the greeting has none', () => {
      const items = sortItems([
        msg(1, 'Assistant', '2026-01-01T00:00:00Z'), // greeting
        msg(2, 'User', '2026-01-01T00:00:10Z'),
        msg(3, 'Assistant', '2026-01-01T00:00:13Z'),
        summary(10, '2026-01-01T00:00:13.5Z', turnTokens(58632)),
        msg(4, 'User', '2026-01-01T00:00:30Z'),
        msg(5, 'Assistant', '2026-01-01T00:00:33Z'),
        summary(11, '2026-01-01T00:00:33.5Z', turnTokens(61000)),
      ]);

      const byAnswer = tokensByAnswer(items);

      expect([...byAnswer.keys()]).toEqual([3, 5]);
      expect(byAnswer.get(3)?.totalTokens).toBe(58632);
      expect(byAnswer.get(5)?.totalTokens).toBe(61000);
    });

    it('is the same whether the summary arrives before the answer or after it', () => {
      const answerFirst = sortItems([
        msg(2, 'User', '2026-01-01T00:00:10Z'),
        msg(3, 'Assistant', '2026-01-01T00:00:13Z'),
        summary(10, '2026-01-01T00:00:14Z', turnTokens(500)),
      ]);
      const summaryFirst = sortItems([
        msg(2, 'User', '2026-01-01T00:00:10Z'),
        summary(10, '2026-01-01T00:00:12Z', turnTokens(500)),
        msg(3, 'Assistant', '2026-01-01T00:00:13Z'),
      ]);

      expect(tokensByAnswer(answerFirst).get(3)?.totalTokens).toBe(500);
      expect(tokensByAnswer(summaryFirst).get(3)?.totalTokens).toBe(500);
    });

    it('two summaries in one turn add up on the turn\'s answer', () => {
      const items = sortItems([
        msg(2, 'User', '2026-01-01T00:00:10Z'),
        summary(10, '2026-01-01T00:00:11Z', turnTokens(300, 290, 10, 145)),
        summary(11, '2026-01-01T00:00:12Z', turnTokens(200, 190, 10, 0)),
        msg(3, 'Assistant', '2026-01-01T00:00:13Z'),
      ]);

      const t = tokensByAnswer(items).get(3)!;
      expect(t.totalTokens).toBe(500);
      expect(t.promptTokens).toBe(480);
      expect(t.cachedTokens).toBe(145);
      expect(t.cachedPercent).toBe(30);
      expect(t.calls).toBe(4);
    });

    it('a turn without tokens (recorded before they were) leaves its answer out', () => {
      const items = sortItems([
        msg(2, 'User', '2026-01-01T00:00:10Z'),
        msg(3, 'Assistant', '2026-01-01T00:00:13Z'),
        summary(10, '2026-01-01T00:00:14Z', JSON.stringify({ turnId: 't', totalMs: 900 })),
      ]);

      expect(tokensByAnswer(items).size).toBe(0);
    });

    it('a summary after the last answer with no answer in its turn is not given to an older one', () => {
      const items = sortItems([
        msg(2, 'User', '2026-01-01T00:00:10Z'),
        msg(3, 'Assistant', '2026-01-01T00:00:13Z'),
        summary(10, '2026-01-01T00:00:14Z', turnTokens(500)),
        msg(4, 'User', '2026-01-01T00:00:30Z'),
        summary(11, '2026-01-01T00:00:31Z', turnTokens(900)), // the turn failed: no answer saved
      ]);

      const byAnswer = tokensByAnswer(items);
      expect(byAnswer.size).toBe(1);
      expect(byAnswer.get(3)?.totalTokens).toBe(500);
    });
  });
});
