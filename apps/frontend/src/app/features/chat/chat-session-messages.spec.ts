import { describe, expect, it } from 'vitest';
import { HttpErrorResponse } from '@angular/common/http';
import {
  canSeeOperatorErrors,
  connectErrorMessage,
  canSeeTelemetryLogs,
  isConversationClosedError,
  mapPipelineErrorToMessage,
  mapSendTurnError,
  mapSessionEndToMessage,
  MSG_CONVERSATION_CLOSED,
  MSG_CONNECT_FAILED,
  MSG_LIMIT_REACHED,
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

  it('isConversationClosedError recognises the hub error and nothing else', () => {
    expect(
      isConversationClosedError(
        new Error("An unexpected error occurred invoking 'SendTurn' on the server. HubException: conversation closed"),
      ),
    ).toBe(true);
    expect(isConversationClosedError(new Error('turn already active'))).toBe(false);
    expect(isConversationClosedError(new Error('Not connected'))).toBe(false);
  });

  describe('connectErrorMessage', () => {
    const http = (status: number, error: unknown) => new HttpErrorResponse({ status, error });

    it('shows the text the server sent, which is the bot\'s own when its owner set one', () => {
      expect(connectErrorMessage(http(429, { error: '  Hay mucha demanda. Llame al *462.  ' }))).toBe(
        'Hay mucha demanda. Llame al *462.',
      );
      expect(connectErrorMessage(http(503, { error: 'El servicio de voz no está disponible.' }))).toBe(
        'El servicio de voz no está disponible.',
      );
    });

    it('a 429 without text gets the plain limit message, in usted', () => {
      expect(connectErrorMessage(http(429, null))).toBe(MSG_LIMIT_REACHED);
      expect(connectErrorMessage(http(429, { error: '   ' }))).toBe(MSG_LIMIT_REACHED);
      expect(connectErrorMessage(http(429, 'Too Many Requests'))).toBe(MSG_LIMIT_REACHED);
      expect(MSG_LIMIT_REACHED).toContain('Intente');
    });

    it('a 503 without text says the service is unavailable', () => {
      expect(connectErrorMessage(http(503, null))).toBe(MSG_SERVICE_UNAVAILABLE);
    });

    it('any other failed call gets the generic text, or the one the caller gives', () => {
      expect(connectErrorMessage(http(500, null))).toBe(MSG_CONNECT_FAILED);
      expect(connectErrorMessage(http(0, null))).toBe(MSG_CONNECT_FAILED);
      expect(connectErrorMessage(http(500, null), 'Error al reanudar.')).toBe('Error al reanudar.');
    });

    it('a plain Error keeps its message; anything else the generic text', () => {
      expect(connectErrorMessage(new Error('No se recibió la sesión de voz.'))).toBe('No se recibió la sesión de voz.');
      expect(connectErrorMessage('boom')).toBe(MSG_CONNECT_FAILED);
      expect(connectErrorMessage(undefined)).toBe(MSG_CONNECT_FAILED);
    });
  });
});
