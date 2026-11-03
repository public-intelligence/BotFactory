import type { BotToolResponse } from '../../api/models/bot-tool-response';

export interface BotToolRow {
  toolDefinitionId: number;
  name: string;
  displayName: string;
  category: string | null;
  status: string;
  requiresAuthenticatedUser: boolean;
  enabled: boolean;
  additionalInstructions: string;
  showInstructions: boolean;
}

export function mapBotToolResponseToRow(t: BotToolResponse): BotToolRow {
  return {
    toolDefinitionId: t.toolDefinitionId,
    name: t.name,
    displayName: t.displayName,
    category: t.category ?? null,
    status: t.status,
    requiresAuthenticatedUser: t.requiresAuthenticatedUser,
    enabled: t.enabled,
    additionalInstructions: t.additionalInstructions ?? '',
    showInstructions: !!t.additionalInstructions,
  };
}

export function mapBotToolRowsToUpdatePayload(rows: BotToolRow[]) {
  return rows.map((row) => ({
    toolDefinitionId: row.toolDefinitionId,
    enabled: row.enabled,
    additionalInstructions: row.additionalInstructions || null,
    maxCallsPerConversationOverride: null,
  }));
}
