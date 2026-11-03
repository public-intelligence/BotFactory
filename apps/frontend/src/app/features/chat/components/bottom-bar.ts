import {
  ChangeDetectionStrategy,
  Component,
  input,
  output,
} from '@angular/core';

@Component({
  selector: 'bf-bottom-bar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="pointer-events-auto flex items-center gap-2 rounded-full border border-white/10 bg-neutral-900/70 px-3 py-2 shadow-2xl backdrop-blur-xl"
    >
      <!-- Mute / unmute -->
      <button
        type="button"
        class="flex h-12 w-12 items-center justify-center rounded-full transition-colors"
        [style.background]="micEnabled() ? 'var(--bf-accent)' : 'var(--bf-surface)'"
        [style.color]="micEnabled() ? 'var(--bf-cta-fg)' : 'var(--bf-fg)'"
        [title]="micEnabled() ? 'Silenciar micrófono' : 'Activar micrófono'"
        [attr.aria-label]="micEnabled() ? 'Silenciar micrófono' : 'Activar micrófono'"
        (click)="toggleMic.emit()"
      >
        @if (micEnabled()) {
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="h-5 w-5">
            <path d="M12 14a3 3 0 0 0 3-3V6a3 3 0 1 0-6 0v5a3 3 0 0 0 3 3Z" />
            <path d="M19 11a1 1 0 1 0-2 0 5 5 0 0 1-10 0 1 1 0 1 0-2 0 7 7 0 0 0 6 6.92V20H9a1 1 0 1 0 0 2h6a1 1 0 1 0 0-2h-2v-2.08A7 7 0 0 0 19 11Z" />
          </svg>
        } @else {
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="h-5 w-5">
            <path d="M4.3 3.3a1 1 0 0 0-1.4 1.4l16 16a1 1 0 0 0 1.4-1.4l-2.97-2.97A6.96 6.96 0 0 0 19 11a1 1 0 1 0-2 0 5 5 0 0 1-.88 2.83l-1.47-1.47A3 3 0 0 0 15 11V6a3 3 0 0 0-5.82-1.06L4.3 3.3Z" />
            <path d="M9 9.42v1.58a3 3 0 0 0 4.58 2.54l1.46 1.46A5 5 0 0 1 7 11a1 1 0 1 0-2 0 7 7 0 0 0 6 6.92V20H9a1 1 0 1 0 0 2h6a1 1 0 1 0 0-2h-2v-2.08a6.95 6.95 0 0 0 2.2-.82L9 9.42Z" />
          </svg>
        }
      </button>

      <!-- Conversation panel (staff with telemetry:read) -->
      @if (showPanel()) {
        <button
          type="button"
          class="flex h-12 w-12 items-center justify-center rounded-full transition-colors"
          [style.background]="panelOpen() ? 'var(--bf-accent)' : 'var(--bf-surface)'"
          [style.color]="panelOpen() ? 'var(--bf-cta-fg)' : 'var(--bf-fg)'"
          title="Ver conversación"
          aria-label="Ver conversación"
          (click)="togglePanel.emit()"
        >
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="h-5 w-5">
            <path d="M12 8a2 2 0 1 0 0-4 2 2 0 0 0 0 4Zm0 6a2 2 0 1 0 0-4 2 2 0 0 0 0 4Zm0 6a2 2 0 1 0 0-4 2 2 0 0 0 0 4Z" />
          </svg>
        </button>
      }

      @if (showUseChat()) {
        <!-- Switch to text chat -->
        <button
          type="button"
          class="flex h-12 items-center justify-center gap-1.5 rounded-full bg-neutral-800 px-4 text-sm font-semibold text-white transition-colors hover:bg-neutral-700"
          title="Usar chat de texto"
          aria-label="Usar chat de texto"
          (click)="useChat.emit()"
        >
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="h-4 w-4">
            <path d="M4 4h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H8l-4 4V6a2 2 0 0 1 2-2Z" />
          </svg>
          Chat
        </button>
      }

      <!-- Divider -->
      <span class="mx-1 h-8 w-px bg-white/10"></span>

      <!-- Pause session -->
      <button
        type="button"
        class="flex h-12 items-center gap-2 rounded-full bg-neutral-700/90 px-4 text-sm font-semibold text-white transition-colors hover:bg-neutral-600"
        title="Pausar sesión (puedes reanudarla después)"
        aria-label="Pausar sesión"
        (click)="pause.emit()"
      >
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="h-4 w-4">
          <path d="M8 5a1 1 0 0 1 1 1v12a1 1 0 1 1-2 0V6a1 1 0 0 1 1-1Zm8 0a1 1 0 0 1 1 1v12a1 1 0 1 1-2 0V6a1 1 0 0 1 1-1Z" />
        </svg>
        Pausar
      </button>

      <!-- Close / end session -->
      <button
        type="button"
        class="flex h-12 items-center gap-2 rounded-full bg-red-500/90 px-4 text-sm font-semibold text-white transition-colors hover:bg-red-500"
        title="Terminar sesión"
        aria-label="Cerrar sesión"
        (click)="close.emit()"
      >
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="h-4 w-4">
          <path d="M6.2 4.8a1 1 0 0 1 1.4 0L12 9.2l4.4-4.4a1 1 0 1 1 1.4 1.4L13.4 10.6l4.4 4.4a1 1 0 1 1-1.4 1.4L12 12l-4.4 4.4a1 1 0 1 1-1.4-1.4l4.4-4.4-4.4-4.4a1 1 0 0 1 0-1.4Z" />
        </svg>
        Cerrar
      </button>
    </div>
  `,
})
export class BottomBar {
  readonly micEnabled = input<boolean>(false);
  readonly panelOpen = input<boolean>(false);
  readonly showPanel = input<boolean>(true);
  readonly showUseChat = input<boolean>(false);

  readonly toggleMic = output<void>();
  readonly togglePanel = output<void>();
  readonly useChat = output<void>();
  readonly pause = output<void>();
  readonly close = output<void>();
}
