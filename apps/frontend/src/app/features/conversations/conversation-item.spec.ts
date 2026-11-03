import { describe, expect, it } from 'vitest';
import type { MessageResponse } from '../../api/models/message-response';
import type { TelemetryEventResponse } from '../../api/models/telemetry-event-response';
import {
  eventItem,
  itemKey,
  messageItem,
  sortItems,
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
});
