import { describe, expect, it } from 'vitest';
import { PublicSessionService } from './public-session.service';

describe('PublicSessionService', () => {
  it('start / refresh / clear manage in-memory signals', () => {
    const svc = new PublicSessionService();
    expect(svc.token()).toBeNull();

    svc.start('pub123', 'tok-a', '2030-01-01T00:00:00.000Z');
    expect(svc.publicId()).toBe('pub123');
    expect(svc.token()).toBe('tok-a');
    expect(svc.expiresAt()?.toISOString()).toBe('2030-01-01T00:00:00.000Z');

    svc.refresh('tok-b', '2031-06-01T12:00:00.000Z');
    expect(svc.token()).toBe('tok-b');
    expect(svc.publicId()).toBe('pub123');
    expect(svc.expiresAt()?.toISOString()).toBe('2031-06-01T12:00:00.000Z');

    svc.clear();
    expect(svc.token()).toBeNull();
    expect(svc.expiresAt()).toBeNull();
    expect(svc.publicId()).toBeNull();
  });
});
