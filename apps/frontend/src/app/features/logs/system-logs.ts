import {
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { PageShellComponent, DropdownSearchComponent, LayoutService } from '@public-intelligence/ui-core';

import { Api } from '../../api/api';
import { ApiConfiguration } from '../../api/api-configuration';
import { getSystemLogs } from '../../api/functions';
import type { GetSystemLogs$Params } from '../../api/fn/system-logs/get-system-logs';
import type { SystemLogEntryResponse } from '../../api/models/system-log-entry-response';

const REFRESH_MS = 5000;

const LEVEL_OPTIONS = [
  { value: '', label: 'Todos los niveles' },
  { value: 'Information', label: 'Información y superior' },
  { value: 'Warning', label: 'Advertencias y errores' },
  { value: 'Error', label: 'Solo errores' },
];

const LEVEL_CLASSES: Record<string, string> = {
  Fatal: 'text-red-400 font-semibold',
  Error: 'text-red-400',
  Warning: 'text-amber-300',
  Information: 'text-neutral-300',
  Debug: 'text-neutral-500',
  Verbose: 'text-neutral-500',
};

// Serilog's own three-letter level names. The log lines are shown verbatim, in
// English, exactly as the server wrote them — only the surrounding page chrome
// is translated.
const LEVEL_LABELS: Record<string, string> = {
  Fatal: 'FTL',
  Error: 'ERR',
  Warning: 'WRN',
  Information: 'INF',
  Debug: 'DBG',
  Verbose: 'VRB',
};

const TIME_FORMAT: Intl.DateTimeFormatOptions = {
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hour12: false,
};

@Component({
  selector: 'bf-system-logs',
  imports: [PageShellComponent, DropdownSearchComponent],
  template: `
    <app-page-shell
      title="Registros"
      description="Registros técnicos del servidor de la API. Úsalos para diagnosticar errores junto con el soporte."
    >
      @if (forbidden()) {
        <div class="flex flex-col items-center justify-center py-32">
          <p class="text-sm text-neutral-600">No tienes permiso para ver los registros.</p>
        </div>
      } @else {
        <div class="mb-4 rounded-lg border border-neutral-200 bg-white p-4">
          <div class="flex w-full flex-wrap items-center gap-3">
            <app-dropdown-search
              class="min-w-[220px]"
              [options]="levelOptions"
              [value]="level() || null"
              placeholder="Todos los niveles"
              (valueChange)="onLevelChange($event)"
            ></app-dropdown-search>
            <input
              type="text"
              [value]="search()"
              (input)="onSearchInput($event)"
              placeholder="Buscar en los mensajes..."
              class="min-w-[220px] flex-1 rounded-lg border border-neutral-200 px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none"
            />
            <label class="flex cursor-pointer items-center gap-2 text-sm text-neutral-700">
              <input type="checkbox" [checked]="autoRefresh()" (change)="toggleAutoRefresh()" />
              Actualizar cada 5s
            </label>
            <button
              type="button"
              (click)="loadLogs()"
              class="rounded-lg border border-neutral-200 px-3 py-2 text-sm font-medium text-neutral-700 transition hover:bg-neutral-50"
            >
              Actualizar
            </button>
            <a
              [href]="downloadUrl"
              class="rounded-lg border border-neutral-200 px-3 py-2 text-sm font-medium text-neutral-700 transition hover:bg-neutral-50"
            >
              Descargar
            </a>
          </div>
        </div>

        @if (error()) {
          <div class="mb-4 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
            {{ error() }}
          </div>
        }

        <div class="rounded-lg border border-neutral-800 bg-neutral-950">
          <div #scrollBox class="max-h-[65vh] min-h-[300px] overflow-auto p-3 font-mono text-xs leading-5">
            @if (entries().length === 0) {
              <p class="py-8 text-center text-neutral-500">
                {{ loading() ? 'Cargando registros...' : 'No hay registros que coincidan.' }}
              </p>
            }
            @for (row of rows(); track row.key) {
              <div class="whitespace-pre-wrap break-all">
                <span class="text-neutral-500">{{ formatTime(row.entry.timestamp) }}</span>
                <span [class]="levelClass(row.entry.level)"> {{ levelLabel(row.entry.level) }} </span>
                <span class="text-neutral-300">{{ row.entry.message }}</span>
                @if (row.entry.category) {
                  <span class="text-neutral-600"> · {{ row.entry.category }}</span>
                }
              </div>
              @if (row.entry.exception) {
                <div class="mb-1 whitespace-pre-wrap break-all pl-6 text-red-300/80">
                  {{ row.entry.exception }}
                </div>
              }
            }
          </div>
          <div class="border-t border-neutral-800 px-3 py-2 text-xs text-neutral-500">
            {{ entries().length }} entradas · archivo {{ fileSizeLabel() }}
            @if (lastUpdated()) {
              · actualizado {{ lastUpdated() }}
            }
          </div>
        </div>
      }
    </app-page-shell>
  `,
})
export class SystemLogs implements OnInit {
  private readonly layout = inject(LayoutService);
  private readonly api = inject(Api);
  private readonly apiConfig = inject(ApiConfiguration);
  private readonly destroyRef = inject(DestroyRef);

  readonly scrollBox = viewChild<ElementRef<HTMLDivElement>>('scrollBox');

  readonly levelOptions = LEVEL_OPTIONS;
  readonly downloadUrl = `${this.apiConfig.rootUrl}/api/logs/raw?download=true`;

  readonly loading = signal(false);
  readonly forbidden = signal(false);
  readonly error = signal<string | null>(null);
  readonly entries = signal<SystemLogEntryResponse[]>([]);
  readonly fileSizeBytes = signal(0);
  readonly lastUpdated = signal('');

  readonly level = signal('');
  readonly search = signal('');
  readonly autoRefresh = signal(true);

  /// The list is a sliding tail, so `track $index` made index 0 point at a
  /// different entry after every poll and Angular rewrote all 500 rows. Key each
  /// row by its content instead; identical repeated lines get an ordinal suffix
  /// so the keys stay unique (@for rejects duplicates).
  readonly rows = computed(() => {
    const seen = new Map<string, number>();
    return this.entries().map((entry) => {
      const base = `${entry.timestamp}|${entry.level}|${entry.category ?? ''}|${entry.message}`;
      const n = seen.get(base) ?? 0;
      seen.set(base, n + 1);
      return { key: n === 0 ? base : `${base}#${n}`, entry };
    });
  });

  readonly fileSizeLabel = computed(() => {
    const bytes = this.fileSizeBytes();
    if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  });

  private searchDebounce: ReturnType<typeof setTimeout> | null = null;

  ngOnInit() {
    this.layout.setPageTitle('Registros');
    void this.loadLogs();

    const timer = setInterval(() => {
      if (this.autoRefresh() && !this.loading()) {
        void this.loadLogs();
      }
    }, REFRESH_MS);
    this.destroyRef.onDestroy(() => clearInterval(timer));
  }

  levelClass(level: string): string {
    return LEVEL_CLASSES[level] ?? 'text-neutral-300';
  }

  levelLabel(level: string): string {
    return LEVEL_LABELS[level] ?? level.toUpperCase();
  }

  /// Local time as "2026-08-07, 11:51:15". All-numeric on purpose: the old
  /// month:'short' under 'es' rendered August as "ago", so a line stamped 7
  /// August read as "7 ago" — "7 minutes ago" to anyone scanning a log tail.
  /// hour12 must be false explicitly; en-CA otherwise appends "a.m."/"p.m.".
  formatTime(iso: string): string {
    return new Date(iso).toLocaleString('en-CA', TIME_FORMAT);
  }

  onLevelChange(value: string | null) {
    this.level.set(value ?? '');
    void this.loadLogs();
  }

  onSearchInput(event: Event) {
    const value = (event.target as HTMLInputElement).value;
    this.search.set(value);
    if (this.searchDebounce) clearTimeout(this.searchDebounce);
    this.searchDebounce = setTimeout(() => void this.loadLogs(), 400);
  }

  toggleAutoRefresh() {
    this.autoRefresh.update((v) => !v);
  }

  async loadLogs() {
    this.loading.set(true);
    this.error.set(null);

    try {
      const params: GetSystemLogs$Params = { take: 500 };
      if (this.level()) params.level = this.level();
      if (this.search().trim()) params.search = this.search().trim();

      const result = await this.api.invoke(getSystemLogs, params);
      const box = this.scrollBox()?.nativeElement;
      const pinnedToBottom =
        !box || box.scrollHeight - box.scrollTop - box.clientHeight < 40;

      this.entries.set(result.entries ?? []);
      this.fileSizeBytes.set(Number(result.fileSizeBytes ?? 0));
      this.lastUpdated.set(new Date().toLocaleTimeString('es'));

      if (pinnedToBottom) {
        setTimeout(() => {
          const el = this.scrollBox()?.nativeElement;
          if (el) el.scrollTop = el.scrollHeight;
        });
      }
    } catch (err) {
      if (err instanceof HttpErrorResponse && err.status === 403) {
        this.forbidden.set(true);
      } else {
        this.error.set('Error al cargar los registros.');
      }
    } finally {
      this.loading.set(false);
    }
  }
}
