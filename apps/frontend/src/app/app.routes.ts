import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    title: 'Iniciar Sesión',
    loadComponent: () => import('./features/auth/login').then((m) => m.Login),
  },
  {
    path: 'auth/callback',
    loadComponent: () => import('./features/auth/auth-callback').then((m) => m.AuthCallback),
  },
  {
    path: 'chat/:slug',
    title: 'Chat',
    loadComponent: () => import('./features/chat/chat').then((m) => m.Chat),
  },
  {
    // Embedded chat: the document at /api/embed/{publicId} is served by the
    // backend (with a per-bot frame-ancestors header) but boots this same SPA,
    // so the router must recognize the path. `embedded` suppresses navigation
    // that would take over the host page.
    path: 'api/embed/:slug',
    title: 'Chat',
    data: { embedded: true },
    loadComponent: () => import('./features/chat/chat').then((m) => m.Chat),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout-shell').then((m) => m.LayoutShell),
    children: [
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
      {
        path: 'dashboard',
        title: 'Dashboard',
        loadComponent: () =>
          import('./features/dashboard/dashboard').then((m) => m.Dashboard),
      },
      {
        path: 'bots',
        redirectTo: 'dashboard',
        pathMatch: 'full',
      },
      {
        path: 'bots/create',
        title: 'Crear Bot',
        loadComponent: () =>
          import('./features/bots/bot-create').then((m) => m.BotCreate),
      },
      {
        path: 'bots/:id/edit',
        title: 'Editar Bot',
        loadComponent: () =>
          import('./features/bots/bot-edit').then((m) => m.BotEdit),
        children: [
          { path: '', redirectTo: 'general', pathMatch: 'full' },
          {
            path: 'general',
            loadComponent: () =>
              import('./features/bots/bot-edit-general').then(
                (m) => m.BotEditGeneral,
              ),
          },
          {
            path: 'apariencia',
            loadComponent: () =>
              import('./features/bots/bot-edit-apariencia').then(
                (m) => m.BotEditApariencia,
              ),
          },
          {
            path: 'voz',
            loadComponent: () =>
              import('./features/bots/bot-edit-voz').then(
                (m) => m.BotEditVoz,
              ),
          },
          {
            path: 'kb',
            loadComponent: () =>
              import('./features/bots/bot-edit-kb').then(
                (m) => m.BotEditKb,
              ),
          },
          {
            path: 'debug',
            loadComponent: () =>
              import('./features/bots/bot-debug-search').then(
                (m) => m.BotDebugSearch,
              ),
          },
          {
            path: 'chat-history',
            loadComponent: () =>
              import('./features/bots/bot-edit-chat-history').then(
                (m) => m.BotEditChatHistory,
              ),
          },
          {
            path: 'herramientas',
            loadComponent: () =>
              import('./features/bots/bot-edit-tools').then(
                (m) => m.BotEditTools,
              ),
          },
        ],
      },
      {
        path: 'tools',
        title: 'Herramientas',
        loadComponent: () =>
          import('./features/tools/tools-list').then((m) => m.ToolsList),
      },
      {
        path: 'tools/create',
        title: 'Nueva Herramienta',
        loadComponent: () =>
          import('./features/tools/tool-create').then((m) => m.ToolCreate),
      },
      {
        path: 'tools/:id/edit',
        title: 'Editar Herramienta',
        loadComponent: () =>
          import('./features/tools/tool-edit').then((m) => m.ToolEdit),
      },
      {
        path: 'settings',
        title: 'Configuración',
        loadComponent: () =>
          import('./features/settings/settings').then((m) => m.Settings),
      },
      {
        path: 'audit-logs',
        title: 'Auditoría',
        loadComponent: () =>
          import('./features/audit/audit-logs').then((m) => m.AuditLogs),
      },
      {
        path: 'logs',
        title: 'Registros',
        loadComponent: () =>
          import('./features/logs/system-logs').then((m) => m.SystemLogs),
      },
    ],
  },
  { path: '**', redirectTo: 'dashboard' },
];
