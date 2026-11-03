import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  input,
  output,
  viewChild,
} from '@angular/core';
import { ConversationFeedComponent } from '../../conversations/conversation-feed';
import type { ConversationItem } from '../../conversations/conversation-item';

@Component({
  selector: 'bf-chat-panel',
  standalone: true,
  imports: [ConversationFeedComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <aside
      class="pointer-events-auto fixed inset-y-0 right-0 z-40 flex w-full max-w-[600px] flex-col border-l border-white/10 bg-neutral-900/95 shadow-2xl backdrop-blur-xl transition-transform duration-300 ease-out will-change-transform"
      [class.translate-x-0]="open()"
      [class.translate-x-full]="!open()"
      aria-label="Panel de chat"
    >
      <header class="flex items-center justify-between border-b border-white/10 px-5 py-4">
        <div>
          <h2 class="text-sm font-semibold text-white">Conversación</h2>
          <p class="text-xs text-neutral-400">
            @if (showEvents()) {
              Mensajes y eventos en vivo
            } @else {
              Conversación en tiempo real
            }
          </p>
        </div>
        <div class="flex items-center gap-1.5">
          @if (isAdmin()) {
            <button
              type="button"
              class="flex h-8 w-8 items-center justify-center rounded-full text-neutral-400 transition-colors hover:bg-white/5 hover:text-white"
              title="Parámetros activos"
              aria-label="Parámetros activos"
              (click)="openParams.emit()"
            >
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="h-4 w-4">
                <rect x="3" y="8" width="18" height="12" rx="2" />
                <path d="M12 2v4" />
                <circle cx="8.5" cy="14" r="1" />
                <circle cx="15.5" cy="14" r="1" />
                <path d="M9 18h6" />
              </svg>
            </button>
            <button
              type="button"
              class="flex h-8 w-8 items-center justify-center rounded-full text-neutral-400 transition-colors hover:bg-white/5 hover:text-white"
              [class.bg-white]="showEvents()"
              [class.!text-neutral-900]="showEvents()"
              [title]="showEvents() ? 'Desactivar modo admin' : 'Activar modo admin'"
              [attr.aria-label]="showEvents() ? 'Desactivar modo admin' : 'Activar modo admin'"
              [attr.aria-pressed]="showEvents()"
              (click)="toggleAdmin.emit()"
            >
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="h-4 w-4">
                <polyline points="16 18 22 12 16 6" />
                <polyline points="8 6 2 12 8 18" />
              </svg>
            </button>
          }
          <button
            type="button"
            class="flex h-8 w-8 items-center justify-center rounded-full text-neutral-400 transition-colors hover:bg-white/5 hover:text-white"
            aria-label="Cerrar panel"
            (click)="closed.emit()"
          >
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="h-4 w-4">
              <path d="M6.2 4.8a1 1 0 0 1 1.4 0L12 9.2l4.4-4.4a1 1 0 1 1 1.4 1.4L13.4 10.6l4.4 4.4a1 1 0 1 1-1.4 1.4L12 12l-4.4 4.4a1 1 0 1 1-1.4-1.4l4.4-4.4-4.4-4.4a1 1 0 0 1 0-1.4Z" />
            </svg>
          </button>
        </div>
      </header>

      <div
        #panelScrollEl
        class="bf-feed-scroll min-h-0 flex-1 overflow-y-scroll overflow-x-hidden"
      >
        <div class="px-5 py-4">
          <bf-conversation-feed
            [items]="items()"
            [loading]="loading()"
            [showEvents]="showEvents()"
            [dark]="true"
            [branded]="true"
            [scrollable]="false"
            [externalScrollRoot]="scrollContainerRef()?.nativeElement ?? null"
            ariaLabel="Conversación"
          />
        </div>
      </div>
    </aside>
  `,
})
export class ChatPanel {
  readonly scrollContainerRef =
    viewChild<ElementRef<HTMLDivElement>>('panelScrollEl');

  readonly open = input<boolean>(false);
  readonly items = input<ConversationItem[]>([]);
  readonly loading = input<boolean>(false);
  readonly showEvents = input<boolean>(false);
  readonly isAdmin = input<boolean>(false);

  readonly closed = output<void>();
  readonly toggleAdmin = output<void>();
  readonly openParams = output<void>();
}
