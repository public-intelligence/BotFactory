/** Date-range helpers for bot chat-history filters. */

import { formatDateInput } from '../../shared/filter-ui';

export const CHAT_HISTORY_STATUS_MAP: Record<string, string> = {
  Active: 'Activa',
  Paused: 'En pausa',
  Closed: 'Cerrada',
};

/**
 * Whether the viewer may see telemetry: the events in a conversation's view and the tokens it
 * spent. The API decides in the end (it sends no tokens or events to anyone else); this only
 * keeps the interface from showing a column or a row that would be empty.
 */
export function canSeeTelemetry(permissions: readonly string[]): boolean {
  return (
    permissions.includes('telemetry:read') ||
    permissions.includes('superadmin') ||
    permissions.includes('bot:admin')
  );
}

export function defaultChatHistoryDateRange(): { from: string; to: string } {
  const to = new Date();
  const from = new Date();
  from.setDate(from.getDate() - 30);
  return { from: formatDateInput(from), to: formatDateInput(to) };
}

export function toUtcStartOfDay(dateStr: string): string {
  const [y, m, d] = dateStr.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d, 0, 0, 0, 0)).toISOString();
}

export function toUtcEndOfDay(dateStr: string): string {
  const [y, m, d] = dateStr.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d, 23, 59, 59, 999)).toISOString();
}

export function mapConversationStatusLabel(status: string): string {
  return CHAT_HISTORY_STATUS_MAP[status] ?? status;
}
