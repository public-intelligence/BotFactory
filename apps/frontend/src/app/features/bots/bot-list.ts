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
import { getBots, deleteBot } from '../../api/functions';
import type { BotResponse } from '../../api/models/bot-response';

const STATUS_LABELS: Record<string, string> = {
  draft: 'Borrador',
  published: 'Publicado',
  inactive: 'Inactivo',
  Draft: 'Borrador',
  Published: 'Publicado',
  Inactive: 'Inactivo',
};

@Component({
  selector: 'bf-bot-list',
  imports: [
    RouterLink,
    PageShellComponent,
    PaginatedTableComponent,
    StatusPillComponent,
    FilterBarComponent,
    DropdownSearchComponent,
  ],
  template: `
    <app-page-shell title="Bots">
      <!-- TODO: !text-white needed because ui-core overrides anchor color — fix in design system -->
      <a
        actions
        routerLink="/bots/create"
        class="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium !text-white transition hover:bg-blue-700"
      >
        + Crear Bot
      </a>

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
        <ng-container>
          <!-- Filters -->
          <div filters>
            <app-filter-bar
              searchPlaceholder="Buscar bots..."
              [showDeletedToggle]="false"
            >
              <div advanced-filters>
                <app-dropdown-search
                  class="block w-full"
                  [options]="statusOptions"
                  placeholder="Estado: Todos"
                ></app-dropdown-search>
              </div>
            </app-filter-bar>
          </div>

          <!-- Table -->
          <app-paginated-table
            [data]="tableData()"
            [columns]="columns"
          ></app-paginated-table>

          <p class="mt-2 text-sm text-neutral-500">
            Mostrando {{ tableData().page }} de {{ tableData().totalPages }} paginas.
          </p>
        </ng-container>
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
export class BotList implements OnInit {
  private readonly layout = inject(LayoutService);
  private readonly api = inject(Api);

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

  readonly isEmpty = computed(() => !this.loading() && this.bots().length === 0);

  readonly tableData = computed(() => ({
    items: this.bots().map(bot => ({
      id: bot.id,
      name: bot.botName,
      slug: bot.urlSlug,
      status: STATUS_LABELS[bot.publishedState] ?? bot.publishedState,
      updated: new Date(bot.updatedAt).toLocaleDateString('es', { month: 'short', day: 'numeric' }),
    })),
    page: this.page(),
    pageSize: this.pageSize(),
    totalItems: this.totalItems(),
    totalPages: this.totalPages(),
  }));

  columns: PaginatedColumnDef[] = [];

  readonly statusOptions = [
    { value: 'all', label: 'Todos' },
    { value: 'published', label: 'Publicado' },
    { value: 'draft', label: 'Borrador' },
    { value: 'inactive', label: 'Inactivo' },
  ];

  ngOnInit() {
    this.layout.setPageTitle('Bots');

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
      await this.loadBots();
    } catch {
      this.error.set('Error al eliminar el bot.');
    }
  }
}
