import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from '../services/auth.service';
import { PublicSessionService } from '../services/public-session.service';
import { firstValueFrom } from 'rxjs';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let logout: ReturnType<typeof vi.fn>;
  let publicSession: PublicSessionService;

  beforeEach(() => {
    logout = vi.fn().mockResolvedValue(undefined);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        PublicSessionService,
        { provide: AuthService, useValue: { logout } },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    publicSession = TestBed.inject(PublicSessionService);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('sets withCredentials on requests', async () => {
    const promise = firstValueFrom(http.get('/api/bots'));
    const req = httpMock.expectOne('/api/bots');
    expect(req.request.withCredentials).toBe(true);
    req.flush([]);
    await promise;
  });

  it('adds bearer token on public API requests when session exists', async () => {
    publicSession.start('pub1', 'anon-token', '2030-01-01T00:00:00.000Z');

    const promise = firstValueFrom(http.post('/api/public/bots/pub1/chat', {}));
    const req = httpMock.expectOne('/api/public/bots/pub1/chat');
    expect(req.request.headers.get('Authorization')).toBe('Bearer anon-token');
    req.flush({});
    await promise;
  });

  it('does not add bearer token on non-public routes', async () => {
    publicSession.start('pub1', 'anon-token', '2030-01-01T00:00:00.000Z');

    const promise = firstValueFrom(http.get('/api/bots'));
    const req = httpMock.expectOne('/api/bots');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush([]);
    await promise;
  });

  it('calls logout on 401 from protected endpoints', async () => {
    const promise = firstValueFrom(http.get('/api/bots')).catch((e) => e);

    const req = httpMock.expectOne('/api/bots');
    req.flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });

    const error = await promise;
    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect(logout).toHaveBeenCalled();
  });

  it('does not logout on 401 from auth endpoints', async () => {
    const promise = firstValueFrom(http.get('/api/auth/me')).catch((e) => e);

    const req = httpMock.expectOne('/api/auth/me');
    req.flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });

    await promise;
    expect(logout).not.toHaveBeenCalled();
  });

  it('does not logout on 401 from public endpoints', async () => {
    const promise = firstValueFrom(http.get('/api/public/bots/x/chat')).catch((e) => e);

    const req = httpMock.expectOne('/api/public/bots/x/chat');
    req.flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });

    await promise;
    expect(logout).not.toHaveBeenCalled();
  });
});
