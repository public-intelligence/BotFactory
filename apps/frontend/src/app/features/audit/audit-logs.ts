import {
  Component,
  computed,
  inject,
  OnInit,
  signal,
  TemplateRef,
  viewChild,
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import {
  PageShellComponent,
  PaginatedTableComponent,
  DropdownSearchComponent,
  LayoutService,
} from '@public-intelligence/ui-core';
import type { PaginatedColumnDef } from '@public-intelligence/ui-core';

import { Api } from '../../api/api';
import { getAuditLogs } from '../../api/functions';
import type { GetAuditLogs$Params } from '../../api/fn/audit-logs/get-audit-logs';
import type { AuditLogResponse } from '../../api/models/audit-log-response';
import { AuditLogDetailDrawer } from './audit-log-detail-drawer';
import {
  formatDateInput,
  matchesDatePreset,
  presetDaysButtonClass,
} from '../../shared/filter-ui';

const ENTITY_LABELS: Record<string, string> = {
  Bot: 'Bot',
  AppSetting: 'Configuración',
  KbDocument: 'Documento KB',
};

const ACTION_LABELS: Record<string, string> = {
  Added: 'Creado',
  Modified: 'Modificado',
  Deleted: 'Eliminado',
};

const ENTITY_TYPE_OPTIONS = [
  { value: '', label: 'Todos los tipos' },
  { value: 'Bot', label: 'Bot' },
  { value: 'AppSetting', label: 'Configuración' },
  { value: 'KbDocument', label: 'Documento KB' },
];

function defaultDateRange(): { from: string; to: string } {
  const to = new Date();
  const from = new Date();
  from.setDate(from.getDate() - 30);
  return { from: formatDateInput(from), to: formatDateInput(to) };
}

function toUtcStartOfDay(dateStr: string): string {
  const [y, m, d] = dateStr.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d, 0, 0, 0, 0)).toISOString();
}

function toUtcEndOfDay(dateStr: string): string {
  const [y, m, d] = dateStr.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d, 23, 59, 59, 999)).toISOString();
}

type AuditTableRow = AuditLogResponse & {
  _entityLabel: string;
  _actionLabel: string;
  _date: string;
};

@Component({
  selector: 'bf-audit-logs',
  imports: [
    PageShellComponent,
    PaginatedTableComponent,
    DropdownSearchComponent,
    AuditLogDetailDrawer,
  ],
  template: `
    <app-page-shell
      title="Auditoría"
      description="Registro de cambios en bots, configuración y documentos de la base de conocimiento."
    >
      @if (forbidden()) {
        <div class="flex flex-col items-center justify-center py-32">
          <p class="text-sm text-neutral-600">
            No tienes permiso para ver la auditoría.
          </p>
        </div>
      } @else if (error()) {
        <div class="flex flex-col items-center justify-center py-32">
          <p class="mb-4 text-sm text-red-600">{{ error() }}</p>
          <button
            type="button"
            (click)="loadLogs()"
            class="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-blue-700"
          >
            Reintentar
          </button>
        </div>
      } @else {
        <div class="mb-4 rounded-lg border border-neutral-200 bg-white p-4">
          <div class="flex w-full flex-wrap items-end gap-3">
              <div class="flex flex-col gap-1">
                <label class="text-xs font-medium text-neutral-500">Desde</label>
                <input
                  type="date"
                  [value]="dateFrom()"
                  (change)="onDateFromChange($event)"
                  class="rounded-lg border border-neutral-200 px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none"
                />
              </div>
              <div class="flex flex-col gap-1">
                <label class="text-xs font-medium text-neutral-500">Hasta</label>
                <input
                  type="date"
                  [value]="dateTo()"
                  (change)="onDateToChange($event)"
                  class="rounded-lg border border-neutral-200 px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none"
                />
              </div>
              <div class="flex gap-2 pb-0.5">
                <button
                  type="button"
                  (click)="applyPresetDays(7)"
                  [class]="presetDaysButtonClass(isPreset7())"
                  [attr.aria-pressed]="isPreset7()"
                >
                  7 días
                </button>
                <button
                  type="button"
                  (click)="applyPresetDays(30)"
                  [class]="presetDaysButtonClass(isPreset30())"
                  [attr.aria-pressed]="isPreset30()"
                >
                  30 días
                </button>
              </div>
              @for (trackId of [entityType()]; track trackId) {
                <app-dropdown-search
                  class="min-w-[180px]"
                  [options]="entityTypeOptions"
                  [value]="entityType() || null"
                  placeholder="Todos los tipos"
                  (valueChange)="onEntityTypeChange($event)"
                ></app-dropdown-search>
              }
          </div>
        </div>

        @if (loading()) {
          <div class="flex items-center justify-center py-32">
            <p class="text-sm text-neutral-400">Cargando registros...</p>
          </div>
        } @else if (isEmpty()) {
          <div class="flex flex-col items-center justify-center py-32">
            <p class="text-sm text-neutral-600">
              No hay registros de auditoría
            </p>
            @if (hasActiveFilters()) {
              <p class="mt-2 text-xs text-neutral-500">
                Prueba ampliar el rango de fechas o quitar filtros.
              </p>
            }
          </div>
        } @else {
          <app-paginated-table
            class="mt-4 block"
            [data]="tableData()"
            [columns]="columns"
            (pageChange)="onPageChange($event)"
          ></app-paginated-table>
        }
      }
    </app-page-shell>

    <ng-template #dateTpl let-item>
      {{ formatDate(item.createdAt) }}
    </ng-template>

    <ng-template #entityTypeTpl let-item>
      {{ item._entityLabel }}
    </ng-template>

    <ng-template #actionTpl let-item>
      {{ item._actionLabel }}
    </ng-template>

    <ng-template #changesTpl let-item>
      {{ item.changes.length }}
    </ng-template>

    <ng-template #actionsTpl let-item>
      <button
        type="button"
        (click)="openDrawer(item)"
        class="text-sm font-medium text-blue-600 transition hover:underline"
      >
        Ver
      </button>
    </ng-template>

    <bf-audit-log-detail-drawer
      [open]="drawerOpen()"
      [log]="selectedLog()"
      (closed)="closeDrawer()"
    />
  `,
})
export class AuditLogs implements OnInit {
  private readonly layout = inject(LayoutService);
  private readonly api = inject(Api);

  readonly dateTpl = viewChild<TemplateRef<unknown>>('dateTpl');
  readonly entityTypeTpl = viewChild<TemplateRef<unknown>>('entityTypeTpl');
  readonly actionTpl = viewChild<TemplateRef<unknown>>('actionTpl');
  readonly changesTpl = viewChild<TemplateRef<unknown>>('changesTpl');
  readonly actionsTpl = viewChild<TemplateRef<unknown>>('actionsTpl');

  readonly entityTypeOptions = ENTITY_TYPE_OPTIONS;

  readonly loading = signal(false);
  readonly forbidden = signal(false);
  readonly error = signal<string | null>(null);
  readonly drawerOpen = signal(false);
  readonly selectedLog = signal<AuditLogResponse | null>(null);

  readonly dateFrom = signal(defaultDateRange().from);
  readonly dateTo = signal(defaultDateRange().to);
  readonly entityType = signal('');
  readonly page = signal(1);

  readonly tableData = signal({
    items: [] as AuditTableRow[],
    page: 1,
    pageSize: 20,
    totalItems: 0,
    totalPages: 0,
  });

  readonly isEmpty = computed(
    () => !this.loading() && this.tableData().items.length === 0,
  );

  readonly isPreset7 = computed(() =>
    matchesDatePreset(this.dateFrom(), this.dateTo(), 7),
  );

  readonly isPreset30 = computed(() =>
    matchesDatePreset(this.dateFrom(), this.dateTo(), 30),
  );

  readonly presetDaysButtonClass = presetDaysButtonClass;

  columns: PaginatedColumnDef[] = [];

  ngOnInit() {
    this.layout.setPageTitle('Auditoría');

    this.columns = [
      { header: 'Fecha', template: this.dateTpl(), width: '170px' },
      { header: 'Usuario', field: 'userName', width: '140px' },
      { header: 'Acción', template: this.actionTpl(), width: '110px' },
      { header: 'Tipo', template: this.entityTypeTpl(), width: '130px' },
      { header: 'Entidad', field: 'entityId', width: '90px', align: 'center' },
      {
        header: 'Cambios',
        template: this.changesTpl(),
        width: '90px',
        align: 'center',
      },
      {
        header: '',
        template: this.actionsTpl(),
        width: '70px',
        align: 'center',
      },
    ];

    void this.loadLogs();
  }

  hasActiveFilters(): boolean {
    return (
      this.entityType() !== '' ||
      this.dateFrom() !== defaultDateRange().from ||
      this.dateTo() !== defaultDateRange().to
    );
  }

  formatDate(iso: string): string {
    return new Date(iso).toLocaleString('es', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  onDateFromChange(event: Event) {
    const value = (event.target as HTMLInputElement).value;
    if (value) {
      this.dateFrom.set(value);
      this.resetAndLoad();
    }
  }

  onDateToChange(event: Event) {
    const value = (event.target as HTMLInputElement).value;
    if (value) {
      this.dateTo.set(value);
      this.resetAndLoad();
    }
  }

  applyPresetDays(days: number) {
    const to = new Date();
    const from = new Date();
    from.setDate(from.getDate() - days);
    this.dateFrom.set(formatDateInput(from));
    this.dateTo.set(formatDateInput(to));
    this.resetAndLoad();
  }

  onEntityTypeChange(value: string | null) {
    this.entityType.set(value ?? '');
    this.resetAndLoad();
  }

  onPageChange(page: number) {
    this.page.set(page);
    void this.loadLogs();
  }

  openDrawer(item: AuditLogResponse) {
    this.selectedLog.set(item);
    this.drawerOpen.set(true);
  }

  closeDrawer() {
    this.drawerOpen.set(false);
    this.selectedLog.set(null);
  }

  private resetAndLoad() {
    this.page.set(1);
    void this.loadLogs();
  }

  async loadLogs() {
    this.loading.set(true);
    this.error.set(null);

    try {
      const params: GetAuditLogs$Params = {
        from: toUtcStartOfDay(this.dateFrom()),
        to: toUtcEndOfDay(this.dateTo()),
        page: this.page(),
        pageSize: 20,
      };

      const type = this.entityType();
      if (type) {
        params.entityType = type;
      }

      const result = await this.api.invoke(getAuditLogs, params);
      this.tableData.set({
        items: result.items.map((item) => ({
          ...item,
          _entityLabel: ENTITY_LABELS[item.entityType] ?? item.entityType,
          _actionLabel: ACTION_LABELS[item.action] ?? item.action,
          _date: this.formatDate(item.createdAt),
        })),
        page: Number(result.page),
        pageSize: Number(result.pageSize),
        totalItems: Number(result.totalItems),
        totalPages: Number(result.totalPages),
      });
    } catch (err) {
      if (err instanceof HttpErrorResponse && err.status === 403) {
        this.forbidden.set(true);
      } else {
        this.error.set('Error al cargar los registros de auditoría.');
      }
    } finally {
      this.loading.set(false);
    }
  }
}
