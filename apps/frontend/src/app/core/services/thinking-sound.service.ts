import { Injectable, inject } from '@angular/core';
import { Api } from '../../api/api';
import type { PublicBotResponse } from '../../api/models/public-bot-response';

export const DEFAULT_THINKING_SOUND_URL = '/audio/default-thinking.mp3';

const PRELOAD_TIMEOUT_MS = 5_000;

@Injectable({ providedIn: 'root' })
export class ThinkingSoundService {
  private readonly api = inject(Api);

  private audio: HTMLAudioElement | null = null;
  private loadedSrc: string | null = null;
  private unlocked = false;

  /** Call synchronously from a user gesture (e.g. connect tap) so mobile browsers allow playback. */
  unlock(): void {
    this.unlocked = true;
    const audio = this.ensureAudio(DEFAULT_THINKING_SOUND_URL);
    void audio
      .play()
      .then(() => {
        audio.pause();
        audio.currentTime = 0;
      })
      .catch(() => {
        // Best-effort prime during user gesture.
      });
  }

  resolveUrl(bot: PublicBotResponse, slug: string): string {
    if (!bot.hasCustomThinkingSound) {
      return DEFAULT_THINKING_SOUND_URL;
    }

    const base = `${this.api.rootUrl}/api/bots/by-slug/${encodeURIComponent(slug)}/thinking-sound/download`;
    return bot.thinkingSoundVersion
      ? `${base}?v=${encodeURIComponent(bot.thinkingSoundVersion)}`
      : base;
  }

  async preload(url: string): Promise<void> {
    if (!this.unlocked) return;

    const audio = this.ensureAudio(url);
    if (audio.readyState >= HTMLMediaElement.HAVE_FUTURE_DATA) {
      return;
    }

    await this.waitForReady(audio);
  }

  async play(url: string): Promise<void> {
    if (!this.unlocked) return;

    try {
      await this.preload(url);
      if (!this.audio) return;
      if (this.audio.paused) {
        await this.audio.play();
      }
    } catch (err) {
      console.debug('[ThinkingSoundService] playback failed', err);
    }
  }

  stop(): void {
    if (this.audio) {
      this.audio.pause();
      this.audio.currentTime = 0;
    }
  }

  reset(): void {
    this.stop();
    this.audio = null;
    this.loadedSrc = null;
  }

  private ensureAudio(url: string): HTMLAudioElement {
    if (!this.audio) {
      this.audio = new Audio(url);
      this.audio.loop = true;
      this.audio.preload = 'auto';
      this.audio.setAttribute('playsinline', '');
      this.loadedSrc = url;
      return this.audio;
    }

    if (this.loadedSrc !== url) {
      this.audio.src = url;
      this.audio.load();
      this.loadedSrc = url;
    }

    return this.audio;
  }

  private waitForReady(audio: HTMLAudioElement): Promise<void> {
    if (audio.readyState >= HTMLMediaElement.HAVE_FUTURE_DATA) {
      return Promise.resolve();
    }

    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        cleanup();
        if (audio.readyState >= HTMLMediaElement.HAVE_FUTURE_DATA) {
          resolve();
        } else {
          reject(new Error('Thinking sound preload timed out'));
        }
      }, PRELOAD_TIMEOUT_MS);

      const onReady = () => {
        cleanup();
        resolve();
      };
      const onError = () => {
        cleanup();
        reject(new Error('Thinking sound failed to load'));
      };
      const cleanup = () => {
        clearTimeout(timeout);
        audio.removeEventListener('canplaythrough', onReady);
        audio.removeEventListener('error', onError);
      };

      audio.addEventListener('canplaythrough', onReady, { once: true });
      audio.addEventListener('error', onError, { once: true });
    });
  }
}
