import { Injectable, inject } from '@angular/core';
import { Api } from '../../api/api';
import { closeConversation, closePublicConversation } from '../../api/functions';

export type ClientCloseReason = 'user_ended' | 'user_aborted';

@Injectable({ providedIn: 'root' })
export class ConversationLifecycleService {
  private readonly api = inject(Api);

  async close(
    botId: number,
    conversationId: number,
    reason: ClientCloseReason,
  ): Promise<void> {
    await this.api.invoke(closeConversation, {
      botId,
      id: conversationId,
      body: { reason },
    });
  }

  /** Anonymous public sessions close through /api/public with the bearer token. */
  async closePublic(
    publicId: string,
    conversationId: number,
    reason: ClientCloseReason,
  ): Promise<void> {
    await this.api.invoke(closePublicConversation, {
      publicId,
      id: conversationId,
      body: { reason },
    });
  }

  /** Best-effort close on tab close / navigation (session cookie + keepalive). */
  closeOnUnload(botId: number, conversationId: number, reason: ClientCloseReason): void {
    const url = `${this.api.rootUrl}/api/bots/${botId}/conversations/${conversationId}/close`;
    this.fireKeepalive(url, reason, { credentials: 'include' });
  }

  /** Public variant: bearer token instead of the session cookie. */
  closePublicOnUnload(
    publicId: string,
    conversationId: number,
    reason: ClientCloseReason,
    token: string,
  ): void {
    const url = `${this.api.rootUrl}/api/public/bots/${publicId}/conversations/${conversationId}/close`;
    this.fireKeepalive(url, reason, {
      headers: { Authorization: `Bearer ${token}` },
    });
  }

  private fireKeepalive(
    url: string,
    reason: ClientCloseReason,
    init: { credentials?: RequestCredentials; headers?: Record<string, string> },
  ): void {
    try {
      void fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...(init.headers ?? {}) },
        body: JSON.stringify({ reason }),
        credentials: init.credentials,
        keepalive: true,
      });
    } catch {
      // Unload paths cannot surface errors to the user.
    }
  }
}
