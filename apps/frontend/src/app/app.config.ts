import { APP_INITIALIZER, ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, TitleStrategy } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';

import { routes } from './app.routes';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { AppTitleStrategy } from './core/title-strategy';
import { AuthService } from './core/services/auth.service';
import { provideApiConfiguration } from './api/api-configuration';
import { environment } from '../environments/environment';

const apiUrl = window.__env?.apiUrl || environment.apiUrl;

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor])),
    provideApiConfiguration(apiUrl),
    { provide: TitleStrategy, useClass: AppTitleStrategy },
    {
      provide: APP_INITIALIZER,
      useFactory: (authService: AuthService) => () => authService.initialize(),
      deps: [AuthService],
      multi: true,
    },
  ],
};
