import { Component, OnInit, inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'bf-auth-callback',
  template: `
    <div class="flex flex-col items-center justify-center h-screen gap-4">
      <svg class="animate-spin h-8 w-8 text-primary-600" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
        <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
        <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>
      </svg>
      <p class="text-lg text-gray-500">Autenticando con GOB-ID...</p>
    </div>
  `,
})
export class AuthCallback implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly authService = inject(AuthService);

  async ngOnInit() {
    const params = this.route.snapshot.queryParamMap;
    console.log('[AuthCallback] URL:', window.location.href);
    console.log('[AuthCallback] All query params:', params.keys.map(k => `${k}=${params.get(k)}`));

    const code = params.get('code');
    const organizationApplicationId = params.get('organizationApplicationId') ?? params.get('orgAppId');
    const provider = params.get('provider') || undefined;

    console.log('[AuthCallback] Parsed:', { code: code?.substring(0, 10) + '...', organizationApplicationId, provider });

    if (!code || !organizationApplicationId) {
      console.warn('[AuthCallback] Missing params, redirecting to /login. code:', !!code, 'orgAppId:', !!organizationApplicationId);
      this.router.navigate(['/login']);
      return;
    }

    try {
      console.log('[AuthCallback] Exchanging code...');
      await this.authService.exchangeCode(code, Number(organizationApplicationId), provider);
      const returnUrl = this.authService.consumePostLoginReturnUrl();
      const target = returnUrl && returnUrl.startsWith('/') ? returnUrl : '/dashboard';
      console.log('[AuthCallback] Success, navigating to', target);
      this.router.navigateByUrl(target);
    } catch (err) {
      console.error('[AuthCallback] Exchange failed:', err);
      this.router.navigate(['/login']);
    }
  }
}
