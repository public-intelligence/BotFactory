import { Component, computed, inject, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import {
  PageShellComponent,
  StatusPillComponent,
  LayoutService,
} from '@public-intelligence/ui-core';
import { Api } from '../../api/api';
import { deleteBot, getBot, publishBot, unpublishBot } from '../../api/functions';
import { AuthService } from '../../core/services/auth.service';
import { BotEditStore } from './bot-edit.store';
import { toast } from 'ngx-sonner';

// Canonical backend values are lowercase (`draft` / `published`). PascalCase
// aliases are kept for safety in case any older record still uses them.
const STATUS_LABELS: Record<string, string> = {
  draft: 'Borrador',
  published: 'Publicado',
  inactive: 'Inactivo',
  Draft: 'Borrador',
  Published: 'Publicado',
  Inactive: 'Inactivo',
};

const PUBLISH_PERMISSIONS = new Set([
  'superadmin',
  'bot:admin',
  'bot:publish',
]);

@Component({
  selector: 'bf-bot-edit',
  imports: [
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    PageShellComponent,
    StatusPillComponent,
  ],
  providers: [BotEditStore],
  template: `
    @if (store.loading()) {
      <app-page-shell title="Cargando...">
        <div class="flex items-center justify-center py-32">
          <div class="text-sm text-neutral-500">Cargando bot...</div>
        </div>
      </app-page-shell>
    } @else if (store.error()) {
      <app-page-shell title="Error">
        <div class="flex flex-col items-center justify-center py-32">
          <p class="mb-4 text-sm text-red-600">{{ store.error() }}</p>
          <a
            routerLink="/dashboard"
            class="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-blue-700"
          >
            Volver al Dashboard
          </a>
        </div>
      </app-page-shell>
    } @else if (store.bot(); as bot) {
      <app-page-shell [title]="bot.botName">
        <nav breadcrumbs>
          <ol class="flex items-center gap-1 text-sm text-neutral-500">
            <li>
              <a routerLink="/dashboard" class="hover:text-neutral-900">Bots</a>
            </li>
            <li class="text-neutral-300">›</li>
            <li class="text-neutral-900">{{ bot.botName }}</li>
          </ol>
        </nav>

        <span status>
          <app-status-pill
            [status]="statusLabel()"
            type="publication"
          ></app-status-pill>
        </span>

        <div actions class="flex items-center gap-2">
          <button
            (click)="onTest()"
            class="rounded-lg border border-neutral-200 px-3 py-1.5 text-sm font-medium text-neutral-700 transition hover:bg-neutral-50"
          >
            ⚡ Probar
          </button>

          @if (canPublish()) {
            @if (isPublished()) {
              <button
                (click)="onUnpublish()"
                [disabled]="store.publishing()"
                class="rounded-lg border border-amber-500 px-3 py-1.5 text-sm font-medium text-amber-600 transition hover:bg-amber-50 disabled:opacity-50"
              >
                {{ store.publishing() ? 'Desactivando…' : 'Desactivar' }}
              </button>
            } @else {
              <button
                (click)="onPublish()"
                [disabled]="store.publishing()"
                class="rounded-lg bg-emerald-600 px-3 py-1.5 text-sm font-medium text-white transition hover:bg-emerald-700 disabled:opacity-50"
              >
                {{ store.publishing() ? 'Publicando…' : 'Publicar' }}
              </button>
            }
          }

          <button
            (click)="onDelete()"
            aria-label="Eliminar bot"
            class="rounded p-1.5 text-neutral-400 transition hover:bg-neutral-100 hover:text-red-600"
          >
            <svg
              aria-hidden="true"
              class="h-5 w-5"
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
              stroke-width="2"
            >
              <path
                d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"
              />
            </svg>
          </button>
        </div>

        <!-- Tabs -->
        <div class="mb-6 border-b border-neutral-200">
          <nav class="-mb-px flex gap-6">
            @for (tab of tabs(); track tab.id) {
              <a
                [routerLink]="tab.id"
                routerLinkActive="border-neutral-900 text-neutral-900"
                [routerLinkActiveOptions]="{ exact: true }"
                class="border-b-2 border-transparent pb-3 text-sm font-medium text-neutral-500 transition hover:text-neutral-700"
              >
                {{ tab.label }}
              </a>
            }
          </nav>
        </div>

        <router-outlet />
      </app-page-shell>
    }
  `,
})
export class BotEdit implements OnInit {
  private readonly layout = inject(LayoutService);
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly store = inject(BotEditStore);

  readonly statusLabel = computed(() => {
    const bot = this.store.bot();
    if (!bot) return '';
    return STATUS_LABELS[bot.publishedState] ?? bot.publishedState;
  });

  readonly isPublished = computed(() => {
    const bot = this.store.bot();
    return bot?.publishedState?.toLowerCase() === 'published';
  });

  readonly canPublish = computed(() => {
    const perms = this.auth.user()?.permissions ?? [];
    return perms.some((p) => PUBLISH_PERMISSIONS.has(p));
  });

  readonly tabs = computed(() => {
    const base = [
      { id: 'general', label: 'General' },
      { id: 'apariencia', label: 'Apariencia' },
      { id: 'voz', label: 'Voz' },
      { id: 'kb', label: 'Base de Conocimiento' },
      { id: 'debug', label: 'Debug KB' },
    ];
    const perms = this.auth.user()?.permissions ?? [];
    if (perms.includes('bot:update') || perms.includes('superadmin') || perms.includes('bot:admin')) {
      base.push({ id: 'herramientas', label: 'Herramientas' });
    }
    if (perms.includes('conversation:read') || perms.includes('superadmin') || perms.includes('bot:admin')) {
      base.push({ id: 'chat-history', label: 'Historial de Chat' });
    }
    return base;
  });

  ngOnInit() {
    const id = Number(this.route.snapshot.params['id']);
    this.loadBot(id);
  }

  private async loadBot(id: number) {
    this.store.loading.set(true);
    this.store.error.set(null);

    try {
      const bot = await this.api.invoke(getBot, { id });
      this.store.bot.set(bot);
      this.layout.setPageTitle(bot.botName);
    } catch {
      this.store.error.set('No se pudo cargar el bot.');
    } finally {
      this.store.loading.set(false);
    }
  }

  async onPublish() {
    const bot = this.store.bot();
    if (!bot) return;

    this.store.publishing.set(true);
    try {
      const updated = await this.api.invoke(publishBot, { id: bot.id });
      this.store.bot.set(updated);
      toast.success('Bot publicado. Ya está disponible en su URL pública.');
    } catch {
      toast.error('No se pudo publicar el bot.');
    } finally {
      this.store.publishing.set(false);
    }
  }

  async onUnpublish() {
    const bot = this.store.bot();
    if (!bot) return;
    if (
      !confirm(
        `¿Desactivar "${bot.botName}"? Dejará de ser accesible públicamente.`,
      )
    )
      return;

    this.store.publishing.set(true);
    try {
      const updated = await this.api.invoke(unpublishBot, { id: bot.id });
      this.store.bot.set(updated);
      toast.info('Bot desactivado. Ya no es accesible públicamente.');
    } catch {
      toast.error('No se pudo desactivar el bot.');
    } finally {
      this.store.publishing.set(false);
    }
  }

  onTest() {
    const bot = this.store.bot();
    if (bot?.urlSlug) {
      window.open(`/chat/${bot.urlSlug}`, '_blank');
    }
  }

  async onDelete() {
    const bot = this.store.bot();
    if (!bot || !confirm(`¿Estás seguro de que deseas eliminar "${bot.botName}"?`)) return;

    try {
      await this.api.invoke(deleteBot, { id: bot.id });
      toast.info('Bot eliminado correctamente.');
      await this.router.navigate(['/dashboard']);
    } catch {
      toast.error('Error al eliminar el bot.');
    }
  }
}
