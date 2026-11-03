import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { PublicSessionService } from '../services/public-session.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const publicSession = inject(PublicSessionService);

  req = req.clone({ withCredentials: true });

  const pathname = new URL(req.url, location.origin).pathname;
  const isPublicEndpoint = pathname.includes('/api/public/');

  // Anonymous public sessions authenticate with an in-memory bearer token
  // (no cookie), scoped strictly to the /api/public surface.
  if (isPublicEndpoint) {
    const token = publicSession.token();
    if (token) {
      req = req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
    }
  }

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      // On 401 from non-auth endpoints, redirect to login.
      // All /auth/ endpoints handle their own errors (initialize, login, etc.)
      // Public endpoints also handle their own 401s (expired anonymous token) —
      // bouncing an anonymous visitor into the GOB-ID login would be wrong.
      const isAuthEndpoint = pathname.includes('/auth/');

      if (error.status === 401 && !isAuthEndpoint && !isPublicEndpoint) {
        authService.logout();
      }
      return throwError(() => error);
    }),
  );
};
