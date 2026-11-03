import { describe, expect, it } from 'vitest';
import {
  canSeeOperatorErrors,
  canSeeTelemetryLogs,
  mapPipelineErrorToMessage,
  mapSendTurnError,
  mapSessionEndToMessage,
  MSG_CONVERSATION_CLOSED,
  MSG_SERVICE_UNAVAILABLE,
  resolveProviderMessage,
} from './chat-session-messages';

describe('chat-session-messages', () => {
  it('canSeeOperatorErrors is false for bot:chat only', () => {
    expect(canSeeOperatorErrors(['bot:chat'])).toBe(false);
  });

  it('canSeeOperatorErrors is true for staff permissions', () => {
    expect(canSeeOperatorErrors(['bot:chat', 'bot:read'])).toBe(true);
    expect(canSeeOperatorErrors(['telemetry:read'])).toBe(true);
  });

  it('canSeeTelemetryLogs requires telemetry:read or admin', () => {
    expect(canSeeTelemetryLogs(['bot:chat'])).toBe(false);
    expect(canSeeTelemetryLogs(['conversation:read'])).toBe(false);
    expect(canSeeTelemetryLogs(['telemetry:read'])).toBe(true);
    expect(canSeeTelemetryLogs(['bot:admin'])).toBe(true);
  });

  it('resolveProviderMessage prefers operator copy for staff', () => {
    const payload = {
      userMessage: MSG_SERVICE_UNAVAILABLE,
      operatorMessage: 'Gemini: cuota excedida',
    };
    expect(resolveProviderMessage(payload, true)).toBe('Gemini: cuota excedida');
    expect(resolveProviderMessage(payload, false)).toBe(MSG_SERVICE_UNAVAILABLE);
  });

  it('mapSessionEndToMessage uses structured close payload', () => {
    const end = {
      source: 'close' as const,
      reason: 'error',
      operatorMessage: 'ElevenLabs: saldo agotado (TTS)',
      userMessage: MSG_SERVICE_UNAVAILABLE,
    };
    expect(mapSessionEndToMessage(end, true)).toContain('ElevenLabs');
    expect(mapSessionEndToMessage(end, false)).toBe(MSG_SERVICE_UNAVAILABLE);
  });

  it('mapPipelineErrorToMessage falls back when empty', () => {
    expect(mapPipelineErrorToMessage({}, false)).toBe(MSG_SERVICE_UNAVAILABLE);
  });

  it('mapSendTurnError maps hub conversation closed', () => {
    const err = new Error(
      "Failed to invoke 'SendTurn' on the server. HubException: conversation closed",
    );
    expect(mapSendTurnError(err)).toBe(MSG_CONVERSATION_CLOSED);
  });
});
