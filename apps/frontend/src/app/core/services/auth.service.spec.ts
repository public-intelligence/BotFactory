import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { AuthService } from './auth.service';
import { Api } from '../../api/api';
import { BrandingService } from './branding.service';
import type { UserInfoResponse } from '../../api/models/user-info-response';

describe('AuthService', () => {
  let service: AuthService;
  let apiInvoke: ReturnType<typeof vi.fn>;
  let routerNavigate: ReturnType<typeof vi.fn>;
  let brandingClear: ReturnType<typeof vi.fn>;

  const sampleUser: UserInfoResponse = {
    userId: 'u1',
    fullName: 'Test User',
    organizationId: '1',
    organizationName: 'Org',
    permissions: ['bot:read'],
  };

  beforeEach(() => {
    apiInvoke = vi.fn();
    routerNavigate = vi.fn();
    brandingClear = vi.fn();
    sessionStorage.clear();

    TestBed.configureTestingModule({
      providers: [
        AuthService,
        { provide: Api, useValue: { invoke: apiInvoke } },
        { provide: Router, useValue: { navigate: routerNavigate } },
        { provide: BrandingService, useValue: { clear: brandingClear } },
      ],
    });
    service = TestBed.inject(AuthService);
  });

  afterEach(() => {
    sessionStorage.clear();
  });

  it('loadUser sets the user signal', async () => {
    apiInvoke.mockResolvedValueOnce(sampleUser);
    await service.loadUser();
    expect(service.user()).toEqual(sampleUser);
    expect(service.isAuthenticated()).toBe(true);
  });

  it('logout clears user and navigates to login', async () => {
    service.user.set(sampleUser);
    apiInvoke.mockResolvedValueOnce(undefined);

    await service.logout();

    expect(apiInvoke).toHaveBeenCalled();
    expect(service.user()).toBeNull();
    expect(brandingClear).toHaveBeenCalled();
    expect(routerNavigate).toHaveBeenCalledWith(['/login']);
  });

  it('setPostLoginReturnUrl and consumePostLoginReturnUrl round-trip', () => {
    service.setPostLoginReturnUrl('/bots/1');
    expect(service.consumePostLoginReturnUrl()).toBe('/bots/1');
    expect(service.consumePostLoginReturnUrl()).toBeNull();
  });

  it('initialize leaves user null when session is missing', async () => {
    apiInvoke.mockRejectedValueOnce(new Error('401'));
    await service.initialize();
    expect(service.user()).toBeNull();
  });

  it('exchangeCode loads user after token exchange', async () => {
    apiInvoke
      .mockResolvedValueOnce(undefined)
      .mockResolvedValueOnce(sampleUser);

    await service.exchangeCode('superadmin@example.com', 1, 'fake');

    expect(apiInvoke).toHaveBeenCalledTimes(2);
    expect(service.user()).toEqual(sampleUser);
  });
});
