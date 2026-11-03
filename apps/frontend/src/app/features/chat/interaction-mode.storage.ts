export type InteractionMode = 'voice' | 'text';

const STORAGE_PREFIX = 'bf:interaction-mode:';

export function readStoredInteractionMode(slug: string): InteractionMode | null {
  try {
    const raw = localStorage.getItem(`${STORAGE_PREFIX}${slug}`);
    return raw === 'voice' || raw === 'text' ? raw : null;
  } catch {
    return null;
  }
}

export function writeStoredInteractionMode(slug: string, mode: InteractionMode): void {
  try {
    localStorage.setItem(`${STORAGE_PREFIX}${slug}`, mode);
  } catch {
    /* ignore quota / private mode */
  }
}

export function resolvePreferredMode(
  slug: string,
  enableChat: boolean,
  enableVoice: boolean,
): InteractionMode {
  if (enableChat && !enableVoice) return 'text';
  if (enableVoice && !enableChat) return 'voice';

  const stored = readStoredInteractionMode(slug);
  if (stored === 'text' && enableChat) return 'text';
  if (stored === 'voice' && enableVoice) return 'voice';
  return enableVoice ? 'voice' : 'text';
}
