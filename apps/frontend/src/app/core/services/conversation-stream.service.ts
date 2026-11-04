import { computed, inject, Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Api } from '../../api/api';
import { getConversation, getPublicConversation } from '../../api/functions';
import { PublicSessionService } from './public-session.service';
import type { ConversationDetailResponse } from '../../api/models/conversation-detail-response';
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
  type ConversationItem,
} from '../../features/conversations/conversation-item';

/**
 * Frontend item shape published by ConversationStreamPublisher on the hub.
 * Mirrors the C# ConversationItemDto record.
 */
interface ConversationItemPayload {
  kind: 'message' | 'event';
  occurredAt: string;
  message: {
    id: number;
    role: string;
    content: string;
    provider: string | null;
    latencyMs: number | null;
  } | null;
  event: {
    id: number;
    eventName: string;
    stage: string | null;
    latencyMs: number | null;
    provider: string | null;
    errorMessage: string | null;
    summary: string | null;
    payload: string | null;
    turnId: string | null;
  } | null;
}

export type StreamState = 'idle' | 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

@Injectable({ providedIn: 'root' })
export class ConversationStreamService {
  private readonly api = inject(Api);
  private readonly http = inject(HttpClient);

  private readonly itemsByKey = signal(new Map<string, ConversationItem>());
  readonly items = computed(() => sortItems(Array.from(this.itemsByKey().values())));

  readonly state = signal<StreamState>('idle');
  readonly eventsSubscribed = signal<boolean>(false);
  readonly loading = signal<boolean>(false);
  readonly turnInFlight = signal(false);
  readonly sendPending = signal(false);
  readonly activeTurnId = signal<string | null>(null);
  readonly streamingText = signal('');
  readonly turnError = signal<string | null>(null);

  readonly sendBusy = computed(() => this.turnInFlight() || this.sendPending());

  readonly displayItems = computed(() => {
    const base = this.items();
    const draft = this.streamingText();
    if (!draft) return base;
    const streamingItem: ConversationItem = {
      kind: 'message',
      occurredAt: new Date().toISOString(),
      data: {
        id: -1,
        role: 'Assistant',
        content: draft,
        provider: 'streaming',
        latencyMs: null,
        createdAt: new Date().toISOString(),
      },
    };
    return sortItems([...base, streamingItem]);
  });

  private hub: HubConnection | null = null;
  private currentBotId: number | null = null;
  private currentConversationId: number | null = null;
  // Set for anonymous public sessions: history loads via /api/public and the
  // hub authenticates with the in-memory public token instead of hub-token.
  private publicAccessId: string | null = null;

  private readonly publicSession = inject(PublicSessionService);

  /**
   * Loads conversation history and opens the SignalR connection for live updates.
   * Idempotent; calling with the same conversation re-uses the existing connection.
   */
  async connect(
    botId: number,
    conversationId: number,
    options: { withEvents?: boolean; publicAccessId?: string; keepItems?: boolean } = {},
  ): Promise<void> {
    if (this.currentConversationId === conversationId && this.hub) {
      if (options.withEvents) await this.subscribeEvents();
      return;
    }

    await this.disconnect({ keepItems: options.keepItems });
    this.currentBotId = botId;
    this.currentConversationId = conversationId;
    this.publicAccessId = options.publicAccessId ?? null;

    // `keepItems` is for a conversation that was just opened to replace a closed one: what the
    // person already sees stays on screen, and the new conversation holds nothing but its
    // greeting, which would show up as a second «Hola» in the middle of the chat.
    if (!options.keepItems) {
      await this.loadHistory(botId, conversationId, options.withEvents ?? false);
    }
    await this.startHub(conversationId, options.withEvents ?? false);
  }

  async disconnect(options: { keepItems?: boolean } = {}): Promise<void> {
    if (this.hub) {
      try {
        await this.hub.stop();
      } catch {
        /* ignore */
      }
      this.hub = null;
    }
    if (!options.keepItems) this.itemsByKey.set(new Map());
    this.state.set('idle');
    this.eventsSubscribed.set(false);
    this.clearStreamingState();
    this.currentBotId = null;
    this.currentConversationId = null;
    this.publicAccessId = null;
  }

  async sendTurn(text: string): Promise<void> {
    if (!this.hub || this.currentConversationId === null) {
      throw new Error('Not connected');
    }
    if (this.sendBusy()) {
      throw new Error('Turn in flight');
    }

    this.sendPending.set(true);
    try {
      await this.hub.invoke('SendTurn', this.currentConversationId, text);
    } catch (err) {
      this.clearStreamingState();
      throw err;
    } finally {
      this.sendPending.set(false);
      if (this.turnInFlight()) {
        this.clearStreamingState();
      }
    }
  }

  async cancelActiveTurn(): Promise<void> {
    const turnId = this.activeTurnId();
    if (!this.hub || !turnId) return;
    try {
      await this.hub.invoke('CancelTurn', turnId);
    } catch (err) {
      console.warn('CancelTurn failed', err);
    }
  }

  private clearStreamingState(): void {
    this.turnInFlight.set(false);
    this.activeTurnId.set(null);
    this.streamingText.set('');
    this.turnError.set(null);
  }

  async subscribeEvents(): Promise<void> {
    if (!this.hub || this.currentConversationId === null) return;
    if (this.eventsSubscribed()) return;
    try {
      console.log('[hub] join SubscribeEvents', { conversationId: this.currentConversationId });
      await this.hub.invoke('SubscribeEvents', this.currentConversationId);
      this.eventsSubscribed.set(true);
      // Backfill: existing history may not include events.
      if (this.currentBotId !== null) {
        await this.loadHistory(this.currentBotId, this.currentConversationId, true);
      }
    } catch (err) {
      console.warn('SubscribeEvents failed', err);
    }
  }

  async unsubscribeEvents(): Promise<void> {
    if (!this.hub || this.currentConversationId === null) return;
    if (!this.eventsSubscribed()) return;
    try {
      await this.hub.invoke('UnsubscribeEvents', this.currentConversationId);
    } catch (err) {
      console.warn('UnsubscribeEvents failed', err);
    }
    this.eventsSubscribed.set(false);
    // Drop event items from the local store so they disappear from the UI.
    this.itemsByKey.update((map) => {
      const next = new Map(map);
      for (const [key, item] of next) if (item.kind === 'event') next.delete(key);
      return next;
    });
  }

  private async loadHistory(botId: number, conversationId: number, includeEvents: boolean): Promise<void> {
    this.loading.set(true);
    try {
      const detail: ConversationDetailResponse = this.publicAccessId
        ? await this.api.invoke(getPublicConversation, {
            publicId: this.publicAccessId,
            id: conversationId,
          })
        : await this.api.invoke(getConversation, {
            botId,
            id: conversationId,
            includeEvents,
          });
      this.itemsByKey.update((map) => {
        const next = new Map(map);
        for (const m of detail.messages ?? []) this.addMessageToMap(next, m);
        for (const e of detail.events ?? []) this.addEventToMap(next, e);
        return next;
      });
    } finally {
      this.loading.set(false);
    }
  }

  private async startHub(conversationId: number, withEvents: boolean): Promise<void> {
    this.state.set('connecting');

    this.hub = new HubConnectionBuilder()
      .withUrl(`${window.__env?.hubsUrl ?? environment.hubsUrl}/hubs/conversation`, {
        accessTokenFactory: () =>
          this.publicAccessId
            ? Promise.resolve(this.publicSession.token() ?? '')
            : firstValueFrom(this.http.get('/api/auth/hub-token', { responseType: 'text' })),
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    this.hub.on('messageAdded', (payload: ConversationItemPayload) => {
      console.log('[hub] messageAdded', payload);
      if (!payload?.message) return;
      this.itemsByKey.update((map) => {
        const next = new Map(map);
        this.addMessageToMap(next, this.toMessageResponse(payload));
        return next;
      });
      if (payload.message.role === 'Assistant' && this.turnInFlight()) {
        this.clearStreamingState();
      }
    });

    this.hub.on('turnStarted', (payload: { turnId: string }) => {
      this.activeTurnId.set(payload.turnId);
      this.streamingText.set('');
      this.turnInFlight.set(true);
    });

    this.hub.on('turnDelta', (payload: { turnId: string; text: string }) => {
      if (this.activeTurnId() !== payload.turnId) return;
      this.streamingText.update((prev) => prev + payload.text);
    });

    this.hub.on('turnCompleted', () => {
      this.clearStreamingState();
    });

    this.hub.on('turnCancelled', () => {
      this.clearStreamingState();
    });

    this.hub.on('turnFailed', (payload: { userMessage?: string }) => {
      this.turnError.set(payload.userMessage ?? 'No se pudo completar la respuesta.');
      this.clearStreamingState();
    });

    this.hub.on('eventAdded', (payload: ConversationItemPayload) => {
      console.log('[hub] eventAdded', payload);
      if (!payload?.event) return;
      this.itemsByKey.update((map) => {
        const next = new Map(map);
        this.addEventToMap(next, this.toEventResponse(payload, conversationId));
        return next;
      });
    });

    this.hub.onreconnecting(() => this.state.set('reconnecting'));
    this.hub.onreconnected(async () => {
      this.state.set('connected');
      try {
        console.log('[hub] join SubscribeConversation (reconnect)', { conversationId });
        await this.hub!.invoke('SubscribeConversation', conversationId);
        if (this.eventsSubscribed()) {
          console.log('[hub] join SubscribeEvents (reconnect)', { conversationId });
          await this.hub!.invoke('SubscribeEvents', conversationId);
        }
        if (this.currentBotId !== null) {
          await this.loadHistory(this.currentBotId, conversationId, this.eventsSubscribed());
        }
        this.clearStreamingState();
      } catch (err) {
        console.warn('Re-subscribe after reconnect failed', err);
      }
    });
    this.hub.onclose(() => this.state.set('disconnected'));

    try {
      await this.hub.start();
      console.log('[hub] join SubscribeConversation', { conversationId });
      await this.hub.invoke('SubscribeConversation', conversationId);
      this.state.set('connected');
      if (withEvents) {
        await this.subscribeEvents();
      }
    } catch (err) {
      console.warn('Hub connection failed', err);
      this.state.set('disconnected');
    }
  }

  private addMessageToMap(map: Map<string, ConversationItem>, m: MessageResponse): void {
    if (m.role === 'Tool') return; // Tool messages are LLM scaffolding, not feed content.
    const item = messageItem(m);
    map.set(itemKey(item), item);
  }

  private addEventToMap(map: Map<string, ConversationItem>, e: TelemetryEventResponse): void {
    // backend_completed carries the per-turn backend waterfall; render it as a
    // latency-breakdown card. Tool events carry a `display` block; surface
    // those as pills. Anything else renders as a generic admin event row.
    const turn = turnSummaryItem(e);
    if (turn) {
      map.set(itemKey(turn), turn);
      return;
    }
    const metrics = turnMetricsItem(e);
    if (metrics) {
      map.set(itemKey(metrics), metrics);
      return;
    }
    const tool = toolInvocationItem(e);
    if (tool) {
      map.set(itemKey(tool), tool);
      return;
    }
    const item = eventItem(e);
    map.set(itemKey(item), item);
  }

  private toMessageResponse(p: ConversationItemPayload): MessageResponse {
    const m = p.message!;
    return {
      id: m.id,
      role: m.role,
      content: m.content,
      provider: m.provider,
      latencyMs: m.latencyMs,
      createdAt: p.occurredAt,
    };
  }

  private toEventResponse(p: ConversationItemPayload, conversationId: number): TelemetryEventResponse {
    const e = p.event!;
    return {
      id: e.id,
      eventName: e.eventName,
      source: 'Agent', // best-effort; source isn't relevant for rendering.
      botId: null,
      conversationId,
      userId: null,
      occurredAt: p.occurredAt,
      stage: e.stage,
      latencyMs: e.latencyMs,
      provider: e.provider,
      errorMessage: e.errorMessage,
      summary: e.summary,
      data: e.payload,
      turnId: e.turnId,
    };
  }
}
