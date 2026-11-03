import {
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
} from '@angular/core';
import type { AgentActivity } from './voice-visualizer';

@Component({
  selector: 'bf-status-indicator',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex items-center gap-3" style="color: var(--bf-fg)">
      @if (activity() === 'listening') {
        <div class="flex items-end gap-1">
          <span class="h-2 w-1 animate-[bf-bar_1s_ease-in-out_infinite] rounded-full" style="background: var(--bf-fg)"></span>
          <span class="h-3 w-1 animate-[bf-bar_1s_ease-in-out_infinite_0.15s] rounded-full" style="background: var(--bf-fg)"></span>
          <span class="h-4 w-1 animate-[bf-bar_1s_ease-in-out_infinite_0.3s] rounded-full" style="background: var(--bf-fg)"></span>
          <span class="h-3 w-1 animate-[bf-bar_1s_ease-in-out_infinite_0.45s] rounded-full" style="background: var(--bf-fg)"></span>
          <span class="h-2 w-1 animate-[bf-bar_1s_ease-in-out_infinite_0.6s] rounded-full" style="background: var(--bf-fg)"></span>
        </div>
      } @else if (activity() === 'thinking') {
        <div class="flex gap-1.5">
          <span class="h-2 w-2 animate-[bf-dot_1.4s_ease-in-out_infinite] rounded-full" style="background: var(--bf-fg)"></span>
          <span class="h-2 w-2 animate-[bf-dot_1.4s_ease-in-out_infinite_0.2s] rounded-full" style="background: var(--bf-fg)"></span>
          <span class="h-2 w-2 animate-[bf-dot_1.4s_ease-in-out_infinite_0.4s] rounded-full" style="background: var(--bf-fg)"></span>
        </div>
      } @else if (activity() === 'speaking') {
        <span class="inline-block h-2 w-2 animate-pulse rounded-full bg-emerald-400"></span>
      } @else if (activity() === 'paused') {
        <span class="inline-block h-2 w-2 rounded-full bg-amber-400"></span>
      }
      <span class="text-sm font-medium tracking-wide">{{ label() }}</span>
    </div>
  `,
  styles: [
    `
      @keyframes bf-bar {
        0%, 100% { transform: scaleY(0.5); }
        50% { transform: scaleY(1); }
      }
      @keyframes bf-dot {
        0%, 80%, 100% { opacity: 0.3; transform: scale(0.8); }
        40% { opacity: 1; transform: scale(1); }
      }
    `,
  ],
})
export class StatusIndicator {
  readonly activity = input<AgentActivity>('idle');
  readonly connectionLabel = input<string | null>(null);

  readonly label = computed(() => {
    const override = this.connectionLabel();
    if (override) return override;
    switch (this.activity()) {
      case 'listening':
        return 'Te estoy escuchando';
      case 'thinking':
        return 'Pensando...';
      case 'speaking':
        return 'Hablando';
      case 'paused':
        return 'En pausa';
      default:
        return 'Listo';
    }
  });
}
