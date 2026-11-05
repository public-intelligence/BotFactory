import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { Api } from '../../api/api';
import { getConversation } from '../../api/functions';
import type { ConversationDetailResponse } from '../../api/models/conversation-detail-response';
import { AuthService } from '../../core/services/auth.service';
import { ConversationStreamService } from '../../core/services/conversation-stream.service';
import { ConversationFeedComponent } from '../conversations/conversation-feed';
import { tokenUsageLabel } from '../conversations/conversation-item';
import { canSeeTelemetry } from './bot-edit-chat-history.helpers';

const STATUS_MAP: Record<string, string> = {
  Active: 'Activa',
  Closed: 'Cerrada',
};

@Component({
  selector: 'bf-conversation-detail-drawer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ConversationFeedComponent],
  providers: [ConversationStreamService],
  template: `
    @if (open()) {
      <!-- Backdrop -->
      <div
        class="fixed inset-0 z-40 bg-black/40 backdrop-blur-sm transition-opacity"
        (click)="closed.emit()"
      ></div>

      <!-- Panel -->
      <aside
        class="fixed inset-y-0 right-0 z-50 flex w-full max-w-lg flex-col bg-white shadow-2xl"
      >
        <!-- Header -->
        <header
          class="flex items-center justify-between border-b border-neutral-200 px-5 py-4"
        >
          <div>
            <h2 class="text-sm font-semibold text-neutral-900">
              Conversación #{{ conversationId() }}
            </h2>
            @if (conversation(); as c) {
              <p class="mt-0.5 text-xs text-neutral-500">
                {{ statusLabel(c.status) }}
                · {{ formatDate(c.createdAt) }}
                @if (c.closedAt) {
                  · Duración: {{ duration(c.createdAt, c.closedAt) }}
                }
              </p>
              @if (c.tokens; as tk) {
                <p class="mt-0.5 text-xs text-neutral-500" data-testid="conversation-tokens">
                  Tokens: {{ tokensLabel(tk) }}
                </p>
              }
            }
          </div>
          <button
            type="button"
            class="flex h-8 w-8 items-center justify-center rounded-full text-neutral-400 transition-colors hover:bg-neutral-100 hover:text-neutral-700"
            aria-label="Cerrar panel"
            (click)="closed.emit()"
          >
            <svg
              xmlns="http://www.w3.org/2000/svg"
              viewBox="0 0 24 24"
              fill="currentColor"
              class="h-4 w-4"
            >
              <path
                d="M6.2 4.8a1 1 0 0 1 1.4 0L12 9.2l4.4-4.4a1 1 0 1 1 1.4 1.4L13.4 10.6l4.4 4.4a1 1 0 1 1-1.4 1.4L12 12l-4.4 4.4a1 1 0 1 1-1.4-1.4l4.4-4.4-4.4-4.4a1 1 0 0 1 0-1.4Z"
              />
            </svg>
          </button>
        </header>

        @if (conversation()?.closedReason; as reason) {
          <div
            class="mx-5 mt-3 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-700"
          >
            Motivo de cierre: {{ reason }}
          </div>
        }

        <bf-conversation-feed
          [items]="stream.items()"
          [loading]="stream.loading()"
          [showEvents]="canSeeEvents()"
        ></bf-conversation-feed>
      </aside>
    }
  `,
})
export class ConversationDetailDrawer {
  protected readonly stream = inject(ConversationStreamService);
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);

  readonly open = input<boolean>(false);
  readonly botId = input.required<number>();
  readonly conversationId = input.required<number | null>();

  readonly closed = output<void>();

  // Header metadata loaded alongside the stream. We keep it separate from
  // ConversationStreamService.items so the drawer header can render the
  // conversation row's status / closedReason without coupling those fields
  // into every stream item.
  protected readonly conversation = signal<ConversationDetailResponse | null>(null);

  protected readonly canSeeEvents = computed(() => canSeeTelemetry(this.auth.user()?.permissions ?? []));

  protected readonly tokensLabel = tokenUsageLabel;

  constructor() {
    effect(async () => {
      const isOpen = this.open();
      const convId = this.conversationId();
      const botId = this.botId();
      if (!isOpen || convId == null) {
        await this.stream.disconnect();
        this.conversation.set(null);
        return;
      }

      // Load header metadata in parallel with the stream connection.
      const includeEvents = this.canSeeEvents();
      const [detail] = await Promise.all([
        this.api.invoke(getConversation, { botId, id: convId, includeEvents }),
        this.stream.connect(botId, convId, { withEvents: includeEvents }),
      ]);
      this.conversation.set(detail);
    });
  }

  statusLabel(status: string): string {
    return STATUS_MAP[status] ?? status;
  }

  formatDate(iso: string): string {
    return new Date(iso).toLocaleDateString('es', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  duration(start: string, end: string): string {
    const ms = new Date(end).getTime() - new Date(start).getTime();
    const mins = Math.floor(ms / 60000);
    const secs = Math.floor((ms % 60000) / 1000);
    if (mins > 0) return `${mins}m ${secs}s`;
    return `${secs}s`;
  }
}
