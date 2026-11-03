import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  input,
  OnDestroy,
  signal,
} from '@angular/core';
import type { LocalAudioTrack, RemoteAudioTrack } from 'livekit-client';

export type AgentActivity =
  | 'idle'
  | 'listening'
  | 'thinking'
  | 'speaking'
  | 'paused';

type AudioTrackLike = RemoteAudioTrack | LocalAudioTrack | null | undefined;

const BAR_COUNT = 12;

@Component({
  selector: 'bf-voice-visualizer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="relative flex h-full w-full items-center justify-center">
      <div
        class="absolute h-[420px] w-[420px] rounded-full blur-3xl transition-[transform,background-color,opacity] duration-[400ms] ease-[cubic-bezier(0.23,1,0.32,1)]"
        [class.bg-amber-400]="isPaused()"
        [class.bg-opacity-10]="isPaused()"
        [class.opacity-60]="isPaused()"
        [style.background]="isPaused() ? null : 'color-mix(in srgb, var(--bf-accent) 12%, transparent)'"
        [style.transform]="'scale(' + (isPaused() ? 0.85 : glowScale()) + ')'"
      ></div>

      @if (activity() === 'listening') {
        <!-- Audio bars: rendered when listening -->
        <div
          class="relative flex h-[260px] w-[340px] items-center justify-center gap-2 transition-opacity duration-300"
          [class.opacity-40]="muted()"
        >
          @for (level of barLevels(); track $index) {
            <span
              class="inline-block w-3 rounded-full transition-[height] duration-75 ease-out"
              style="background: color-mix(in srgb, var(--bf-accent) 85%, transparent)"
              [style.height.%]="level"
            ></span>
          }
        </div>
      } @else {
        @if (activity() === 'thinking') {
          <div class="pointer-events-none absolute inset-0 flex items-center justify-center">
            <div class="relative h-[320px] w-[320px] animate-[spin_4s_linear_infinite]">
              <span class="absolute left-1/2 top-0 h-6 w-6 -translate-x-1/2 rounded-full" style="background: var(--bf-accent); opacity: 0.9"></span>
              <span class="absolute right-0 top-1/2 h-4 w-4 -translate-y-1/2 rounded-full" style="background: var(--bf-accent); opacity: 0.6"></span>
              <span class="absolute bottom-0 left-1/2 h-3 w-3 -translate-x-1/2 rounded-full" style="background: var(--bf-accent); opacity: 0.5"></span>
            </div>
          </div>
        }

        <div
          class="relative flex items-center justify-center rounded-full transition-[transform,background-color,box-shadow,filter] duration-[400ms] ease-[cubic-bezier(0.23,1,0.32,1)]"
          [class.bg-neutral-300]="isPaused()"
          [class.animate-breathe]="activity() === 'idle'"
          [style.background]="isPaused() ? null : 'var(--bf-accent)'"
          [style.width.px]="240"
          [style.height.px]="240"
          [style.transform]="'scale(' + (isPaused() ? 0.88 : blobScale()) + ')'"
          [style.boxShadow]="isPaused()
            ? '0 0 60px 10px rgba(251, 191, 36, 0.18), inset 0 0 40px rgba(0,0,0,0.05)'
            : '0 0 120px 30px color-mix(in srgb, var(--bf-accent) 35%, transparent)'"
          [style.filter]="isPaused() ? 'saturate(0.7)' : 'none'"
        >
          <!-- Pause glyph: fades+scales in when paused. -->
          <div
            class="pointer-events-none flex items-center gap-3 transition-[opacity,transform] duration-[350ms] ease-[cubic-bezier(0.23,1,0.32,1)]"
            [class.opacity-0]="!isPaused()"
            [class.opacity-100]="isPaused()"
            [style.transform]="isPaused() ? 'scale(1)' : 'scale(0.6)'"
          >
            <span class="block h-12 w-3 rounded-full bg-neutral-700/80"></span>
            <span class="block h-12 w-3 rounded-full bg-neutral-700/80"></span>
          </div>
        </div>
      }
    </div>
  `,
  styles: [
    `
      @keyframes bf-breathe {
        0%, 100% { transform: scale(1); }
        50% { transform: scale(1.04); }
      }
      .animate-breathe {
        animation: bf-breathe 3.2s ease-in-out infinite;
      }
    `,
  ],
})
export class VoiceVisualizer implements AfterViewInit, OnDestroy {
  readonly activity = input<AgentActivity>('idle');
  readonly audioTrack = input<RemoteAudioTrack | null>(null);
  readonly localAudioTrack = input<LocalAudioTrack | null>(null);
  readonly muted = input<boolean>(false);

  readonly isPaused = computed(() => this.activity() === 'paused');

  readonly blobScale = signal(1);
  readonly glowScale = signal(1);
  readonly barLevels = signal<number[]>(
    Array.from({ length: BAR_COUNT }, () => 25),
  );

  private audioContext: AudioContext | null = null;
  private analyser: AnalyserNode | null = null;
  private sourceNode: MediaStreamAudioSourceNode | null = null;
  private dataArray: Uint8Array<ArrayBuffer> | null = null;
  private rafId: number | null = null;
  private attachedTrack: AudioTrackLike = null;
  private attachedMode: 'speaking' | 'listening' | null = null;

  constructor() {
    effect(() => {
      const activity = this.activity();
      if (activity === 'speaking') {
        this.attachTrack(this.audioTrack(), 'speaking');
      } else if (activity === 'listening') {
        this.attachTrack(this.localAudioTrack(), 'listening');
      } else {
        this.attachTrack(null, null);
        this.blobScale.set(1);
        this.glowScale.set(activity === 'thinking' ? 1.1 : 1);
        // 'paused' uses the same idle blob; CSS transitions handle the morph.
      }
    });
  }

  ngAfterViewInit(): void {
    this.loop();
  }

  ngOnDestroy(): void {
    if (this.rafId !== null) {
      cancelAnimationFrame(this.rafId);
      this.rafId = null;
    }
    this.teardownAudio();
  }

  private attachTrack(
    track: AudioTrackLike,
    mode: 'speaking' | 'listening' | null,
  ): void {
    if (track === this.attachedTrack && mode === this.attachedMode) return;
    this.teardownAudio();
    this.attachedTrack = track ?? null;
    this.attachedMode = mode;
    if (!track) return;

    try {
      const mediaStream =
        track.mediaStream ?? new MediaStream([track.mediaStreamTrack]);
      const AudioCtx =
        (window as unknown as { AudioContext?: typeof AudioContext; webkitAudioContext?: typeof AudioContext })
          .AudioContext ??
        (window as unknown as { webkitAudioContext?: typeof AudioContext })
          .webkitAudioContext;
      if (!AudioCtx) return;
      this.audioContext = new AudioCtx();
      this.sourceNode = this.audioContext.createMediaStreamSource(mediaStream);
      this.analyser = this.audioContext.createAnalyser();
      this.analyser.fftSize = mode === 'listening' ? 128 : 256;
      this.analyser.smoothingTimeConstant = mode === 'listening' ? 0.7 : 0.75;
      this.sourceNode.connect(this.analyser);
      this.dataArray = new Uint8Array(
        new ArrayBuffer(this.analyser.frequencyBinCount),
      );
    } catch {
      this.teardownAudio();
    }
  }

  private teardownAudio(): void {
    try {
      this.sourceNode?.disconnect();
    } catch {
      // no-op
    }
    this.sourceNode = null;
    this.analyser = null;
    this.dataArray = null;
    if (this.audioContext && this.audioContext.state !== 'closed') {
      this.audioContext.close().catch(() => undefined);
    }
    this.audioContext = null;
    this.attachedTrack = null;
    this.attachedMode = null;
  }

  private loop = (): void => {
    this.rafId = requestAnimationFrame(this.loop);
    const activity = this.activity();

    if (activity === 'speaking') {
      this.stepSpeaking();
    } else if (activity === 'listening') {
      this.stepListening();
    }
  };

  private stepSpeaking(): void {
    if (!this.analyser || !this.dataArray) {
      return;
    }
    this.analyser.getByteFrequencyData(this.dataArray);
    let sum = 0;
    for (let i = 0; i < this.dataArray.length; i++) {
      sum += this.dataArray[i] * this.dataArray[i];
    }
    const rms = Math.sqrt(sum / this.dataArray.length) / 255;
    const target = 1 + Math.min(rms * 2, 0.3);
    const current = this.blobScale();
    const next = current + (target - current) * 0.25;
    this.blobScale.set(next);
    this.glowScale.set(1 + (next - 1) * 2);
  }

  private stepListening(): void {
    const previous = this.barLevels();
    const next: number[] = new Array(BAR_COUNT);
    const now = performance.now() / 1000;

    if (this.analyser && this.dataArray) {
      this.analyser.getByteFrequencyData(this.dataArray);
      const usable = Math.min(this.dataArray.length, 48);
      const step = Math.max(1, Math.floor(usable / BAR_COUNT));
      for (let i = 0; i < BAR_COUNT; i++) {
        const start = i * step;
        let sum = 0;
        for (let j = 0; j < step; j++) {
          sum += this.dataArray[start + j] ?? 0;
        }
        const avg = sum / step / 255;
        // Gentle baseline + amplitude reaction; gives life even in silence.
        const wobble =
          15 + 6 * Math.sin(now * 2.3 + i * 0.6) +
          4 * Math.sin(now * 3.7 + i * 1.1);
        const target = Math.min(100, wobble + avg * 120);
        next[i] = previous[i] + (target - previous[i]) * 0.4;
      }
    } else {
      // Procedural wave when we have no mic analyser (e.g. mic muted).
      for (let i = 0; i < BAR_COUNT; i++) {
        const wobble =
          20 +
          10 * Math.sin(now * 2.1 + i * 0.55) +
          6 * Math.sin(now * 3.3 + i * 1.3);
        next[i] = previous[i] + (wobble - previous[i]) * 0.25;
      }
    }
    this.barLevels.set(next);
  }
}
