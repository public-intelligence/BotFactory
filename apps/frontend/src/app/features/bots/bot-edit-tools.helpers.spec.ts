import { describe, expect, it } from 'vitest';
import {
  mapBotToolResponseToRow,
  mapBotToolRowsToUpdatePayload,
} from './bot-edit-tools.helpers';

describe('bot-edit-tools helpers', () => {
  it('mapBotToolResponseToRow copies API fields', () => {
    const row = mapBotToolResponseToRow({
      toolDefinitionId: 9,
      name: 'clima',
      displayName: 'Clima',
      category: 'weather',
      status: 'Published',
      requiresAuthenticatedUser: false,
      enabled: true,
      additionalInstructions: 'Pide ciudad',
      dataClassification: 'Internal',
      maxCallsPerConversationOverride: null,
    });
    expect(row.toolDefinitionId).toBe(9);
    expect(row.showInstructions).toBe(true);
    expect(row.additionalInstructions).toBe('Pide ciudad');
  });

  it('mapBotToolRowsToUpdatePayload strips empty instructions', () => {
    const payload = mapBotToolRowsToUpdatePayload([
      {
        toolDefinitionId: 1,
        name: 'a',
        displayName: 'A',
        category: null,
        status: 'Published',
        requiresAuthenticatedUser: true,
        enabled: false,
        additionalInstructions: '',
        showInstructions: false,
      },
    ]);
    expect(payload[0].enabled).toBe(false);
    expect(payload[0].additionalInstructions).toBeNull();
  });
});
