import { Injectable, inject, signal, computed, Injector } from '@angular/core';
import { Router } from '@angular/router';
import { Api } from '../../api/api';
import { getAuthProviders } from '../../api/fn/auth/get-auth-providers';
import { getLoginUrl } from '../../api/fn/auth/get-login-url';
import { exchangeToken } from '../../api/fn/auth/exchange-token';
import { logout } from '../../api/fn/auth/logout';
import { getCurrentUser } from '../../api/fn/auth/get-current-user';
import type { AuthProviderInfo } from '../../api/models/auth-provider-info';
import type { UserInfoResponse } from '../../api/models/user-info-response';
import { BrandingService } from './branding.service';

const RETURN_URL_STORAGE_KEY = 'bf:post-login-return-url';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);

  readonly user = signal<UserInfoResponse | null>(null);
  readonly isAuthenticated = computed(() => !!this.user());

  async getProviders(): Promise<AuthProviderInfo[]> {
    return this.api.invoke(getAuthProviders);
  }

  async getLoginUrl(provider: 'gobid' | 'fake' = 'gobid'): Promise<string> {
    const response = await this.api.invoke(getLoginUrl, { provider });
    return response.url;
  }

  /**
   * Persists a return URL (relative path) that should be navigated to after a
   * successful OIDC callback. Stored in sessionStorage so the redirect round-
   * trip survives (unlike in-memory state).
   */
  setPostLoginReturnUrl(url: string | null): void {
    if (typeof sessionStorage === 'undefined') return;
    if (url) {
      sessionStorage.setItem(RETURN_URL_STORAGE_KEY, url);
    } else {
      sessionStorage.removeItem(RETURN_URL_STORAGE_KEY);
    }
  }

  consumePostLoginReturnUrl(): string | null {
    if (typeof sessionStorage === 'undefined') return null;
    const value = sessionStorage.getItem(RETURN_URL_STORAGE_KEY);
    if (value) sessionStorage.removeItem(RETURN_URL_STORAGE_KEY);
    return value;
  }

  async exchangeCode(code: string, organizationApplicationId: number, provider?: string): Promise<void> {
    await this.api.invoke(exchangeToken, {
      body: { code, organizationApplicationId, provider },
    });
    await this.loadUser();
  }

  async logout(): Promise<void> {
    await this.api.invoke(logout);
    this.user.set(null);
    this.injector.get(BrandingService).clear();
    this.router.navigate(['/login']);
  }

  async loadUser(): Promise<void> {
    const userInfo = await this.api.invoke(getCurrentUser);
    this.user.set(userInfo);
  }

  async initialize(): Promise<void> {
    try {
      await this.loadUser();
    } catch {
      // Not authenticated — session cookie missing or expired
    }
  }
}
