import {
  Component,
  ElementRef,
  OnDestroy,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import {
  StatusPillComponent,
} from '@public-intelligence/ui-core';

import { BotEditStore } from './bot-edit.store';
import { Api } from '../../api/api';
import {
  getKbDocuments,
  uploadKbDocument,
  createKbDocumentsFromUrls,
  deleteKbDocument,
  reindexKbDocument,
  bulkReindexKbDocuments,
  bulkDeleteKbDocuments,
  getKbDocumentContent,
} from '../../api/functions';
import type { BulkKbDocumentsRequest } from '../../api/models/bulk-kb-documents-request';
import type { KbDocumentResponse } from '../../api/models/kb-document-response';
import { toast } from 'ngx-sonner';
import { HttpErrorResponse } from '@angular/common/http';
import * as XLSX from 'xlsx';

function extractApiErrorMessage(err: unknown): string | null {
  if (!(err instanceof HttpErrorResponse)) return null;
  const body = err.error;
  if (typeof body === 'string' && body.trim()) return body.trim();
  if (body && typeof body === 'object' && typeof (body as { error?: unknown }).error === 'string') {
    return (body as { error: string }).error;
  }
  return null;
}

const STATUS_MAP: Record<string, string> = {
  Pending: 'Pendiente',
  Indexed: 'Publicado',
  Failed: 'Error',
};

const SOURCE_MAP: Record<string, string> = {
  File: 'Archivo',
  Url: 'URL',
};

const MAX_URLS = 5000;
const URL_PREVIEW_LIMIT = 200;
const PAGE_SIZE_OPTIONS = [10, 25, 50, 100] as const;
const DEFAULT_PAGE_SIZE = 25;

type KbSortBy = 'filename' | 'sourceType' | 'indexedAt' | 'status' | 'createdAt';
type KbDocRow = KbDocumentResponse & {
  status: string;
  indexedAt: string;
  sourceLabel: string;
  errorMessage: string | null;
};

@Component({
  selector: 'bf-bot-edit-kb',
  imports: [StatusPillComponent],
  template: `
    <div class="space-y-6">
      <section>
        <h3 class="mb-2 text-base font-semibold text-neutral-900">
          Documentos
        </h3>
        <p class="mb-4 text-sm text-neutral-500">
          Agrega archivos .txt o URLs para usarlos como base de conocimiento.
          Los documentos se indexaran automaticamente.
        </p>

        <div class="mb-4 flex flex-wrap items-center gap-3">
          <div class="min-w-[12rem] flex-1">
            <input
              type="text"
              placeholder="Buscar por URL o nombre..."
              class="w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900 placeholder:text-neutral-400 focus:border-neutral-400 focus:outline-none"
              [value]="searchInput()"
              (input)="onSearchInput($event)"
            />
          </div>
          <label class="flex items-center gap-2 text-sm text-neutral-600">
            <span class="whitespace-nowrap">Estado</span>
            <select
              class="rounded-lg border border-neutral-200 bg-white px-2 py-2 text-sm text-neutral-800 focus:border-neutral-400 focus:outline-none"
              [value]="statusFilter()"
              (change)="onStatusFilterChange($event)"
            >
              <option value="">Todos</option>
              <option value="Indexed">Publicado</option>
              <option value="Failed">Error</option>
              <option value="Pending">Pendiente</option>
            </select>
          </label>
          <label class="flex items-center gap-2 text-sm text-neutral-600">
            <span class="whitespace-nowrap">Tipo</span>
            <select
              class="rounded-lg border border-neutral-200 bg-white px-2 py-2 text-sm text-neutral-800 focus:border-neutral-400 focus:outline-none"
              [value]="sourceTypeFilter()"
              (change)="onSourceTypeFilterChange($event)"
            >
              <option value="">Todos</option>
              <option value="Url">URL</option>
              <option value="File">Archivo</option>
            </select>
          </label>
          <button
            type="button"
            (click)="openModal()"
            [disabled]="uploading()"
            class="rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white transition hover:bg-blue-700 disabled:opacity-50"
          >
            {{ uploading() ? 'Procesando...' : '+ Agregar' }}
          </button>
        </div>

        @if (documentsData().totalItems > 0 || selectedCount() > 0) {
          <div class="mb-3 flex flex-wrap items-center justify-between gap-3 rounded-lg border border-neutral-200 bg-neutral-50 px-3 py-2">
            <p class="text-sm text-neutral-700">
              @if (selectedCount() > 0) {
                <span class="font-medium">{{ selectedCount() }}</span> seleccionados
              } @else {
                <span class="font-medium">{{ documentsData().totalItems }}</span> resultados
                @if (hasActiveFilters()) {
                  <span class="text-neutral-500"> (filtrados)</span>
                }
              }
            </p>
            <div class="flex flex-wrap items-center gap-2">
              @if (selectedCount() > 0) {
                <button
                  type="button"
                  (click)="clearSelection()"
                  class="rounded-lg px-3 py-1.5 text-sm font-medium text-neutral-600 transition hover:bg-white"
                  [disabled]="bulkBusy()"
                >
                  Limpiar selección
                </button>
                <button
                  type="button"
                  (click)="bulkReindexSelected()"
                  class="rounded-lg border border-neutral-200 bg-white px-3 py-1.5 text-sm font-medium text-neutral-800 transition hover:bg-neutral-50 disabled:opacity-50"
                  [disabled]="bulkBusy()"
                >
                  Reindexar
                </button>
                <button
                  type="button"
                  (click)="bulkDeleteSelected()"
                  class="rounded-lg border border-red-200 bg-white px-3 py-1.5 text-sm font-medium text-red-600 transition hover:bg-red-50 disabled:opacity-50"
                  [disabled]="bulkBusy()"
                >
                  Eliminar
                </button>
              } @else {
                <button
                  type="button"
                  (click)="bulkReindexFiltered()"
                  class="rounded-lg border border-neutral-200 bg-white px-3 py-1.5 text-sm font-medium text-neutral-800 transition hover:bg-neutral-50 disabled:opacity-50"
                  [disabled]="bulkBusy() || documentsData().totalItems === 0"
                >
                  Reindexar todos
                </button>
                <button
                  type="button"
                  (click)="bulkDeleteFiltered()"
                  class="rounded-lg border border-red-200 bg-white px-3 py-1.5 text-sm font-medium text-red-600 transition hover:bg-red-50 disabled:opacity-50"
                  [disabled]="bulkBusy() || documentsData().totalItems === 0"
                >
                  Eliminar todos
                </button>
              }
            </div>
          </div>
        }

        @if (loading() && documentsData().items.length === 0) {
          <p class="text-sm text-neutral-400">Cargando documentos...</p>
        } @else {
          <div class="overflow-hidden rounded-lg border border-neutral-200 bg-white shadow-sm">
            <div class="overflow-x-auto">
              <table class="min-w-full divide-y divide-neutral-200">
                <thead class="bg-neutral-50">
                  <tr>
                    <th scope="col" class="w-10 px-4 py-3">
                      <input
                        type="checkbox"
                        class="h-4 w-4 rounded border-neutral-300 text-blue-600 focus:ring-blue-500"
                        [checked]="allPageSelected()"
                        [indeterminate]="somePageSelected() && !allPageSelected()"
                        (change)="toggleSelectPage($event)"
                        [disabled]="documentsData().items.length === 0 || bulkBusy()"
                        aria-label="Seleccionar página"
                      />
                    </th>
                    <th scope="col" class="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-neutral-500">
                      <button type="button" class="inline-flex items-center gap-1 hover:text-neutral-800" (click)="toggleSort('filename')">
                        Nombre
                        <span class="text-[10px]">{{ sortIndicator('filename') }}</span>
                      </button>
                    </th>
                    <th scope="col" class="w-[90px] px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-neutral-500">
                      <button type="button" class="inline-flex items-center gap-1 hover:text-neutral-800" (click)="toggleSort('sourceType')">
                        Tipo
                        <span class="text-[10px]">{{ sortIndicator('sourceType') }}</span>
                      </button>
                    </th>
                    <th scope="col" class="w-[140px] px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-neutral-500">
                      <button type="button" class="inline-flex items-center gap-1 hover:text-neutral-800" (click)="toggleSort('indexedAt')">
                        Indexado
                        <span class="text-[10px]">{{ sortIndicator('indexedAt') }}</span>
                      </button>
                    </th>
                    <th scope="col" class="w-[120px] px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-neutral-500">
                      <button type="button" class="inline-flex items-center gap-1 hover:text-neutral-800" (click)="toggleSort('status')">
                        Estado
                        <span class="text-[10px]">{{ sortIndicator('status') }}</span>
                      </button>
                    </th>
                    <th scope="col" class="w-[100px] px-4 py-3 text-center text-xs font-medium uppercase tracking-wider text-neutral-500">
                      Acciones
                    </th>
                  </tr>
                </thead>
                <tbody class="divide-y divide-neutral-200 bg-white">
                  @for (item of documentsData().items; track item.id) {
                    <tr class="transition hover:bg-neutral-50" [class.bg-blue-50]="isSelected(item.id)">
                      <td class="px-4 py-3">
                        <input
                          type="checkbox"
                          class="h-4 w-4 rounded border-neutral-300 text-blue-600 focus:ring-blue-500"
                          [checked]="isSelected(item.id)"
                          (change)="toggleSelect(item.id)"
                          [disabled]="bulkBusy()"
                          [attr.aria-label]="'Seleccionar ' + item.filename"
                        />
                      </td>
                      <td class="max-w-md px-4 py-3 text-sm">
                        <button
                          type="button"
                          (click)="onPreview(item)"
                          class="break-all text-left text-blue-600 transition hover:underline"
                        >{{ item.filename }}</button>
                      </td>
                      <td class="px-4 py-3 text-sm text-neutral-700">{{ item.sourceLabel }}</td>
                      <td class="px-4 py-3 text-sm text-neutral-700">{{ item.indexedAt }}</td>
                      <td class="px-4 py-3 text-sm">
                        <span
                          class="inline-flex items-center gap-1"
                          [class.cursor-help]="!!item.errorMessage"
                          [attr.title]="item.errorMessage || null"
                        >
                          <app-status-pill
                            [status]="item.status"
                            type="publication"
                          ></app-status-pill>
                          @if (item.errorMessage) {
                            <svg
                              class="h-3.5 w-3.5 shrink-0 text-neutral-400"
                              fill="none"
                              viewBox="0 0 24 24"
                              stroke="currentColor"
                              stroke-width="2"
                              aria-hidden="true"
                            >
                              <path
                                stroke-linecap="round"
                                stroke-linejoin="round"
                                d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
                              />
                            </svg>
                          }
                        </span>
                      </td>
                      <td class="px-4 py-3 text-center">
                        <div class="flex items-center justify-center gap-2">
                          <button
                            type="button"
                            (click)="onDownload(item)"
                            title="Descargar"
                            class="rounded p-1 text-neutral-400 transition hover:text-blue-600"
                          >
                            <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                              <path stroke-linecap="round" stroke-linejoin="round" d="M4 16v2a2 2 0 002 2h12a2 2 0 002-2v-2M7 10l5 5 5-5M12 15V3" />
                            </svg>
                          </button>
                          <button
                            type="button"
                            (click)="onReindex(item)"
                            title="Reindexar"
                            class="rounded p-1 text-neutral-400 transition hover:text-blue-600"
                          >
                            <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                              <path d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                            </svg>
                          </button>
                          <button
                            type="button"
                            (click)="onDelete(item)"
                            title="Eliminar"
                            class="rounded p-1 text-neutral-400 transition hover:text-red-600"
                          >
                            <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                              <path d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                            </svg>
                          </button>
                        </div>
                      </td>
                    </tr>
                  } @empty {
                    <tr>
                      <td colspan="6" class="px-4 py-12 text-center text-sm text-neutral-500">
                        No se encontraron documentos.
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>

            @if (documentsData().totalItems > 0) {
              <div class="flex flex-wrap items-center justify-between gap-3 border-t border-neutral-200 bg-white px-4 py-3">
                <div class="flex flex-wrap items-center gap-3">
                  <p class="text-sm text-neutral-700">
                    Mostrando
                    <span class="font-medium">{{ pageStart() }}</span>
                    a
                    <span class="font-medium">{{ pageEnd() }}</span>
                    de
                    <span class="font-medium">{{ documentsData().totalItems }}</span>
                    resultados
                  </p>
                  <label class="flex items-center gap-2 text-sm text-neutral-600">
                    <span class="whitespace-nowrap">Por página</span>
                    <select
                      class="rounded-md border border-neutral-300 bg-white px-2 py-1.5 text-sm text-neutral-800 focus:border-neutral-400 focus:outline-none"
                      [value]="pageSize()"
                      (change)="onPageSizeChange($event)"
                      [disabled]="loading()"
                    >
                      @for (size of pageSizeOptions; track size) {
                        <option [value]="size">{{ size }}</option>
                      }
                    </select>
                  </label>
                </div>
                <div class="inline-flex rounded-md shadow-sm">
                  <button
                    type="button"
                    class="relative inline-flex items-center rounded-l-md border border-neutral-300 bg-white px-2 py-2 text-sm font-medium text-neutral-500 hover:bg-neutral-50 disabled:cursor-not-allowed disabled:bg-neutral-100"
                    [disabled]="page() <= 1 || loading()"
                    (click)="onPageChange(page() - 1)"
                  >
                    ‹
                  </button>
                  <span class="relative inline-flex items-center border-y border-neutral-300 bg-white px-4 py-2 text-sm font-medium text-neutral-700">
                    Página {{ page() }}
                    @if (documentsData().totalPages > 0) {
                      <span class="text-neutral-400"> / {{ documentsData().totalPages }}</span>
                    }
                  </span>
                  <button
                    type="button"
                    class="relative inline-flex items-center rounded-r-md border border-neutral-300 bg-white px-2 py-2 text-sm font-medium text-neutral-500 hover:bg-neutral-50 disabled:cursor-not-allowed disabled:bg-neutral-100"
                    [disabled]="page() >= documentsData().totalPages || loading()"
                    (click)="onPageChange(page() + 1)"
                  >
                    ›
                  </button>
                </div>
              </div>
            }
          </div>
        }
      </section>
    </div>

    @if (preview(); as p) {
      <div
        class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
        role="dialog"
        aria-modal="true"
        aria-label="Previsualización del documento"
        (click)="closePreview()"
      >
        <div
          class="flex max-h-[90vh] w-full max-w-3xl flex-col rounded-xl bg-white shadow-xl"
          (click)="$event.stopPropagation()"
        >
          <div class="flex items-center justify-between gap-3 border-b border-neutral-200 px-5 py-4">
            <h4 class="min-w-0 truncate text-base font-semibold text-neutral-900" [title]="p.filename">{{ p.filename }}</h4>
            <button
              type="button"
              (click)="closePreview()"
              class="shrink-0 text-neutral-400 transition hover:text-neutral-700"
              title="Cerrar"
            >×</button>
          </div>
          <pre class="flex-1 overflow-auto whitespace-pre-wrap px-5 py-4 text-xs leading-relaxed text-neutral-800">{{ p.content }}</pre>
        </div>
      </div>
    }

    @if (modalOpen()) {
      <div
        class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
        role="dialog"
        aria-modal="true"
        (click)="closeModal()"
      >
        <div
          class="flex max-h-[90vh] w-full max-w-xl flex-col rounded-xl bg-white shadow-xl"
          (click)="$event.stopPropagation()"
        >
          <div class="flex items-center justify-between border-b border-neutral-200 px-5 py-4">
            <h4 class="text-base font-semibold text-neutral-900">Agregar a la base de conocimiento</h4>
            <button
              type="button"
              (click)="closeModal()"
              class="text-neutral-400 transition hover:text-neutral-700"
              title="Cerrar"
            >×</button>
          </div>

          <div class="flex gap-2 border-b border-neutral-200 px-5 pt-3">
            <button
              type="button"
              (click)="modalTab.set('file')"
              class="border-b-2 px-3 pb-2 text-sm font-medium transition"
              [class]="modalTab() === 'file'
                ? 'border-blue-600 text-blue-600'
                : 'border-transparent text-neutral-500 hover:text-neutral-800'"
            >Archivo</button>
            <button
              type="button"
              (click)="modalTab.set('urls')"
              class="border-b-2 px-3 pb-2 text-sm font-medium transition"
              [class]="modalTab() === 'urls'
                ? 'border-blue-600 text-blue-600'
                : 'border-transparent text-neutral-500 hover:text-neutral-800'"
            >URLs</button>
          </div>

          <div class="flex-1 overflow-auto px-5 py-4">
            @if (modalTab() === 'file') {
              <p class="mb-3 text-sm text-neutral-500">
                Sube un archivo .txt (máximo 5 MB).
              </p>
              <input
                #fileInput
                type="file"
                accept=".txt"
                class="hidden"
                (change)="onFileSelected($event)"
              />
              <button
                type="button"
                (click)="fileInput.click()"
                [disabled]="uploading()"
                class="rounded-lg border border-neutral-200 px-3 py-2 text-sm font-medium text-neutral-800 transition hover:bg-neutral-50 disabled:opacity-50"
              >
                Seleccionar archivo .txt
              </button>
            } @else {
              <p class="mb-3 text-sm text-neutral-500">
                Pega una URL por línea o importa un CSV/Excel (.csv, .xlsx).
                Se detectan automáticamente las celdas con http(s). Máximo {{ maxUrls }} URLs.
              </p>
              <textarea
                class="mb-3 h-28 w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm text-neutral-900 placeholder:text-neutral-400 focus:border-neutral-400 focus:outline-none"
                placeholder="https://ejemplo.go.cr/pagina&#10;https://ejemplo.go.cr/otra"
                [value]="urlDraft()"
                (input)="onUrlDraftInput($event)"
              ></textarea>
              <div class="mb-3 flex flex-wrap items-center gap-2">
                <button
                  type="button"
                  (click)="parseUrlDraft()"
                  class="rounded-lg border border-neutral-200 px-3 py-1.5 text-sm font-medium text-neutral-800 transition hover:bg-neutral-50"
                >
                  Agregar al listado
                </button>
                <input
                  #csvInput
                  type="file"
                  accept=".csv,.xlsx,.xls,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,application/vnd.ms-excel"
                  class="hidden"
                  (change)="onSpreadsheetSelected($event)"
                />
                <button
                  type="button"
                  (click)="csvInput.click()"
                  class="rounded-lg border border-neutral-200 px-3 py-1.5 text-sm font-medium text-neutral-800 transition hover:bg-neutral-50"
                >
                  Importar CSV / Excel
                </button>
                @if (parsedUrls().length) {
                  <button
                    type="button"
                    (click)="clearParsedUrls()"
                    class="rounded-lg px-3 py-1.5 text-sm font-medium text-red-600 transition hover:bg-red-50"
                  >
                    Limpiar listado
                  </button>
                }
              </div>

              <div class="mb-2 text-xs text-neutral-500">
                {{ parsedUrls().length }} URLs
                @if (invalidUrlCount() > 0) {
                  · <span class="text-amber-700">{{ invalidUrlCount() }} inválidas</span>
                }
                @if (duplicateUrlCount() > 0) {
                  · {{ duplicateUrlCount() }} duplicadas omitidas
                }
              </div>

              @if (parsedUrls().length) {
                <ul class="max-h-56 overflow-auto rounded-lg border border-neutral-200 bg-neutral-50 text-xs">
                  @for (item of visibleParsedUrls(); track item.url; let i = $index) {
                    <li class="flex items-start gap-2 border-b border-neutral-100 px-3 py-2 last:border-b-0">
                      <span
                        class="mt-0.5 shrink-0"
                        [class]="item.valid ? 'text-green-600' : 'text-amber-600'"
                      >{{ item.valid ? '✓' : '!' }}</span>
                      <span class="min-w-0 flex-1 break-all text-neutral-800">{{ item.url }}</span>
                      <button
                        type="button"
                        (click)="removeParsedUrl(i)"
                        class="shrink-0 text-neutral-400 hover:text-red-600"
                        title="Quitar"
                      >×</button>
                    </li>
                  }
                </ul>
                @if (parsedUrls().length > urlPreviewLimit) {
                  <p class="mt-2 text-xs text-neutral-500">
                    Mostrando las primeras {{ urlPreviewLimit }} de {{ parsedUrls().length }}.
                  </p>
                }
              }
            }
          </div>

          <div class="flex justify-end gap-2 border-t border-neutral-200 px-5 py-4">
            <button
              type="button"
              (click)="closeModal()"
              class="rounded-lg px-3 py-2 text-sm font-medium text-neutral-600 transition hover:bg-neutral-50"
            >
              Cancelar
            </button>
            @if (modalTab() === 'urls') {
              <button
                type="button"
                (click)="submitUrls()"
                [disabled]="uploading() || validParsedUrls().length === 0"
                class="rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white transition hover:bg-blue-700 disabled:opacity-50"
              >
                {{ uploading() ? 'Enviando...' : 'Agregar URLs' }}
              </button>
            }
          </div>
        </div>
      </div>
    }
  `,
})
export class BotEditKb implements OnDestroy {
  private readonly store = inject(BotEditStore);
  private readonly api = inject(Api);
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  readonly maxUrls = MAX_URLS;
  readonly urlPreviewLimit = URL_PREVIEW_LIMIT;
  readonly pageSizeOptions = PAGE_SIZE_OPTIONS;

  readonly loading = signal(true);
  readonly uploading = signal(false);
  readonly preview = signal<{ filename: string; content: string } | null>(null);
  readonly modalOpen = signal(false);
  readonly modalTab = signal<'file' | 'urls'>('file');
  readonly urlDraft = signal('');
  readonly parsedUrls = signal<Array<{ url: string; valid: boolean }>>([]);
  readonly invalidUrlCount = signal(0);
  readonly duplicateUrlCount = signal(0);

  readonly page = signal(1);
  readonly pageSize = signal<number>(DEFAULT_PAGE_SIZE);
  readonly sortBy = signal<KbSortBy>('createdAt');
  readonly sortDir = signal<'asc' | 'desc'>('desc');
  readonly searchInput = signal('');
  readonly search = signal('');
  readonly statusFilter = signal('');
  readonly sourceTypeFilter = signal('');
  readonly selectedIds = signal<Set<number>>(new Set());
  readonly bulkBusy = signal(false);

  readonly documentsData = signal({
    items: [] as KbDocRow[],
    page: 1,
    pageSize: DEFAULT_PAGE_SIZE,
    totalItems: 0,
    totalPages: 0,
  });

  readonly selectedCount = computed(() => this.selectedIds().size);
  readonly allPageSelected = computed(() => {
    const items = this.documentsData().items;
    if (items.length === 0) return false;
    const selected = this.selectedIds();
    return items.every(item => selected.has(item.id));
  });
  readonly somePageSelected = computed(() => {
    const items = this.documentsData().items;
    if (items.length === 0) return false;
    const selected = this.selectedIds();
    return items.some(item => selected.has(item.id));
  });

  constructor() {
    effect(() => {
      const bot = this.store.bot();
      // Depend on pagination/sort/search/filters so reloads stay in sync.
      this.page();
      this.pageSize();
      this.sortBy();
      this.sortDir();
      this.search();
      this.statusFilter();
      this.sourceTypeFilter();
      if (bot) {
        void this.loadDocuments(bot.id);
      }
    });
  }

  ngOnDestroy() {
    if (this.searchTimer) clearTimeout(this.searchTimer);
  }

  pageStart() {
    const data = this.documentsData();
    if (data.totalItems === 0) return 0;
    return (data.page - 1) * data.pageSize + 1;
  }

  pageEnd() {
    const data = this.documentsData();
    return Math.min(data.page * data.pageSize, data.totalItems);
  }

  onPageChange(page: number) {
    if (page < 1 || page > this.documentsData().totalPages) return;
    this.page.set(page);
  }

  onPageSizeChange(event: Event) {
    const value = Number((event.target as HTMLSelectElement).value);
    if (!PAGE_SIZE_OPTIONS.includes(value as (typeof PAGE_SIZE_OPTIONS)[number])) return;
    this.pageSize.set(value);
    this.page.set(1);
  }

  toggleSort(column: KbSortBy) {
    if (this.sortBy() === column) {
      this.sortDir.update(d => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      this.sortBy.set(column);
      this.sortDir.set(column === 'filename' || column === 'sourceType' ? 'asc' : 'desc');
    }
    this.page.set(1);
  }

  sortIndicator(column: KbSortBy): string {
    if (this.sortBy() !== column) return '↕';
    return this.sortDir() === 'asc' ? '↑' : '↓';
  }

  onSearchInput(event: Event) {
    const value = (event.target as HTMLInputElement).value;
    this.searchInput.set(value);
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.page.set(1);
      this.clearSelection();
      this.search.set(value.trim());
    }, 300);
  }

  onStatusFilterChange(event: Event) {
    this.statusFilter.set((event.target as HTMLSelectElement).value);
    this.page.set(1);
    this.clearSelection();
  }

  onSourceTypeFilterChange(event: Event) {
    this.sourceTypeFilter.set((event.target as HTMLSelectElement).value);
    this.page.set(1);
    this.clearSelection();
  }

  hasActiveFilters(): boolean {
    return !!this.search() || !!this.statusFilter() || !!this.sourceTypeFilter();
  }

  isSelected(id: number): boolean {
    return this.selectedIds().has(id);
  }

  clearSelection() {
    this.selectedIds.set(new Set());
  }

  toggleSelect(id: number) {
    this.selectedIds.update(current => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  toggleSelectPage(event: Event) {
    const checked = (event.target as HTMLInputElement).checked;
    const pageIds = this.documentsData().items.map(item => item.id);
    this.selectedIds.update(current => {
      const next = new Set(current);
      for (const id of pageIds) {
        if (checked) next.add(id);
        else next.delete(id);
      }
      return next;
    });
  }

  openModal() {
    this.modalTab.set('file');
    this.urlDraft.set('');
    this.parsedUrls.set([]);
    this.invalidUrlCount.set(0);
    this.duplicateUrlCount.set(0);
    this.modalOpen.set(true);
  }

  closeModal() {
    if (this.uploading()) return;
    this.modalOpen.set(false);
  }

  onUrlDraftInput(event: Event) {
    this.urlDraft.set((event.target as HTMLTextAreaElement).value);
  }

  parseUrlDraft() {
    this.appendUrls(this.urlDraft().split(/\r?\n/));
    this.urlDraft.set('');
  }

  clearParsedUrls() {
    this.parsedUrls.set([]);
    this.invalidUrlCount.set(0);
    this.duplicateUrlCount.set(0);
  }

  removeParsedUrl(visibleIndex: number) {
    const current = [...this.parsedUrls()];
    current.splice(visibleIndex, 1);
    this.parsedUrls.set(current);
    this.invalidUrlCount.set(current.filter(u => !u.valid).length);
  }

  visibleParsedUrls() {
    return this.parsedUrls().slice(0, URL_PREVIEW_LIMIT);
  }

  validParsedUrls() {
    return this.parsedUrls().filter(u => u.valid).map(u => u.url);
  }

  async onSpreadsheetSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    try {
      const urls = await this.extractUrlsFromSpreadsheet(file);
      if (urls.length === 0) {
        toast.error('No se encontraron URLs http(s) en el archivo.');
        return;
      }
      this.appendUrls(urls);
      toast.success(`Archivo importado: ${urls.length} URL(s) detectadas.`);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'No se pudo leer el archivo.';
      toast.error(message);
    } finally {
      input.value = '';
    }
  }

  async onFileSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    const bot = this.store.bot();
    if (!bot) return;

    this.uploading.set(true);
    try {
      await this.api.invoke(uploadKbDocument, {
        botId: bot.id,
        body: { file },
      });
      toast.success('Documento subido exitosamente.');
      this.modalOpen.set(false);
      this.page.set(1);
      await this.loadDocuments(bot.id);
    } catch {
      toast.error('Error al subir el documento.');
    } finally {
      this.uploading.set(false);
      input.value = '';
    }
  }

  async submitUrls() {
    const bot = this.store.bot();
    if (!bot) return;

    const urls = this.validParsedUrls();
    if (urls.length === 0) {
      toast.error('Agrega al menos una URL válida.');
      return;
    }
    if (urls.length > MAX_URLS) {
      toast.error(`Máximo ${MAX_URLS} URLs por solicitud.`);
      return;
    }

    this.uploading.set(true);
    try {
      const result = await this.api.invoke(createKbDocumentsFromUrls, {
        botId: bot.id,
        body: { urls },
      });
      const created = result.created?.length ?? 0;
      const skipped = result.skippedExistingCount ?? 0;
      const skippedPdf = result.skippedPdfCount ?? 0;
      const extras: string[] = [];
      if (skipped > 0) extras.push(`${skipped} ya existían`);
      if (skippedPdf > 0) extras.push(`${skippedPdf} PDF omitido(s)`);
      if (created === 0 && skipped > 0 && skippedPdf === 0) {
        toast.info(`Ninguna URL nueva: ${skipped} ya existían en este bot.`);
      } else if (extras.length > 0) {
        toast.success(`${created} URL(s) agregadas · ${extras.join(' · ')}.`);
      } else {
        toast.success(`${created} URL(s) agregadas.`);
      }
      this.modalOpen.set(false);
      this.page.set(1);
      await this.loadDocuments(bot.id);
    } catch (err: unknown) {
      const message = extractApiErrorMessage(err);
      toast.error(message ?? 'Error al agregar las URLs.');
    } finally {
      this.uploading.set(false);
    }
  }

  async onDownload(item: KbDocumentResponse) {
    const bot = this.store.bot();
    if (!bot) return;

    try {
      window.open(
        `${this.api.rootUrl}/api/bots/${bot.id}/documents/${item.id}/download`,
        '_blank',
        'noopener,noreferrer',
      );
    } catch {
      toast.error('Error al obtener el enlace de descarga.');
    }
  }

  async onPreview(item: KbDocumentResponse) {
    const bot = this.store.bot();
    if (!bot) return;

    try {
      const result = await this.api.invoke(getKbDocumentContent, {
        botId: bot.id,
        docId: item.id,
      });
      this.preview.set({ filename: result.filename, content: result.content });
    } catch {
      toast.error('Error al cargar la previsualización.');
    }
  }

  closePreview() {
    this.preview.set(null);
  }

  async onReindex(item: KbDocumentResponse) {
    const bot = this.store.bot();
    if (!bot) return;

    try {
      await this.api.invoke(reindexKbDocument, {
        botId: bot.id,
        docId: item.id,
      });
      toast.info('Documento enviado a reindexar.');
      await this.loadDocuments(bot.id);
    } catch {
      toast.error('Error al reindexar el documento.');
    }
  }

  async onDelete(item: KbDocumentResponse) {
    if (!confirm(`¿Eliminar "${item.filename}"?`)) return;

    const bot = this.store.bot();
    if (!bot) return;

    try {
      await this.api.invoke(deleteKbDocument, {
        botId: bot.id,
        docId: item.id,
      });
      this.selectedIds.update(current => {
        const next = new Set(current);
        next.delete(item.id);
        return next;
      });
      toast.info('Documento eliminado.');
      await this.loadDocuments(bot.id);
    } catch {
      toast.error('Error al eliminar el documento.');
    }
  }

  async bulkReindexSelected() {
    const ids = [...this.selectedIds()];
    if (ids.length === 0) return;
    if (!confirm(`¿Reindexar ${ids.length} documento(s) seleccionado(s)?`)) return;
    await this.runBulkReindex({ documentIds: ids });
  }

  async bulkDeleteSelected() {
    const ids = [...this.selectedIds()];
    if (ids.length === 0) return;
    if (!confirm(`¿Eliminar ${ids.length} documento(s) seleccionado(s)? Esta acción no se puede deshacer.`)) return;
    await this.runBulkDelete({ documentIds: ids });
  }

  async bulkReindexFiltered() {
    const total = this.documentsData().totalItems;
    if (total === 0) return;
    const scope = this.hasActiveFilters() ? 'filtrados' : 'existentes';
    if (!confirm(`¿Reindexar los ${total} documento(s) ${scope}?`)) return;
    await this.runBulkReindex(this.currentFilterRequest());
  }

  async bulkDeleteFiltered() {
    const total = this.documentsData().totalItems;
    if (total === 0) return;
    const scope = this.hasActiveFilters() ? 'filtrados' : 'existentes';
    if (!confirm(`¿Eliminar los ${total} documento(s) ${scope}? Esta acción no se puede deshacer.`)) return;
    await this.runBulkDelete(this.currentFilterRequest());
  }

  private currentFilterRequest(): BulkKbDocumentsRequest {
    return {
      documentIds: null,
      status: this.statusFilter() || null,
      sourceType: this.sourceTypeFilter() || null,
      search: this.search() || null,
    };
  }

  private async runBulkReindex(body: BulkKbDocumentsRequest) {
    const bot = this.store.bot();
    if (!bot) return;

    this.bulkBusy.set(true);
    try {
      const result = await this.api.invoke(bulkReindexKbDocuments, { botId: bot.id, body });
      toast.info(`${result.affectedCount} documento(s) enviados a reindexar.`);
      this.clearSelection();
      await this.loadDocuments(bot.id);
    } catch {
      toast.error('Error al reindexar documentos.');
    } finally {
      this.bulkBusy.set(false);
    }
  }

  private async runBulkDelete(body: BulkKbDocumentsRequest) {
    const bot = this.store.bot();
    if (!bot) return;

    this.bulkBusy.set(true);
    try {
      const result = await this.api.invoke(bulkDeleteKbDocuments, { botId: bot.id, body });
      toast.info(`${result.affectedCount} documento(s) eliminados.`);
      this.clearSelection();
      this.page.set(1);
      await this.loadDocuments(bot.id);
    } catch {
      toast.error('Error al eliminar documentos.');
    } finally {
      this.bulkBusy.set(false);
    }
  }

  private appendUrls(rawLines: string[]) {
    const existing = new Set(this.parsedUrls().map(u => u.url.toLowerCase()));
    let duplicates = this.duplicateUrlCount();
    let invalid = this.invalidUrlCount();
    const next = [...this.parsedUrls()];

    for (const raw of rawLines) {
      const url = raw.trim();
      if (!url) continue;
      const key = url.toLowerCase();
      if (existing.has(key)) {
        duplicates++;
        continue;
      }
      if (next.length >= MAX_URLS) {
        toast.error(`Se alcanzó el máximo de ${MAX_URLS} URLs.`);
        break;
      }
      existing.add(key);
      const valid = this.isValidHttpUrl(url);
      if (!valid) invalid++;
      next.push({ url, valid });
    }

    this.parsedUrls.set(next);
    this.duplicateUrlCount.set(duplicates);
    this.invalidUrlCount.set(invalid);
  }

  private async extractUrlsFromSpreadsheet(file: File): Promise<string[]> {
    const name = file.name.toLowerCase();
    const isExcel = name.endsWith('.xlsx') || name.endsWith('.xls');
    const isCsv = name.endsWith('.csv') || file.type === 'text/csv';

    if (!isExcel && !isCsv) {
      throw new Error('Formato no soportado. Usa .csv o .xlsx.');
    }

    const buffer = await file.arrayBuffer();
    const bytes = new Uint8Array(buffer);

    // .xlsx is a ZIP ("PK…"). Reading it as text produced the garbage list you saw.
    if (!isExcel && bytes.length >= 2 && bytes[0] === 0x50 && bytes[1] === 0x4b) {
      throw new Error('Este archivo parece Excel (.xlsx). Impórtalo como .xlsx o expórtalo a CSV real.');
    }

    const workbook = XLSX.read(buffer, {
      type: 'array',
      raw: false,
      cellDates: false,
    });

    const urls: string[] = [];
    const seen = new Set<string>();

    for (const sheetName of workbook.SheetNames) {
      const sheet = workbook.Sheets[sheetName];
      if (!sheet) continue;
      const rows = XLSX.utils.sheet_to_json<(string | number | boolean | null)[]>(sheet, {
        header: 1,
        defval: '',
        raw: false,
        blankrows: false,
      });

      for (const row of rows) {
        for (const cell of row) {
          for (const url of this.extractHttpUrlsFromCell(String(cell ?? ''))) {
            const key = url.toLowerCase();
            if (seen.has(key)) continue;
            seen.add(key);
            urls.push(url);
          }
        }
      }
    }

    return urls;
  }

  private extractHttpUrlsFromCell(value: string): string[] {
    const text = value.trim();
    if (!text) return [];

    // Prefer exact URL cells; also pull URLs embedded in longer notes.
    const matches = text.match(/https?:\/\/[^\s<>"')\]]+/gi) ?? [];
    const cleaned = matches.map(m => m.replace(/[.,;:!?]+$/g, ''));

    if (cleaned.length > 0) return cleaned;
    if (this.isValidHttpUrl(text)) return [text];
    return [];
  }

  private isValidHttpUrl(value: string): boolean {
    try {
      const uri = new URL(value);
      return uri.protocol === 'http:' || uri.protocol === 'https:';
    } catch {
      return false;
    }
  }

  private async loadDocuments(botId: number) {
    this.loading.set(true);
    try {
      const result = await this.api.invoke(getKbDocuments, {
        botId,
        page: this.page(),
        pageSize: this.pageSize(),
        sortBy: this.sortBy(),
        sortDir: this.sortDir(),
        search: this.search() || undefined,
        status: this.statusFilter() || undefined,
        sourceType: this.sourceTypeFilter() || undefined,
      });
      this.documentsData.set({
        items: result.items.map(doc => ({
          ...doc,
          status: STATUS_MAP[doc.status] ?? doc.status,
          sourceLabel: SOURCE_MAP[doc.sourceType] ?? doc.sourceType,
          indexedAt: doc.indexedAt ? new Date(doc.indexedAt).toLocaleDateString('es', { year: 'numeric', month: 'short', day: 'numeric' }) : '—',
          errorMessage: doc.errorMessage ?? null,
        })),
        page: Number(result.page),
        pageSize: Number(result.pageSize),
        totalItems: Number(result.totalItems),
        totalPages: Number(result.totalPages),
      });
    } catch {
      toast.error('Error al cargar documentos.');
    } finally {
      this.loading.set(false);
    }
  }
}
