import { describe, expect, it } from 'vitest';
import { BotEditStore } from './bot-edit.store';

describe('BotEditStore', () => {
  it('starts in loading state with no bot', () => {
    const store = new BotEditStore();
    expect(store.loading()).toBe(true);
    expect(store.bot()).toBeNull();
    expect(store.error()).toBeNull();
    expect(store.publishing()).toBe(false);
  });

  it('can hold bot snapshot', () => {
    const store = new BotEditStore();
    store.bot.set({
      id: 3,
      botName: 'Demo',
      urlSlug: 'demo',
      publishedState: 'draft',
      masterPrompt: '',
      topK: 5,
      similarityThreshold: 0.5,
      silenceEndMs: 700,
      noSpeechTimeoutMs: 15000,
      maxUtteranceMs: 20000,
      enableBargeIn: true,
      enableTts: true,
      enableTypingSound: true,
      enableChat: true,
      enableVoice: true,
      publicId: 'pub',
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    } as never);
    store.loading.set(false);
    expect(store.bot()?.id).toBe(3);
    expect(store.loading()).toBe(false);
  });
});
