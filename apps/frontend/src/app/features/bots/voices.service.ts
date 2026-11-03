import { Injectable, inject, signal } from '@angular/core';
import { Api } from '../../api/api';
import { getElevenLabsVoices } from '../../api/functions';
import type { VoiceDto } from '../../api/models/voice-dto';

@Injectable({ providedIn: 'root' })
export class VoicesService {
  private readonly api = inject(Api);

  readonly voices = signal<VoiceDto[]>([]);
  readonly configured = signal<boolean>(true);
  readonly loading = signal<boolean>(false);
  readonly fetchedAt = signal<string | null>(null);

  async load(): Promise<void> {
    await this.fetch(false);
  }

  async refresh(): Promise<void> {
    await this.fetch(true);
  }

  private async fetch(force: boolean): Promise<void> {
    this.loading.set(true);
    try {
      const resp = await this.api.invoke(getElevenLabsVoices, force ? { refresh: true } : {});
      this.voices.set(resp.voices ?? []);
      this.configured.set(resp.configured);
      this.fetchedAt.set(resp.fetchedAt ?? null);
    } finally {
      this.loading.set(false);
    }
  }

  findById(voiceId: string | null | undefined): VoiceDto | null {
    if (!voiceId) return null;
    return this.voices().find((v) => v.voiceId === voiceId) ?? null;
  }
}
