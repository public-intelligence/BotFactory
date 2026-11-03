import { Injectable, signal } from '@angular/core';

/**
 * Holds the anonymous public-session token in memory only — never cookies or
 * storage: third-party cookies are blocked/partitioned inside iframes, and the
 * token is short-lived (~15 min) by design. Set when a public chat session
 * starts; the auth interceptor attaches it to /api/public requests and the
 * conversation stream presents it as the SignalR access token.
 */
@Injectable({ providedIn: 'root' })
export class PublicSessionService {
  readonly token = signal<string | null>(null);
  readonly expiresAt = signal<Date | null>(null);
  readonly publicId = signal<string | null>(null);

  start(publicId: string, token: string, expiresAt: string): void {
    this.publicId.set(publicId);
    this.refresh(token, expiresAt);
  }

  /** Resume responses re-mint the token; keep the freshest one. */
  refresh(token: string, expiresAt: string): void {
    this.token.set(token);
    this.expiresAt.set(new Date(expiresAt));
  }

  clear(): void {
    this.token.set(null);
    this.expiresAt.set(null);
    this.publicId.set(null);
  }
}
