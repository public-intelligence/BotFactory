import { Component, DestroyRef, OnInit, inject, computed, effect } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterOutlet } from '@angular/router';
import { AdminLayoutComponent, LayoutService } from '@public-intelligence/ui-core';
import type { SidebarMenuItem, AppBranding, UserProfile, ProfileMenuItem } from '@public-intelligence/ui-core';
import { AuthService } from './core/services/auth.service';
import { BrandingService } from './core/services/branding.service';

const APP_NAME = 'BotFactory';
const APP_NAME_SHORT = 'BF';
const COLLAPSED_LOGO_URL = 'logo-ogtic-vertical.webp';

@Component({
  selector: 'bf-layout-shell',
  imports: [RouterOutlet, AdminLayoutComponent],
  template: `
    <app-admin-layout
      [branding]="branding()"
      [menuItems]="menuItems()"
      [userProfile]="userProfile()"
      [profileMenuItems]="profileMenuItems"
    >
      <router-outlet />
    </app-admin-layout>
  `,
  host: { '[class.bf-collapsed]': 'layout.isSidebarCollapsed()' },
  styles: `
    /* Expanded sidebar: the organization's logo with the application name beside it.
       The logo never shrinks and is capped so a wide one does not push the name out. */
    :host ::ng-deep app-sidebar img {
      flex-shrink: 0;
      max-width: 6rem;
    }
    :host ::ng-deep app-sidebar img + div {
      min-width: 0;
    }

    /* Collapsed sidebar: the vertical logo with "BF" underneath. ui-core hides the
       text column (width 0, opacity 0) when collapsed, so it is shown again here. */
    :host(.bf-collapsed) ::ng-deep app-sidebar .h-24 > div {
      flex-direction: column;
      gap: 0.125rem;
    }
    :host(.bf-collapsed) ::ng-deep app-sidebar img {
      height: 3.5rem !important;
      width: auto !important;
      max-width: 4rem;
    }
    :host(.bf-collapsed) ::ng-deep app-sidebar img + div {
      width: auto !important;
      margin: 0 !important;
      opacity: 1 !important;
    }
    :host(.bf-collapsed) ::ng-deep app-sidebar img + div span {
      font-size: 0.8rem;
      letter-spacing: 0.08em;
    }
  `,
})
export class LayoutShell implements OnInit {
  protected readonly layout = inject(LayoutService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly authService = inject(AuthService);
  private readonly brandingService = inject(BrandingService);

  // Expanded: the organization's logo and "BotFactory". Collapsed (80 px wide): the
  // vertical OGTIC logo, which fits that width, and "BF" underneath.
  readonly branding = computed<AppBranding>(() => {
    const collapsed = this.layout.isSidebarCollapsed();
    return {
      appName: collapsed ? APP_NAME_SHORT : APP_NAME,
      moduleName: '',
      logoUrl: collapsed ? COLLAPSED_LOGO_URL : this.brandingService.logoUrl(),
    };
  });

  constructor() {
    // ui-core AdminLayout only pushes [branding] into LayoutService once in
    // ngOnInit, so later logo uploads would not update the sidebar. Keep the
    // service signal in sync whenever our branding computed changes.
    effect(() => {
      this.layout.branding.set(this.branding());
    });
  }

  readonly menuItems = computed<SidebarMenuItem[]>(() => {
    const items: SidebarMenuItem[] = [
      {
        label: 'Dashboard',
        route: '/dashboard',
        iconPath:
          'M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-4 0a1 1 0 01-1-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 01-1 1',
      },
      {
        label: 'Configuración',
        route: '/settings',
        iconPath:
          'M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8z',
      },
    ];

    const perms = this.authService.user()?.permissions ?? [];

    if (
      perms.includes('tool:read') ||
      perms.includes('tool:update') ||
      perms.includes('bot:admin') ||
      perms.includes('superadmin')
    ) {
      items.splice(1, 0, {
        label: 'Herramientas',
        route: '/tools',
        iconPath:
          'M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z',
      });
    }

    if (
      perms.includes('audit:read') ||
      perms.includes('bot:admin') ||
      perms.includes('superadmin')
    ) {
      items.splice(2, 0, {
        label: 'Auditoría',
        route: '/audit-logs',
        iconPath:
          'M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z',
      });
    }
    if (perms.includes('superadmin') || perms.includes('bot:admin')) {
      items.push({
        label: 'Tareas',
        url: '/api/tasks',
        target: '_blank',
        iconPath:
          'M9 5H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2 M9 5a2 2 0 0 0 2 2h2a2 2 0 0 0 2-2 M9 5a2 2 0 0 1 2-2h2a2 2 0 0 0 2 2 M9 14l2 2 4-4',
      });
    }

    return items;
  });

  readonly userProfile = computed<UserProfile>(() => {
    const user = this.authService.user();
    return {
      name: user?.fullName ?? '',
      role: user?.organizationName ?? '',
    };
  });

  readonly profileMenuItems: ProfileMenuItem[] = [
    { label: 'Logout', action: 'logout', destructive: true },
  ];

  ngOnInit() {
    void this.brandingService.loadForAdmin();
    this.layout.profileMenuAction$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((action) => {
      if (action === 'logout') {
        this.authService.logout();
      }
    });
  }
}
