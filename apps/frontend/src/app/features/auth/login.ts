import { Component, inject, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import type { AuthProviderInfo } from '../../api/models/auth-provider-info';

@Component({
  selector: 'bf-login',
  template: `
    <div
      class="flex min-h-screen items-center justify-center bg-blue-50 p-4"
    >
      <div class="w-full max-w-sm rounded-2xl bg-white p-10 shadow-lg">
        <div class="flex flex-col items-center gap-6">
          <!-- Logo & Title -->
          <div class="flex flex-col items-center gap-3">
            <img
              src="cupula-gob-azul.webp"
              alt="Bot Factory"
              class="h-16 w-auto object-contain"
            />
            <h1 class="text-xl font-bold text-neutral-900">Bot Factory</h1>
            <p class="text-sm text-neutral-500">
              Plataforma de bots conversacionales con IA
            </p>
          </div>

          <!-- Login buttons -->
          @for (provider of providers(); track provider.name) {
            <button
              (click)="loginWith(provider)"
              [class]="provider.name === 'gobid'
                ? 'flex w-full items-center justify-center gap-2 rounded-lg bg-blue-600 px-4 py-3 text-sm font-medium text-white transition hover:bg-blue-700 cursor-pointer'
                : 'flex w-full items-center justify-center gap-2 rounded-lg bg-neutral-800 px-4 py-3 text-sm font-medium text-white transition hover:bg-neutral-900 cursor-pointer'"
            >
              @if (provider.name === 'gobid') {
                <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                  <path d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
                </svg>
              } @else {
                <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                  <path d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
                </svg>
              }
              Iniciar sesión con {{ provider.label }}
            </button>
          }

          <!-- Footer -->
          <p class="text-xs text-neutral-400">v0.1</p>
        </div>
      </div>
    </div>
  `,
})
export class Login implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  readonly providers = signal<AuthProviderInfo[]>([]);

  async ngOnInit() {
    if (this.authService.isAuthenticated()) {
      this.router.navigate(['/dashboard']);
      return;
    }

    const providers = await this.authService.getProviders();
    this.providers.set(providers);
  }

  loginWith(provider: AuthProviderInfo) {
    window.location.href = provider.url;
  }
}
