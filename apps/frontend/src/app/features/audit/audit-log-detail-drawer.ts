import {
  ChangeDetectionStrategy,
  Component,
  input,
  output,
} from '@angular/core';
import type { AuditLogResponse } from '../../api/models/audit-log-response';
import type { AuditFieldChange } from '../../api/models/audit-field-change';

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

@Component({
  selector: 'bf-audit-log-detail-drawer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (open()) {
      <div
        class="fixed inset-0 z-40 bg-black/40 backdrop-blur-sm transition-opacity"
        (click)="closed.emit()"
      ></div>

      <aside
        class="fixed inset-y-0 right-0 z-50 flex w-full max-w-lg flex-col bg-white shadow-2xl"
      >
        <header
          class="flex items-center justify-between border-b border-neutral-200 px-5 py-4"
        >
          <div>
            <h2 class="text-sm font-semibold text-neutral-900">
              Detalle de auditoría
            </h2>
            @if (log(); as entry) {
              <p class="mt-0.5 text-xs text-neutral-500">
                {{ entityLabel(entry.entityType) }}
                · #{{ entry.entityId }}
                · {{ actionLabel(entry.action) }}
              </p>
              <p class="mt-0.5 text-xs text-neutral-500">
                {{ entry.userName }} · {{ formatDate(entry.createdAt) }}
              </p>
            }
          </div>
          <button
            type="button"
            class="flex h-8 w-8 items-center justify-center rounded-full text-neutral-400 transition-colors hover:bg-neutral-100 hover:text-neutral-700"
            aria-label="Cerrar panel"
            (click)="closed.emit()"
          >
            <svg
              xmlns="http://www.w3.org/2000/svg"
              viewBox="0 0 24 24"
              fill="currentColor"
              class="h-4 w-4"
            >
              <path
                d="M6.2 4.8a1 1 0 0 1 1.4 0L12 9.2l4.4-4.4a1 1 0 1 1 1.4 1.4L13.4 10.6l4.4 4.4a1 1 0 1 1-1.4 1.4L12 12l-4.4 4.4a1 1 0 1 1-1.4-1.4l4.4-4.4-4.4-4.4a1 1 0 0 1 0-1.4Z"
              />
            </svg>
          </button>
        </header>

        <div class="flex-1 overflow-y-auto px-5 py-4">
          @if (log(); as entry) {
            @if (entry.changes.length === 0) {
              <p class="py-8 text-center text-sm text-neutral-400">
                No hay cambios registrados.
              </p>
            } @else {
              <div class="overflow-hidden rounded-lg border border-neutral-200">
                <table class="w-full text-left text-sm">
                  <thead class="bg-neutral-50 text-xs font-medium uppercase text-neutral-500">
                    <tr>
                      <th class="px-3 py-2">Campo</th>
                      <th class="px-3 py-2">Anterior</th>
                      <th class="px-3 py-2">Nuevo</th>
                    </tr>
                  </thead>
                  <tbody class="divide-y divide-neutral-100">
                    @for (change of entry.changes; track change.field) {
                      <tr>
                        <td class="px-3 py-2 font-medium text-neutral-900">
                          {{ change.field }}
                        </td>
                        <td class="max-w-[140px] break-words px-3 py-2 text-neutral-600">
                          {{ formatValue(change.oldValue) }}
                        </td>
                        <td class="max-w-[140px] break-words px-3 py-2 text-neutral-600">
                          {{ formatValue(change.newValue) }}
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          }
        </div>
      </aside>
    }
  `,
})
export class AuditLogDetailDrawer {
  readonly open = input<boolean>(false);
  readonly log = input<AuditLogResponse | null>(null);

  readonly closed = output<void>();

  entityLabel(entityType: string): string {
    return ENTITY_LABELS[entityType] ?? entityType;
  }

  actionLabel(action: string): string {
    return ACTION_LABELS[action] ?? action;
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

  formatValue(value: AuditFieldChange['oldValue']): string {
    if (value === null || value === undefined) return '—';
    if (typeof value === 'object') return JSON.stringify(value);
    return String(value);
  }
}
