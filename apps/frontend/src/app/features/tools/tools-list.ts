import { Component, computed, inject, signal, OnInit } from '@angular/core';
import { SlicePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { PageShellComponent, StatusPillComponent } from '@public-intelligence/ui-core';
import { toast } from 'ngx-sonner';
import { Api } from '../../api/api';
import { AuthService } from '../../core/services/auth.service';
import {
  createToolConnection,
  deleteToolConnection,
  getToolConnections,
  getToolDefinitions,
  probeToolConnection,
  updateToolConnection,
} from '../../api/functions';
import type { ToolConnectionResponse } from '../../api/models/tool-connection-response';
import type { ToolDefinitionResponse } from '../../api/models/tool-definition-response';

const STATUS_LABELS: Record<string, string> = {
  Draft: 'Borrador',
  Published: 'Publicado',
  Disabled: 'Deshabilitado',
  Archived: 'Archivado',
  Active: 'Activa',
};

const AUTH_LABELS: Record<string, string> = {
  None: 'Sin autenticación',
  ApiKeyHeader: 'API key (header)',
  ApiKeyQuery: 'API key (query)',
  Bearer: 'Bearer token',
  Basic: 'Usuario y contraseña',
};

@Component({
  selector: 'bf-tools-list',
  imports: [FormsModule, RouterLink, SlicePipe, PageShellComponent, StatusPillComponent],
  template: `
    <app-page-shell
      title="Herramientas"
      subtitle="Herramientas externas configurables que los bots pueden usar durante una conversación."
    >
      <!-- Section tabs -->
      <div class="mb-6 border-b border-neutral-200">
        <nav class="-mb-px flex gap-6">
          <button
            (click)="section.set('tools')"
            class="border-b-2 pb-3 text-sm font-medium transition"
            [class]="section() === 'tools'
              ? 'border-neutral-900 text-neutral-900'
              : 'border-transparent text-neutral-500 hover:text-neutral-700'"
          >
            Herramientas
          </button>
          <button
            (click)="section.set('connections')"
            class="border-b-2 pb-3 text-sm font-medium transition"
            [class]="section() === 'connections'
              ? 'border-neutral-900 text-neutral-900'
              : 'border-transparent text-neutral-500 hover:text-neutral-700'"
          >
            Conexiones
          </button>
        </nav>
      </div>

      @if (section() === 'tools') {
        <!-- ── Herramientas ─────────────────────────────────────────── -->
        <div class="mb-4 flex flex-wrap items-center justify-between gap-3">
          <div class="flex items-center gap-3">
            <input
              type="search"
              [ngModel]="search()"
              (ngModelChange)="onSearch($event)"
              placeholder="Buscar herramienta…"
              class="w-64 rounded-lg border border-neutral-300 px-3 py-1.5 text-sm focus:border-blue-500 focus:outline-none"
            />
            <select
              [ngModel]="statusFilter()"
              (ngModelChange)="onStatusFilter($event)"
              class="rounded-lg border border-neutral-300 px-3 py-1.5 text-sm focus:border-blue-500 focus:outline-none"
            >
              <option value="">Todos los estados</option>
              <option value="Draft">Borrador</option>
              <option value="Published">Publicado</option>
              <option value="Disabled">Deshabilitado</option>
            </select>
          </div>
          @if (canEdit()) {
            <a
              routerLink="/tools/create"
              class="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-blue-700"
            >
              + Nueva herramienta
            </a>
          }
        </div>

        @if (loadingTools()) {
          <div class="py-24 text-center text-sm text-neutral-500">Cargando herramientas…</div>
        } @else if (toolsError()) {
          <div class="flex flex-col items-center py-24">
            <p class="mb-4 text-sm text-red-600">{{ toolsError() }}</p>
            <button (click)="loadTools()" class="rounded-lg border border-neutral-300 px-4 py-2 text-sm">
              Reintentar
            </button>
          </div>
        } @else if (tools().length === 0) {
          <div class="flex flex-col items-center py-24 text-center">
            <p class="mb-1 text-sm font-medium text-neutral-900">No hay herramientas configuradas</p>
            <p class="mb-4 text-sm text-neutral-500">
              Crea una conexión al servicio externo y luego define la primera herramienta.
            </p>
          </div>
        } @else {
          <div class="overflow-x-auto rounded-lg border border-neutral-200">
            <table class="w-full text-sm">
              <thead class="bg-neutral-50 text-left text-xs uppercase text-neutral-500">
                <tr>
                  <th class="px-4 py-3">Herramienta</th>
                  <th class="px-4 py-3">Nombre técnico</th>
                  <th class="px-4 py-3">Conexión</th>
                  <th class="px-4 py-3">Estado</th>
                  <th class="px-4 py-3">Bots</th>
                  <th class="px-4 py-3">Última prueba</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-neutral-100">
                @for (tool of tools(); track tool.id) {
                  <tr class="hover:bg-neutral-50">
                    <td class="px-4 py-3">
                      <a [routerLink]="['/tools', tool.id, 'edit']" class="font-medium text-blue-600 hover:underline">
                        {{ tool.displayName }}
                      </a>
                      @if (tool.category) {
                        <div class="text-xs text-neutral-400">{{ tool.category }}</div>
                      }
                    </td>
                    <td class="px-4 py-3 font-mono text-xs text-neutral-600">{{ tool.name }}</td>
                    <td class="px-4 py-3 text-neutral-600">{{ tool.connectionName }}</td>
                    <td class="px-4 py-3">
                      <app-status-pill [status]="statusLabel(tool.status)" type="publication" />
                    </td>
                    <td class="px-4 py-3 text-neutral-600">{{ tool.botCount }}</td>
                    <td class="px-4 py-3 text-xs text-neutral-500">
                      @if (tool.lastTestedAt) {
                        {{ tool.lastTestOutcome === 'passed' ? '✓' : '✗' }}
                        {{ tool.lastTestedAt | slice: 0 : 10 }}
                      } @else {
                        Sin probar
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      } @else {
        <!-- ── Conexiones ───────────────────────────────────────────── -->
        <div class="mb-4 flex items-center justify-between">
          <p class="text-sm text-neutral-500">
            Configuración técnica reutilizable de cada sistema externo. Las credenciales se
            almacenan cifradas y nunca se muestran completas.
          </p>
          @if (canEdit()) {
            <button
              (click)="openConnectionModal(null)"
              class="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-blue-700"
            >
              + Nueva conexión
            </button>
          }
        </div>

        @if (loadingConnections()) {
          <div class="py-24 text-center text-sm text-neutral-500">Cargando conexiones…</div>
        } @else if (connections().length === 0) {
          <div class="py-24 text-center text-sm text-neutral-500">
            No hay conexiones. Crea la primera para poder definir herramientas.
          </div>
        } @else {
          <div class="grid gap-4 md:grid-cols-2">
            @for (conn of connections(); track conn.id) {
              <div class="rounded-lg border border-neutral-200 p-4">
                <div class="mb-2 flex items-start justify-between">
                  <div>
                    <div class="font-medium text-neutral-900">{{ conn.name }}</div>
                    <div class="font-mono text-xs text-neutral-500">{{ conn.baseUrl }}</div>
                  </div>
                  <app-status-pill
                    [status]="conn.status === 'Active' ? 'Activa' : 'Deshabilitada'"
                    type="access"
                  />
                </div>
                <div class="mb-3 space-y-1 text-xs text-neutral-600">
                  <div>{{ authLabel(conn.authType) }}</div>
                  @if (conn.hasSecrets) {
                    <div class="font-mono">Credencial: {{ conn.secretPrefix }}••••••••</div>
                  }
                  <div>{{ conn.toolCount }} herramienta(s) · timeout {{ conn.timeoutMs }} ms</div>
                </div>
                @if (canEdit()) {
                  <div class="flex items-center gap-2 text-sm">
                    <button
                      (click)="onProbe(conn)"
                      [disabled]="probing() === conn.id"
                      class="rounded border border-neutral-300 px-2.5 py-1 text-xs transition hover:bg-neutral-50 disabled:opacity-50"
                    >
                      {{ probing() === conn.id ? 'Probando…' : 'Probar conexión' }}
                    </button>
                    <button
                      (click)="openConnectionModal(conn)"
                      class="rounded border border-neutral-300 px-2.5 py-1 text-xs transition hover:bg-neutral-50"
                    >
                      Editar
                    </button>
                    <button
                      (click)="onDeleteConnection(conn)"
                      class="rounded px-2.5 py-1 text-xs text-red-600 transition hover:bg-red-50"
                    >
                      Eliminar
                    </button>
                  </div>
                }
              </div>
            }
          </div>
        }
      }
    </app-page-shell>

    <!-- Connection create/edit modal -->
    @if (modalOpen()) {
      <div
        class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
        (click)="modalOpen.set(false)"
      >
        <div
          class="max-h-[90vh] w-full max-w-lg overflow-y-auto rounded-xl bg-white p-6 shadow-xl"
          (click)="$event.stopPropagation()"
        >
          <h2 class="mb-4 text-lg font-semibold text-neutral-900">
            {{ editingConnection() ? 'Editar conexión' : 'Nueva conexión' }}
          </h2>

          <div class="space-y-4">
            <div>
              <label class="mb-1 block text-sm font-medium text-neutral-700">Nombre</label>
              <input
                [(ngModel)]="form.name"
                class="w-full rounded-lg border border-neutral-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
                placeholder="Registro Civil — producción"
              />
            </div>
            <div>
              <label class="mb-1 block text-sm font-medium text-neutral-700">URL base</label>
              <input
                [(ngModel)]="form.baseUrl"
                class="w-full rounded-lg border border-neutral-300 px-3 py-2 font-mono text-sm focus:border-blue-500 focus:outline-none"
                placeholder="https://api.registro.gob.do"
              />
              <p class="mt-1 text-xs text-neutral-500">
                Todas las herramientas de esta conexión quedan limitadas a este host.
              </p>
            </div>
            <div>
              <label class="mb-1 block text-sm font-medium text-neutral-700">Autenticación</label>
              <select
                [(ngModel)]="form.authType"
                class="w-full rounded-lg border border-neutral-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
              >
                <option value="None">Sin autenticación</option>
                <option value="ApiKeyHeader">API key en header</option>
                <option value="ApiKeyQuery">API key en query</option>
                <option value="Bearer">Bearer token</option>
                <option value="Basic">Usuario y contraseña</option>
              </select>
            </div>

            @if (form.authType === 'ApiKeyHeader') {
              <div>
                <label class="mb-1 block text-sm font-medium text-neutral-700">Nombre del header</label>
                <input
                  [(ngModel)]="form.headerName"
                  class="w-full rounded-lg border border-neutral-300 px-3 py-2 font-mono text-sm focus:border-blue-500 focus:outline-none"
                  placeholder="X-Api-Key"
                />
              </div>
            }
            @if (form.authType === 'ApiKeyQuery') {
              <div>
                <label class="mb-1 block text-sm font-medium text-neutral-700">Nombre del parámetro</label>
                <input
                  [(ngModel)]="form.paramName"
                  class="w-full rounded-lg border border-neutral-300 px-3 py-2 font-mono text-sm focus:border-blue-500 focus:outline-none"
                  placeholder="access_key"
                />
                <p class="mt-1 text-xs text-neutral-500">
                  La clave se agrega a la URL solo al ejecutar; nunca aparece en registros ni resúmenes.
                </p>
              </div>
            }
            @if (form.authType === 'Basic') {
              <div>
                <label class="mb-1 block text-sm font-medium text-neutral-700">Usuario</label>
                <input
                  [(ngModel)]="form.username"
                  class="w-full rounded-lg border border-neutral-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
                />
              </div>
            }
            @if (form.authType !== 'None') {
              <div>
                <label class="mb-1 block text-sm font-medium text-neutral-700">
                  {{ form.authType === 'Basic' ? 'Contraseña' : 'API key / token' }}
                </label>
                <input
                  type="password"
                  [(ngModel)]="form.secret"
                  autocomplete="new-password"
                  class="w-full rounded-lg border border-neutral-300 px-3 py-2 font-mono text-sm focus:border-blue-500 focus:outline-none"
                  [placeholder]="secretPlaceholder()"
                />
                @if (editingConnection()?.hasSecrets) {
                  <p class="mt-1 text-xs text-neutral-500">
                    Deja el campo vacío para conservar la credencial actual.
                  </p>
                }
              </div>
            }

            <div>
              <label class="mb-1 block text-sm font-medium text-neutral-700">Timeout (ms)</label>
              <input
                type="number"
                [(ngModel)]="form.timeoutMs"
                min="500"
                max="30000"
                class="w-40 rounded-lg border border-neutral-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
              />
              <p class="mt-1 text-xs text-neutral-500">
                Para bots con voz se recomienda 8000 ms o menos.
              </p>
            </div>

            @if (editingConnection(); as conn) {
              <div>
                <label class="mb-1 block text-sm font-medium text-neutral-700">Estado</label>
                <select
                  [(ngModel)]="form.status"
                  class="w-full rounded-lg border border-neutral-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
                >
                  <option value="Active">Activa</option>
                  <option value="Disabled">Deshabilitada (apaga todas sus herramientas)</option>
                </select>
              </div>
            }
          </div>

          <div class="mt-6 flex justify-end gap-2">
            <button
              (click)="modalOpen.set(false)"
              class="rounded-lg border border-neutral-300 px-4 py-2 text-sm transition hover:bg-neutral-50"
            >
              Cancelar
            </button>
            <button
              (click)="onSaveConnection()"
              [disabled]="savingConnection()"
              class="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-blue-700 disabled:opacity-50"
            >
              {{ savingConnection() ? 'Guardando…' : 'Guardar' }}
            </button>
          </div>
        </div>
      </div>
    }
  `,
})
export class ToolsList implements OnInit {
  private readonly api = inject(Api);
  private readonly authService = inject(AuthService);

  readonly section = signal<'tools' | 'connections'>('tools');

  // Tools
  readonly tools = signal<ToolDefinitionResponse[]>([]);
  readonly loadingTools = signal(true);
  readonly toolsError = signal<string | null>(null);
  readonly search = signal('');
  readonly statusFilter = signal('');
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  // Connections
  readonly connections = signal<ToolConnectionResponse[]>([]);
  readonly loadingConnections = signal(true);
  readonly probing = signal<number | null>(null);

  // Connection modal
  readonly modalOpen = signal(false);
  readonly editingConnection = signal<ToolConnectionResponse | null>(null);
  readonly savingConnection = signal(false);
  form = this.emptyForm();

  readonly canEdit = computed(() => {
    const perms = this.authService.user()?.permissions ?? [];
    return (
      perms.includes('tool:update') ||
      perms.includes('bot:admin') ||
      perms.includes('superadmin')
    );
  });

  statusLabel(status: string): string {
    return STATUS_LABELS[status] ?? status;
  }

  authLabel(authType: string): string {
    return AUTH_LABELS[authType] ?? authType;
  }

  secretPlaceholder(): string {
    const conn = this.editingConnection();
    return conn?.hasSecrets ? `${conn.secretPrefix}••••••••` : '';
  }

  ngOnInit() {
    this.loadTools();
    this.loadConnections();
  }

  onSearch(value: string) {
    this.search.set(value);
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.loadTools(), 300);
  }

  onStatusFilter(value: string) {
    this.statusFilter.set(value);
    this.loadTools();
  }

  async loadTools() {
    this.loadingTools.set(true);
    this.toolsError.set(null);
    try {
      const page = await this.api.invoke(getToolDefinitions, {
        search: this.search() || undefined,
        status: this.statusFilter() || undefined,
        page: 1,
        pageSize: 100,
      });
      this.tools.set(page.items ?? []);
    } catch {
      this.toolsError.set('No se pudieron cargar las herramientas.');
    } finally {
      this.loadingTools.set(false);
    }
  }

  async loadConnections() {
    this.loadingConnections.set(true);
    try {
      this.connections.set(await this.api.invoke(getToolConnections, {}));
    } catch {
      toast.error('No se pudieron cargar las conexiones.');
    } finally {
      this.loadingConnections.set(false);
    }
  }

  openConnectionModal(conn: ToolConnectionResponse | null) {
    this.editingConnection.set(conn);
    if (conn) {
      const authConfig = this.parseJson(conn.authConfigJson);
      this.form = {
        name: conn.name,
        baseUrl: conn.baseUrl,
        authType: conn.authType,
        headerName: (authConfig['headerName'] as string) ?? 'X-Api-Key',
        paramName: (authConfig['paramName'] as string) ?? 'api_key',
        username: (authConfig['username'] as string) ?? '',
        secret: '',
        timeoutMs: conn.timeoutMs,
        status: conn.status,
      };
    } else {
      this.form = this.emptyForm();
    }
    this.modalOpen.set(true);
  }

  async onSaveConnection() {
    if (!this.form.name.trim() || !this.form.baseUrl.trim()) {
      toast.error('Nombre y URL base son obligatorios.');
      return;
    }

    this.savingConnection.set(true);
    try {
      const authConfig: Record<string, string> = {};
      if (this.form.authType === 'ApiKeyHeader' && this.form.headerName.trim()) {
        authConfig['headerName'] = this.form.headerName.trim();
      }
      if (this.form.authType === 'ApiKeyQuery' && this.form.paramName.trim()) {
        authConfig['paramName'] = this.form.paramName.trim();
      }
      if (this.form.authType === 'Basic' && this.form.username.trim()) {
        authConfig['username'] = this.form.username.trim();
      }

      const secretsJson = this.form.secret
        ? JSON.stringify(
            this.form.authType === 'Basic'
              ? { password: this.form.secret }
              : { apiKey: this.form.secret },
          )
        : null;

      const editing = this.editingConnection();
      if (editing) {
        await this.api.invoke(updateToolConnection, {
          id: editing.id,
          body: {
            name: this.form.name.trim(),
            description: null,
            baseUrl: this.form.baseUrl.trim(),
            authType: this.form.authType,
            authConfigJson: JSON.stringify(authConfig),
            secretsJson,
            defaultHeadersJson: null,
            timeoutMs: this.form.timeoutMs,
            maxResponseBytes: null,
            status: this.form.status,
          },
        });
        toast.success('Conexión actualizada.');
      } else {
        await this.api.invoke(createToolConnection, {
          body: {
            name: this.form.name.trim(),
            description: null,
            connectorType: 'Rest',
            baseUrl: this.form.baseUrl.trim(),
            authType: this.form.authType,
            authConfigJson: JSON.stringify(authConfig),
            secretsJson,
            defaultHeadersJson: null,
            timeoutMs: this.form.timeoutMs,
            maxResponseBytes: null,
          },
        });
        toast.success('Conexión creada.');
      }

      this.modalOpen.set(false);
      await this.loadConnections();
    } catch (err) {
      toast.error(this.extractError(err) ?? 'No se pudo guardar la conexión.');
    } finally {
      this.savingConnection.set(false);
    }
  }

  async onProbe(conn: ToolConnectionResponse) {
    this.probing.set(conn.id);
    try {
      const result = await this.api.invoke(probeToolConnection, { id: conn.id });
      if (result.ok) {
        toast.success(
          `Conexión alcanzable (HTTP ${result.httpStatus} · ${result.latencyMs} ms).`,
        );
      } else {
        toast.error(result.error ?? 'La conexión no respondió.');
      }
    } catch {
      toast.error('No se pudo probar la conexión.');
    } finally {
      this.probing.set(null);
    }
  }

  async onDeleteConnection(conn: ToolConnectionResponse) {
    if (!confirm(`¿Eliminar la conexión "${conn.name}"?`)) return;
    try {
      await this.api.invoke(deleteToolConnection, { id: conn.id });
      toast.info('Conexión eliminada.');
      await this.loadConnections();
    } catch (err) {
      toast.error(this.extractError(err) ?? 'No se pudo eliminar (¿tiene herramientas asociadas?).');
    }
  }

  private emptyForm() {
    return {
      name: '',
      baseUrl: '',
      authType: 'None',
      headerName: 'X-Api-Key',
      paramName: 'api_key',
      username: '',
      secret: '',
      timeoutMs: 8000,
      status: 'Active',
    };
  }

  private parseJson(json: string): Record<string, unknown> {
    try {
      return JSON.parse(json) as Record<string, unknown>;
    } catch {
      return {};
    }
  }

  private extractError(err: unknown): string | null {
    const e = err as { error?: { error?: string } };
    return e?.error?.error ?? null;
  }
}
