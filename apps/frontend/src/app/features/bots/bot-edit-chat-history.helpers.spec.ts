import { describe, expect, it } from 'vitest';
import {
  defaultChatHistoryDateRange,
  mapConversationStatusLabel,
  toUtcEndOfDay,
  toUtcStartOfDay,
} from './bot-edit-chat-history.helpers';

describe('bot-edit-chat-history helpers', () => {
  it('defaultChatHistoryDateRange spans 30 days ending today', () => {
    const { from, to } = defaultChatHistoryDateRange();
    expect(to).toHaveLength(10);
    expect(from).toHaveLength(10);
    expect(from <= to).toBe(true);
  });

  it('toUtcStartOfDay and toUtcEndOfDay bracket the local calendar day', () => {
    const start = toUtcStartOfDay('2026-08-06');
    const end = toUtcEndOfDay('2026-08-06');
    expect(start).toBe('2026-08-06T00:00:00.000Z');
    expect(end).toBe('2026-08-06T23:59:59.999Z');
  });

  it('mapConversationStatusLabel translates known statuses', () => {
    expect(mapConversationStatusLabel('Paused')).toBe('En pausa');
    expect(mapConversationStatusLabel('Unknown')).toBe('Unknown');
  });
});
