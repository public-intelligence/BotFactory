import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { ConversationFeedComponent } from '../../conversations/conversation-feed';
import type { ConversationItem } from '../../conversations/conversation-item';
import type { AudioFallbackReason } from '../chat-session-messages';

@Component({
  selector: 'bf-text-session-view',
  standalone: true,
  imports: [ConversationFeedComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- pt-20 clears the absolute chat header (logo + name) so the first
         bubble is not covered on mobile / narrow viewports. -->
    <div class="flex h-full min-h-0 w-full flex-col pt-20">
      <div
        #feedScrollEl
        class="bf-feed-scroll min-h-0 w-full flex-1 overflow-y-scroll overflow-x-hidden"
      >
        <div class="mx-auto w-full max-w-3xl px-4 py-4">
          <bf-conversation-feed
            [items]="items()"
            [loading]="loading()"
            [showEvents]="showEvents()"
            [dark]="true"
            [branded]="true"
            [scrollable]="false"
            [externalScrollRoot]="scrollContainerRef()?.nativeElement ?? null"
            ariaLabel="Conversación por chat"
          />
          @if (thinking()) {
            <div class="mt-3 flex justify-start" aria-live="polite" aria-label="El asistente está escribiendo">
              <div
                class="flex items-center gap-1.5 rounded-lg px-3 py-2.5"
                style="background: var(--bf-bot-bubble); color: var(--bf-fg)"
                role="status"
              >
                <span class="h-2 w-2 animate-[bf-dot_1.4s_ease-in-out_infinite] rounded-full opacity-60" style="background: var(--bf-fg)"></span>
                <span class="h-2 w-2 animate-[bf-dot_1.4s_ease-in-out_infinite_0.2s] rounded-full opacity-60" style="background: var(--bf-fg)"></span>
                <span class="h-2 w-2 animate-[bf-dot_1.4s_ease-in-out_infinite_0.4s] rounded-full opacity-60" style="background: var(--bf-fg)"></span>
              </div>
            </div>
          }
        </div>
      </div>

      <footer class="mx-auto w-full max-w-3xl shrink-0 px-4 pb-6 pt-2">
        @if (statusMessage(); as msg) {
          <p
            class="mb-2 rounded-lg border px-4 py-2.5 text-sm font-medium"
            [class.border-amber-600]="statusTone() === 'warning'"
            [class.bg-amber-50]="statusTone() === 'warning'"
            [class.text-amber-950]="statusTone() === 'warning'"
            [class.border-red-600]="statusTone() === 'error'"
            [class.bg-red-50]="statusTone() === 'error'"
            [class.text-red-950]="statusTone() === 'error'"
          >
            {{ msg }}
          </p>
        }

        <div class="flex flex-col rounded-[1.75rem] px-4 py-3" style="background: var(--bf-surface)">
          <textarea
            #messageInput
            rows="1"
            class="w-full min-h-6 max-h-40 resize-none overflow-y-hidden border-0 bg-transparent text-[0.9375rem] leading-6 shadow-none outline-none ring-0 focus:border-0 focus:outline-none focus:ring-0"
            style="color: var(--bf-fg)"
            placeholder="Escribe un mensaje…"
            (input)="onInput($event)"
            (keydown.enter)="onEnter($event)"
          ></textarea>

          <div class="mt-2 flex items-center gap-1.5">
            @if (isAdmin()) {
              <div class="flex shrink-0 items-center gap-1">
                <button
                  type="button"
                  class="flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-neutral-400 transition-colors hover:bg-neutral-700/80 hover:text-neutral-100"
                  title="Parámetros activos"
                  aria-label="Parámetros activos"
                  (click)="openParams.emit()"
                >
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="h-[1.125rem] w-[1.125rem]" aria-hidden="true">
                    <rect x="3" y="8" width="18" height="12" rx="2" />
                    <path d="M12 2v4" />
                    <circle cx="8.5" cy="14" r="1" />
                    <circle cx="15.5" cy="14" r="1" />
                    <path d="M9 18h6" />
                  </svg>
                </button>
                <button
                  type="button"
                  class="flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-neutral-400 transition-colors hover:bg-neutral-700/80 hover:text-neutral-100"
                  [class.bg-white]="showEvents()"
                  [class.text-neutral-900]="showEvents()"
                  [class.hover:bg-white]="showEvents()"
                  [title]="showEvents() ? 'Desactivar modo admin' : 'Activar modo admin'"
                  [attr.aria-label]="showEvents() ? 'Desactivar modo admin' : 'Activar modo admin'"
                  [attr.aria-pressed]="showEvents()"
                  (click)="toggleAdmin.emit()"
                >
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="h-[1.125rem] w-[1.125rem]" aria-hidden="true">
                    <polyline points="16 18 22 12 16 6" />
                    <polyline points="8 6 2 12 8 18" />
                  </svg>
                </button>
              </div>
            }

            <span class="min-w-0 flex-1"></span>

            @if (showVoiceSwitch()) {
              <button
                type="button"
                class="flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-neutral-400 transition-colors hover:bg-neutral-700/80 hover:text-neutral-100"
                title="Usar voz"
                aria-label="Usar voz"
                (click)="useVoice.emit()"
              >
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="h-[1.125rem] w-[1.125rem]" aria-hidden="true">
                  <rect x="3" y="10" width="2" height="4" rx="1" />
                  <rect x="7" y="7" width="2" height="10" rx="1" />
                  <rect x="11" y="4" width="2" height="16" rx="1" />
                  <rect x="15" y="7" width="2" height="10" rx="1" />
                  <rect x="19" y="9" width="2" height="6" rx="1" />
                </svg>
              </button>
            }

            @if (sending()) {
              <button
                type="button"
                class="flex h-9 w-9 shrink-0 items-center justify-center rounded-full transition-colors"
                style="background: var(--bf-accent); color: var(--bf-cta-fg)"
                title="Detener"
                aria-label="Detener respuesta"
                (click)="stop.emit()"
              >
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="h-3.5 w-3.5" aria-hidden="true">
                  <rect x="6" y="6" width="12" height="12" rx="1.5" />
                </svg>
              </button>
            } @else {
              <button
                type="button"
                class="flex h-9 w-9 shrink-0 items-center justify-center rounded-full transition-colors disabled:opacity-40"
                [style.background]="canSend() ? 'var(--bf-accent)' : 'var(--bf-surface)'"
                [style.color]="canSend() ? 'var(--bf-cta-fg)' : 'var(--bf-muted)'"
                [disabled]="!canSend()"
                title="Enviar"
                aria-label="Enviar mensaje"
                (click)="submit()"
              >
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" class="h-4 w-4" aria-hidden="true">
                  <path d="M12 19V5" />
                  <path d="m5 12 7-7 7 7" />
                </svg>
              </button>
            }
          </div>
        </div>
      </footer>
    </div>
  `,
  styles: [
    `
      @keyframes bf-dot {
        0%, 80%, 100% { opacity: 0.3; transform: scale(0.8); }
        40% { opacity: 1; transform: scale(1); }
      }
    `,
  ],
})
export class TextSessionView {
  private static readonly TEXTAREA_MAX_HEIGHT_PX = 160;
  private static readonly SCROLL_NEAR_BOTTOM_PX = 120;

  private readonly messageInput =
    viewChild<ElementRef<HTMLTextAreaElement>>('messageInput');
  readonly scrollContainerRef =
    viewChild<ElementRef<HTMLDivElement>>('feedScrollEl');

  readonly items = input<ConversationItem[]>([]);
  readonly loading = input(false);
  readonly sending = input(false);
  /** True while waiting for the assistant before streamed text appears. */
  readonly thinking = input(false);
  readonly showEvents = input(false);
  readonly isAdmin = input(false);
  readonly showVoiceSwitch = input(false);
  readonly fallbackReason = input<AudioFallbackReason | null>(null);
  readonly turnError = input<string | null>(null);
  readonly sessionError = input<string | null>(null);

  readonly statusMessage = computed(() => {
    const reason = this.fallbackReason();
    if (reason) return this.fallbackMessage(reason);
    return this.turnError() ?? this.sessionError();
  });

  readonly statusTone = computed<'warning' | 'error' | null>(() => {
    if (this.fallbackReason()) return 'warning';
    if (this.turnError() || this.sessionError()) return 'error';
    return null;
  });

  readonly send = output<string>();
  readonly stop = output<void>();
  readonly useVoice = output<void>();
  readonly openParams = output<void>();
  readonly toggleAdmin = output<void>();

  readonly draft = signal('');

  /** Tracks message ids and streaming length so we scroll when content grows. */
  private readonly messageScrollSignature = computed(() =>
    this.items()
      .filter((i) => i.kind === 'message')
      .map((i) => `${i.data.id}:${i.data.content.length}`)
      .join('|'),
  );

  constructor() {
    effect(() => {
      const signature = this.messageScrollSignature();
      const thinking = this.thinking();
      this.scrollContainerRef();
      if (!signature && !thinking) return;

      const messages = this.items().filter((i) => i.kind === 'message');
      const last = messages[messages.length - 1];
      const force =
        thinking || (last?.kind === 'message' && last.data.role === 'User');
      this.scrollToBottom(force);
    });
  }

  canSend(): boolean {
    return this.draft().trim().length > 0 && !this.sending();
  }

  onInput(event: Event): void {
    const el = event.target as HTMLTextAreaElement;
    this.draft.set(el.value);
    this.resizeTextarea(el);
  }

  onEnter(event: Event): void {
    const ke = event as KeyboardEvent;
    if (ke.shiftKey) return;
    ke.preventDefault();
    this.submit();
  }

  submit(): void {
    const text = this.draft().trim();
    if (!text || this.sending()) return;
    this.send.emit(text);
    this.scrollToBottom(true);
    this.focusInput();
  }

  /** Clears the composer after the server accepts the message. */
  clearDraft(): void {
    this.draft.set('');
    const el = this.messageInput()?.nativeElement;
    if (!el) return;
    el.value = '';
    this.resizeTextarea(el);
    this.focusInput();
  }

  focusInput(): void {
    queueMicrotask(() => this.messageInput()?.nativeElement?.focus());
  }

  private scrollToBottom(force = false): void {
    const root = this.scrollContainerRef()?.nativeElement;
    if (!root) return;
    requestAnimationFrame(() => {
      const gap = root.scrollHeight - root.scrollTop - root.clientHeight;
      const nearBottom = gap < TextSessionView.SCROLL_NEAR_BOTTOM_PX;
      if (force || nearBottom) {
        root.scrollTop = root.scrollHeight;
      }
    });
  }

  private resizeTextarea(el: HTMLTextAreaElement): void {
    const max = TextSessionView.TEXTAREA_MAX_HEIGHT_PX;
    el.style.height = '0';
    const next = Math.min(el.scrollHeight, max);
    el.style.height = `${next}px`;
    el.style.overflowY = el.scrollHeight > max ? 'auto' : 'hidden';
  }

  fallbackMessage(reason: AudioFallbackReason): string {
    switch (reason) {
      case 'connect_failed':
        return 'No pudimos conectar el audio. Puedes continuar por chat.';
      case 'agent_timeout':
        return 'El asistente de voz no respondió a tiempo. Continúa por chat.';
      case 'mic_denied':
        return 'El micrófono no está disponible. Continúa por chat.';
      case 'pipeline_error':
        return 'Hubo un problema con el audio. Puedes seguir escribiendo aquí.';
      default:
        return 'Continúa la conversación por chat.';
    }
  }
}
