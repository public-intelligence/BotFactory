import { afterEach, describe, expect, it } from 'vitest';
import {
  readStoredInteractionMode,
  resolvePreferredMode,
  writeStoredInteractionMode,
} from './interaction-mode.storage';

describe('interaction-mode.storage', () => {
  afterEach(() => {
    localStorage.clear();
  });

  it('read/write round-trips voice and text', () => {
    expect(readStoredInteractionMode('bot-a')).toBeNull();
    writeStoredInteractionMode('bot-a', 'voice');
    expect(readStoredInteractionMode('bot-a')).toBe('voice');
    writeStoredInteractionMode('bot-a', 'text');
    expect(readStoredInteractionMode('bot-a')).toBe('text');
  });

  it('ignores invalid stored values', () => {
    localStorage.setItem('bf:interaction-mode:bot-b', 'video');
    expect(readStoredInteractionMode('bot-b')).toBeNull();
  });

  it('resolvePreferredMode forces the only enabled mode', () => {
    expect(resolvePreferredMode('x', true, false)).toBe('text');
    expect(resolvePreferredMode('x', false, true)).toBe('voice');
  });

  it('resolvePreferredMode prefers stored mode when both enabled', () => {
    writeStoredInteractionMode('dual', 'text');
    expect(resolvePreferredMode('dual', true, true)).toBe('text');
    writeStoredInteractionMode('dual', 'voice');
    expect(resolvePreferredMode('dual', true, true)).toBe('voice');
  });

  it('resolvePreferredMode defaults to voice when both enabled and nothing stored', () => {
    expect(resolvePreferredMode('fresh', true, true)).toBe('voice');
  });
});
