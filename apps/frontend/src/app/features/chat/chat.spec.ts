import { describe, expect, it } from 'vitest';
import { shouldUsePublicSession } from './public-session-mode';

describe('chat helpers', () => {
  it('shouldUsePublicSession is true only for anonymous public bots', () => {
    expect(shouldUsePublicSession(true, false)).toBe(true);
    expect(shouldUsePublicSession(true, true)).toBe(false);
    expect(shouldUsePublicSession(false, false)).toBe(false);
    expect(shouldUsePublicSession(false, true)).toBe(false);
  });
});
