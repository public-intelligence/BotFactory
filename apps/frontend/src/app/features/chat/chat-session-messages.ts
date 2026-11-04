import type { SessionEndInfo, SessionPausedInfo } from '../../core/services/livekit.service';

export const MSG_CONNECTION_LOST =
  'Se perdió la conexión. Puedes iniciar una nueva conversación.';
export const MSG_INACTIVITY_PAUSE =
  'La conversación está en pausa por inactividad. Puedes continuar donde la dejaste o iniciar una nueva.';
export const MSG_INACTIVITY_END =
  'La conversación terminó por inactividad. Puedes iniciar una nueva conversación.';
const MSG_SPEECH_SERVICE =
  'No pudimos procesar el audio en este momento. Intenta iniciar una nueva conversación.';
const MSG_VOICE_SERVICE =
  'No pudimos reproducir la respuesta en voz. Intenta iniciar una nueva conversación.';
const MSG_GENERIC_END =
  'La conversación terminó. Puedes iniciar una nueva conversación.';
export const MSG_SERVICE_UNAVAILABLE =
  'El servicio no está disponible en este momento. Por favor, intenta más tarde.';
export const MSG_CONVERSATION_CLOSED =
  'Esta conversación ya fue cerrada. Inicia una nueva conversación para seguir hablando.';

/**
 * The hub refuses a turn on a conversation the server already closed (it was idle
 * for too long). The chat answers by opening a fresh one instead of asking the person to.
 */
export function isConversationClosedError(err: unknown): boolean {
  const raw = err instanceof Error ? err.message : String(err);
  return raw.toLowerCase().includes('conversation closed');
}

export function mapSendTurnError(err: unknown): string {
  const raw = err instanceof Error ? err.message : String(err);
  const lower = raw.toLowerCase();
  if (lower.includes('conversation closed')) return MSG_CONVERSATION_CLOSED;
  if (lower.includes('empty message')) return 'Escribe un mensaje para enviar.';
  if (lower.includes('chat disabled')) return 'Este bot no tiene chat habilitado.';
  if (lower.includes('not connected')) {
    return 'No hay conexión con el chat. Intenta de nuevo.';
  }
  return 'No se pudo enviar el mensaje. Intenta de nuevo.';
}

export type AudioFallbackReason =
  | 'connect_failed'
  | 'agent_timeout'
  | 'mic_denied'
  | 'pipeline_error';

/** Structured provider failure from agent Error/Close data channel payloads. */
export interface ProviderErrorPayload {
  errorCode?: string;
  provider?: string;
  stage?: string;
  userMessage?: string;
  operatorMessage?: string;
  error?: string;
  reason?: string;
  source?: string;
}

/** End users have only bot:chat; staff roles see provider-specific messages. */
export function canSeeOperatorErrors(permissions: string[]): boolean {
  return (
    permissions.length > 0 &&
    !(permissions.length === 1 && permissions[0] === 'bot:chat')
  );
}

/** Live telemetry / event log in the voice session panel. */
export function canSeeTelemetryLogs(permissions: string[]): boolean {
  return (
    permissions.includes('telemetry:read') ||
    permissions.includes('superadmin') ||
    permissions.includes('bot:admin')
  );
}

export function resolveProviderMessage(
  payload: ProviderErrorPayload | null | undefined,
  canSeeOperator: boolean,
): string | null {
  if (!payload) return null;
  if (canSeeOperator && payload.operatorMessage?.trim()) {
    return payload.operatorMessage.trim();
  }
  if (payload.userMessage?.trim()) {
    return payload.userMessage.trim();
  }
  return null;
}

function classifyError(error: string): string | null {
  if (!error) return null;

  if (
    error.includes('stt_error') ||
    error.includes('stt') ||
    error.includes('recognize speech') ||
    error.includes('transcri')
  ) {
    return MSG_SPEECH_SERVICE;
  }

  if (error.includes('tts_error') || error.includes('tts')) {
    return MSG_VOICE_SERVICE;
  }

  if (
    error.includes('connection closed') ||
    error.includes('connection lost') ||
    error.includes('disconnect')
  ) {
    return MSG_CONNECTION_LOST;
  }

  if (
    error.includes('invalid_request') ||
    error.includes('invalid language') ||
    error.includes('api') ||
    error.includes('status_code') ||
    error.includes('quota') ||
    error.includes('credit') ||
    error.includes('billing') ||
    error.includes('insufficient')
  ) {
    return MSG_SERVICE_UNAVAILABLE;
  }

  return null;
}

export function mapSessionPausedToMessage(_info: SessionPausedInfo): string {
  return MSG_INACTIVITY_PAUSE;
}

/** Agent Close after idle/no-speech — pause in UI, not terminal ended. */
export function isIdleAgentClose(end: SessionEndInfo): boolean {
  if (end.source !== 'close') return false;
  if (end.error?.trim()) return false;
  if (end.errorCode?.trim()) return false;

  const reason = (end.reason ?? '').toLowerCase();
  return (
    reason.includes('timeout') ||
    reason.includes('away') ||
    reason.includes('shutdown') ||
    reason.includes('no_speech') ||
    reason.includes('inactivity')
  );
}

export function mapSessionEndToMessage(
  end: SessionEndInfo,
  canSeeOperator = false,
): string {
  const structured = resolveProviderMessage(end, canSeeOperator);
  if (structured) return structured;

  if (end.source === 'disconnect') {
    return MSG_CONNECTION_LOST;
  }

  const reason = (end.reason ?? '').toLowerCase();
  const error = (end.error ?? '').toLowerCase();

  if (
    reason.includes('timeout') ||
    reason.includes('away') ||
    reason.includes('shutdown') ||
    reason.includes('no_speech') ||
    reason.includes('inactivity')
  ) {
    return MSG_INACTIVITY_END;
  }

  const fromError = classifyError(error);
  if (fromError) return fromError;

  if (reason.includes('error') || reason.includes('failed')) {
    return MSG_GENERIC_END;
  }

  return MSG_GENERIC_END;
}

export function mapPipelineErrorToMessage(
  payload: ProviderErrorPayload,
  canSeeOperator: boolean,
): string {
  return (
    resolveProviderMessage(payload, canSeeOperator) ?? MSG_SERVICE_UNAVAILABLE
  );
}
