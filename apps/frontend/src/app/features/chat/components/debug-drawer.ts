import {
  ChangeDetectionStrategy,
  Component,
  input,
  output,
} from '@angular/core';
import type { BotResponse } from '../../../api/models/bot-response';

@Component({
  selector: 'bf-debug-drawer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (open()) {
      <div
        class="pointer-events-auto fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm"
        role="dialog"
        aria-modal="true"
        aria-label="Parámetros activos"
        (click)="closed.emit()"
      >
        <div
          class="w-full max-w-md overflow-hidden rounded-2xl border border-white/10 bg-neutral-900/95 shadow-2xl"
          (click)="$event.stopPropagation()"
        >
          <header class="flex items-center justify-between border-b border-white/10 px-5 py-4">
            <div>
              <h2 class="text-sm font-semibold text-white">Parámetros activos</h2>
              <p class="text-xs text-neutral-400">Configuración del bot</p>
            </div>
            <button
              type="button"
              class="flex h-8 w-8 items-center justify-center rounded-full text-neutral-400 transition-colors hover:bg-white/5 hover:text-white"
              aria-label="Cerrar"
              (click)="closed.emit()"
            >
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="h-4 w-4">
                <path d="M6.2 4.8a1 1 0 0 1 1.4 0L12 9.2l4.4-4.4a1 1 0 1 1 1.4 1.4L13.4 10.6l4.4 4.4a1 1 0 1 1-1.4 1.4L12 12l-4.4 4.4a1 1 0 1 1-1.4-1.4l4.4-4.4-4.4-4.4a1 1 0 0 1 0-1.4Z" />
              </svg>
            </button>
          </header>

          <div class="px-5 py-5">
            @if (bot(); as b) {
              <dl class="grid grid-cols-2 gap-x-3 gap-y-1 rounded-lg border border-white/10 bg-white/[0.03] p-3 text-xs">
                <dt class="text-neutral-400">Bot</dt>
                <dd class="text-right text-neutral-200">{{ b.botName }}</dd>
                <dt class="text-neutral-400">URL slug</dt>
                <dd class="text-right font-mono text-neutral-200">{{ b.urlSlug }}</dd>
                <dt class="text-neutral-400">top_k</dt>
                <dd class="text-right text-neutral-200">{{ b.topK }}</dd>
                <dt class="text-neutral-400">silence_end_ms</dt>
                <dd class="text-right text-neutral-200">{{ b.silenceEndMs }}</dd>
                <dt class="text-neutral-400">max_utterance_ms</dt>
                <dd class="text-right text-neutral-200">{{ b.maxUtteranceMs }}</dd>
                <dt class="text-neutral-400">barge-in</dt>
                <dd class="text-right text-neutral-200">{{ b.enableBargeIn ? 'sí' : 'no' }}</dd>
                <dt class="text-neutral-400">TTS</dt>
                <dd class="text-right text-neutral-200">{{ b.enableTts ? 'sí' : 'no' }}</dd>
                <dt class="text-neutral-400">estado</dt>
                <dd class="text-right text-neutral-200">{{ b.publishedState }}</dd>
              </dl>
            } @else {
              <p class="text-xs text-neutral-500">Cargando parámetros…</p>
            }
          </div>
        </div>
      </div>
    }
  `,
})
export class DebugDrawer {
  readonly open = input<boolean>(false);
  readonly bot = input<BotResponse | null>(null);

  readonly closed = output<void>();
}
