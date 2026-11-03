/** Date-range helpers for bot chat-history filters. */

import { formatDateInput } from '../../shared/filter-ui';

export const CHAT_HISTORY_STATUS_MAP: Record<string, string> = {
  Active: 'Activa',
  Paused: 'En pausa',
  Closed: 'Cerrada',
};

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
