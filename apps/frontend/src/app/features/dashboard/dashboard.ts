import {
  Component,
  computed,
  inject,
  OnInit,
  signal,
  TemplateRef,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  PageShellComponent,
  PaginatedTableComponent,
  StatusPillComponent,
  FilterBarComponent,
  DropdownSearchComponent,
  LayoutService,
} from '@public-intelligence/ui-core';
import type { PaginatedColumnDef } from '@public-intelligence/ui-core';
import { Api } from '../../api/api';
import { getBots, deleteBot, getPublicUsage } from '../../api/functions';
import type { BotResponse } from '../../api/models/bot-response';
import type { PublicUsageResponse } from '../../api/models/public-usage-response';
import { AuthService } from '../../core/services/auth.service';
import { toast } from 'ngx-sonner';

const USAGE_VIEW_PERMISSIONS = new Set(['superadmin', 'bot:admin', 'telemetry:read']);

const STATUS_LABELS: Record<string, string> = {
  draft: 'Borrador',
  published: 'Publicado',
  inactive: 'Inactivo',
  Draft: 'Borrador',
  Published: 'Publicado',
  Inactive: 'Inactivo',
};

const isPublished = (state: string | null | undefined): boolean =>
  state?.toLowerCase() === 'published';
const isInactive = (state: string | null | undefined): boolean =>
  state?.toLowerCase() === 'inactive';

@Component({
  selector: 'bf-dashboard',
  imports: [
    RouterLink,
    PageShellComponent,
    PaginatedTableComponent,
    StatusPillComponent,
    FilterBarComponent,
    DropdownSearchComponent,
  ],
  template: `
    <app-page-shell title="Dashboard">
      <!-- TODO: !text-white needed because ui-core overrides anchor color — fix in design system -->
      <a
        actions
        routerLink="/bots/create"
        class="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium !text-white transition hover:bg-blue-700"
      >
        + Crear Bot
      </a>

      <!-- Stats Cards -->
      <div class="mb-8 grid grid-cols-1 gap-4 sm:grid-cols-3">
        <div class="rounded-xl border border-neutral-200 bg-white p-5">
          <p class="text-sm font-medium text-neutral-500">Total Bots</p>
          <p class="mt-1 text-3xl font-bold text-neutral-900">
            {{ loading() ? '—' : totalBots() }}
          </p>
        </div>
        <div class="rounded-xl border border-neutral-200 bg-white p-5">
          <p class="text-sm font-medium text-neutral-500">Publicados</p>
          <p class="mt-1 text-3xl font-bold text-neutral-900">
            {{ loading() ? '—' : publishedCount() }}
          </p>
        </div>
        <div class="rounded-xl border border-neutral-200 bg-white p-5">
          <p class="text-sm font-medium text-neutral-500">No Activos</p>
          <p class="mt-1 text-3xl font-bold text-neutral-900">
            {{ loading() ? '—' : inactiveCount() }}
          </p>
        </div>
      </div>

      <!-- Uso público (30 días) -->
      @if (canSeeUsage() && usage(); as u) {
        <div class="mb-8">
          <h3 class="mb-3 text-sm font-semibold text-neutral-900">
            Uso público (últimos 30 días)
          </h3>
          <div class="grid grid-cols-1 gap-4 sm:grid-cols-4">
            <div class="rounded-xl border border-neutral-200 bg-white p-5">
              <p class="text-sm font-medium text-neutral-500">Conversaciones públicas</p>
              <p class="mt-1 text-3xl font-bold text-neutral-900">{{ u.publicConversations }}</p>
              <p class="mt-1 text-xs text-neutral-400">
                {{ u.embeddedPublicConversations }} incrustadas · {{ u.directPublicConversations }} directas
              </p>
            </div>
            <div class="rounded-xl border border-neutral-200 bg-white p-5">
              <p class="text-sm font-medium text-neutral-500">Conversaciones autenticadas</p>
              <p class="mt-1 text-3xl font-bold text-neutral-900">{{ u.authenticatedConversations }}</p>
            </div>
            <div class="rounded-xl border border-neutral-200 bg-white p-5">
              <p class="text-sm font-medium text-neutral-500">Sesiones rechazadas</p>
              <p class="mt-1 text-3xl font-bold text-neutral-900">{{ u.rejectedSessions }}</p>
              <p class="mt-1 text-xs text-neutral-400">capacidad o voz no disponible</p>
            </div>
            <div class="rounded-xl border border-neutral-200 bg-white p-5">
              <p class="text-sm font-medium text-neutral-500">Límites de tasa</p>
              <p class="mt-1 text-3xl font-bold text-neutral-900">{{ u.rateLimitEvents }}</p>
              <p class="mt-1 text-xs text-neutral-400">solicitudes bloqueadas</p>
            </div>
          </div>

          @if (u.topEmbedOrigins.length > 0) {
            <div class="mt-4 rounded-xl border border-neutral-200 bg-white p-5">
              <p class="mb-3 text-sm font-medium text-neutral-500">
                Dominios que incrustan bots
              </p>
              <table class="w-full text-sm">
                <tbody>
                  @for (origin of u.topEmbedOrigins; track origin.origin) {
                    <tr class="border-t border-neutral-100">
                      <td class="py-2 font-mono text-xs text-neutral-700">{{ origin.origin }}</td>
                      <td class="py-2 text-right font-medium text-neutral-900">{{ origin.count }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </div>
      }

      @if (loading()) {
        <div class="flex items-center justify-center py-32">
          <div class="text-sm text-neutral-500">Cargando bots...</div>
        </div>
      } @else if (error()) {
        <div class="flex flex-col items-center justify-center py-32">
          <p class="mb-4 text-sm text-red-600">{{ error() }}</p>
          <button
            (click)="loadBots()"
            class="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-blue-700"
          >
            Reintentar
          </button>
        </div>
      } @else if (isEmpty()) {
        <!-- Empty State -->
        <div class="flex flex-col items-center justify-center py-32">
          <div class="mb-4 text-neutral-300">
            <svg
              class="h-16 w-16"
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
              stroke-width="1"
            >
              <path
                d="M12 8V4H8 M6 8h12a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2v-8a2 2 0 0 1 2-2z M2 14h2 M20 14h2 M15 13v2 M9 13v2"
              />
            </svg>
          </div>
          <h3 class="mb-2 text-lg font-semibold text-neutral-900">
            No hay bots creados
          </h3>
          <p class="mb-6 text-sm text-neutral-500">
            Crea tu primer bot para comenzar a configurar asistentes
            conversacionales.
          </p>
          <!-- TODO: !text-white needed because ui-core overrides anchor color — fix in design system -->
          <a
            routerLink="/bots/create"
            class="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium !text-white transition hover:bg-blue-700"
          >
            + Crear Bot
          </a>
        </div>
      } @else {
        <div>
          <app-filter-bar
            searchPlaceholder="Buscar bots..."
            [showDeletedToggle]="false"
            (searchQueryChange)="onSearch($event)"
          >
            <div advanced-filters>
              @for (trackId of [statusFilter()]; track trackId) {
                <app-dropdown-search
                  class="block w-full"
                  [options]="statusOptions"
                  [value]="statusFilter()"
                  placeholder="Estado: Todos"
                  (valueChange)="onStatusFilterChange($event)"
                ></app-dropdown-search>
              }
            </div>
          </app-filter-bar>

          <app-paginated-table
            class="mt-4 block"
            [data]="tableData()"
            [columns]="columns"
          ></app-paginated-table>

          <p class="mt-2 text-sm text-neutral-500">
            Mostrando {{ tableData().page }} de {{ tableData().totalPages }} paginas.
          </p>
        </div>
      }
    </app-page-shell>

    <!-- Name Column Template -->
    <ng-template #nameTpl let-item>
      <a
        [routerLink]="['/bots', item.id, 'edit']"
        class="inline-flex items-center gap-1.5 font-medium text-neutral-900 transition hover:text-blue-600"
      >
        {{ item.name }}
        <svg
          class="h-3.5 w-3.5"
          fill="none"
          viewBox="0 0 24 24"
          stroke="currentColor"
          stroke-width="2"
        >
          <path
            d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"
          />
          <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
        </svg>
      </a>
    </ng-template>

    <!-- Slug Column Template -->
    <ng-template #slugTpl let-item>
      <a
        [href]="'/chat/' + item.slug"
        target="_blank"
        class="inline-flex items-center gap-1.5 text-sm text-neutral-500 transition hover:text-blue-600"
      >
        {{ item.slug }}
        <svg
          class="h-3.5 w-3.5"
          fill="none"
          viewBox="0 0 24 24"
          stroke="currentColor"
          stroke-width="2"
        >
          <path
            d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6M15 3h6v6M10 14L21 3"
          />
        </svg>
      </a>
    </ng-template>

    <!-- Status Column Template -->
    <ng-template #statusTpl let-item>
      <app-status-pill
        [status]="item.status"
        type="publication"
      ></app-status-pill>
    </ng-template>

    <!-- Actions Column Template -->
    <ng-template #actionsTpl let-item>
      <div class="flex items-center gap-2">
        <a
          [routerLink]="['/bots', item.id, 'edit']"
          class="rounded p-1 text-neutral-400 transition hover:bg-neutral-100 hover:text-blue-600"
        >
          <svg
            class="h-4 w-4"
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
            stroke-width="2"
          >
            <path
              d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"
            />
            <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
          </svg>
        </a>
        <button
          (click)="onDelete(item.id)"
          class="rounded p-1 text-neutral-400 transition hover:text-red-600"
        >
          <svg
            class="h-4 w-4"
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
    </ng-template>
  `,
})
export class Dashboard implements OnInit {
  private readonly layout = inject(LayoutService);
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);

  readonly nameTpl = viewChild<TemplateRef<unknown>>('nameTpl');
  readonly slugTpl = viewChild<TemplateRef<unknown>>('slugTpl');
  readonly statusTpl = viewChild<TemplateRef<unknown>>('statusTpl');
  readonly actionsTpl = viewChild<TemplateRef<unknown>>('actionsTpl');

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly bots = signal<BotResponse[]>([]);
  readonly page = signal(1);
  readonly pageSize = signal(10);
  readonly totalItems = signal(0);
  readonly totalPages = signal(1);
  readonly searchQuery = signal('');
  readonly statusFilter = signal('all');

  readonly totalBots = computed(() => this.totalItems());
  readonly hasClientFilters = computed(
    () => !!this.searchQuery() || this.statusFilter() !== 'all',
  );
  readonly isEmpty = computed(
    () =>
      !this.loading() &&
      this.bots().length === 0 &&
      !this.hasClientFilters(),
  );

  readonly publishedCount = computed(
    () => this.bots().filter(b => isPublished(b.publishedState)).length,
  );
  readonly inactiveCount = computed(
    () => this.bots().filter(b => isInactive(b.publishedState)).length,
  );

  readonly usage = signal<PublicUsageResponse | null>(null);
  readonly canSeeUsage = computed(() => {
    const perms = this.auth.user()?.permissions ?? [];
    return perms.some(p => USAGE_VIEW_PERMISSIONS.has(p));
  });

  readonly filteredBots = computed(() => {
    const status = this.statusFilter();
    let bots = this.bots();
    if (status !== 'all') {
      bots = bots.filter(
        bot => bot.publishedState?.toLowerCase() === status,
      );
    }
    const query = this.searchQuery().toLowerCase();
    if (!query) return bots;
    return bots.filter(
      bot =>
        bot.botName.toLowerCase().includes(query) ||
        bot.urlSlug.toLowerCase().includes(query),
    );
  });

  readonly tableData = computed(() => {
    const bots = this.filteredBots();
    return {
      items: bots.map(bot => ({
        id: bot.id,
        name: bot.botName,
        slug: bot.urlSlug,
        status: STATUS_LABELS[bot.publishedState] ?? bot.publishedState,
        updated: new Date(bot.updatedAt).toLocaleDateString('es', {
          month: 'short',
          day: 'numeric',
        }),
      })),
      page: this.page(),
      pageSize: this.pageSize(),
      totalItems: this.hasClientFilters() ? bots.length : this.totalItems(),
      totalPages: this.hasClientFilters() ? 1 : this.totalPages(),
    };
  });

  columns: PaginatedColumnDef[] = [];

  readonly statusOptions = [
    { value: 'all', label: 'Todos' },
    { value: 'published', label: 'Publicado' },
    { value: 'draft', label: 'Borrador' },
    { value: 'inactive', label: 'Inactivo' },
  ];

  ngOnInit() {
    this.layout.setPageTitle('Dashboard');

    this.columns = [
      { header: 'Nombre', template: this.nameTpl() },
      { header: 'URL Slug', template: this.slugTpl() },
      { header: 'Estado', template: this.statusTpl(), width: '120px' },
      { header: 'Actualizado', field: 'updated', width: '120px' },
      {
        header: 'Acciones',
        template: this.actionsTpl(),
        width: '100px',
        align: 'center',
      },
    ];

    this.loadBots();
    if (this.canSeeUsage()) {
      void this.loadPublicUsage();
    }
  }

  private async loadPublicUsage() {
    try {
      this.usage.set(await this.api.invoke(getPublicUsage, {}));
    } catch {
      // The panel simply doesn't render; the dashboard stays functional.
      this.usage.set(null);
    }
  }

  onSearch(query: string) {
    this.searchQuery.set(query);
  }

  onStatusFilterChange(value: string | null) {
    this.statusFilter.set(value ?? 'all');
  }

  async loadBots() {
    this.loading.set(true);
    this.error.set(null);

    try {
      const result = await this.api.invoke(getBots, {
        page: this.page(),
        pageSize: this.pageSize(),
      });
      this.bots.set(result.items);
      this.totalItems.set(Number(result.totalItems));
      this.totalPages.set(Number(result.totalPages));
    } catch {
      this.error.set('Error al cargar los bots. Verifica tu conexión.');
    } finally {
      this.loading.set(false);
    }
  }

  async onDelete(id: number) {
    if (!confirm('¿Estás seguro de que deseas eliminar este bot?')) return;

    try {
      await this.api.invoke(deleteBot, { id });
      toast.info('Bot eliminado correctamente.');
      await this.loadBots();
    } catch {
      toast.error('Error al eliminar el bot.');
    }
  }
}
