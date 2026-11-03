import { Component, computed, inject, signal, OnInit } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { PageShellComponent, StatusPillComponent } from '@public-intelligence/ui-core';
import { toast } from 'ngx-sonner';
import { Api } from '../../api/api';
import {
  deleteToolDefinition,
  disableToolDefinition,
  getToolConnections,
  getToolDefinition,
  getToolExecutions,
  publishToolDefinition,
  testToolDefinition,
  unpublishToolDefinition,
  updateToolDefinition,
} from '../../api/functions';
import type { ToolConnectionResponse } from '../../api/models/tool-connection-response';
import type { ToolDefinitionResponse } from '../../api/models/tool-definition-response';
import type { ToolExecutionResponse } from '../../api/models/tool-execution-response';
import type { ToolTestResponse } from '../../api/models/tool-test-response';

interface ParamRow {
  name: string;
  type: 'string' | 'integer' | 'number' | 'boolean';
  required: boolean;
  description: string;
  enumCsv: string;
}

interface KeyValueRow {
  name: string;
  value: string;
}

const STATUS_LABELS: Record<string, string> = {
  Draft: 'Borrador',
  Published: 'Publicado',
  Disabled: 'Deshabilitado',
  Archived: 'Archivado',
};

const OUTCOME_LABELS: Record<string, string> = {
  Success: 'Éxito',
  ValidationFailed: 'Argumentos inválidos',
  PolicyBlocked: 'Bloqueado por política',
  Timeout: 'Timeout',
  HttpError: 'Error HTTP',
  ConnectorError: 'Error de conexión',
  Cancelled: 'Cancelado',
};

@Component({
  selector: 'bf-tool-edit',
  imports: [DatePipe, FormsModule, RouterLink, PageShellComponent, StatusPillComponent],
  template: `
    @if (loading()) {
      <app-page-shell title="Cargando...">
        <div class="py-32 text-center text-sm text-neutral-500">Cargando herramienta…</div>
      </app-page-shell>
    } @else if (tool(); as t) {
      <app-page-shell [title]="t.displayName">
        <nav breadcrumbs>
          <ol class="flex items-center gap-1 text-sm text-neutral-500">
            <li><a routerLink="/tools" class="hover:text-neutral-900">Herramientas</a></li>
            <li class="text-neutral-300">›</li>
            <li class="font-mono text-neutral-900">{{ t.name }}</li>
          </ol>
        </nav>

        <span status>
          <app-status-pill [status]="statusLabel()" type="publication" />
        </span>

        <div actions class="flex items-center gap-2">
          <button
            (click)="onSave()"
            [disabled]="saving()"
            class="rounded-lg bg-blue-600 px-3 py-1.5 text-sm font-medium text-white transition hover:bg-blue-700 disabled:opacity-50"
          >
            {{ saving() ? 'Guardando…' : 'Guardar' }}
          </button>

          @if (t.status === 'Published') {
            <button
              (click)="onUnpublish()"
              class="rounded-lg border border-amber-500 px-3 py-1.5 text-sm font-medium text-amber-600 transition hover:bg-amber-50"
            >
              Despublicar
            </button>
          } @else {
            <button
              (click)="onPublish()"
              class="rounded-lg bg-emerald-600 px-3 py-1.5 text-sm font-medium text-white transition hover:bg-emerald-700"
            >
              Publicar
            </button>
          }

          @if (t.status !== 'Disabled') {
            <button
              (click)="onDisable()"
              class="rounded-lg border border-neutral-300 px-3 py-1.5 text-sm text-neutral-600 transition hover:bg-neutral-50"
              title="Apaga la herramienta en todos los bots de inmediato"
            >
              Deshabilitar
            </button>
          }

          <button
            (click)="onDelete()"
            aria-label="Eliminar herramienta"
            class="rounded p-1.5 text-neutral-400 transition hover:bg-neutral-100 hover:text-red-600"
          >
            <svg aria-hidden="true" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
              <path d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
            </svg>
          </button>
        </div>

        <!-- Tabs -->
        <div class="mb-6 border-b border-neutral-200">
          <nav class="-mb-px flex gap-6">
            @for (tab of tabs; track tab.id) {
              <button
                (click)="activeTab.set(tab.id)"
                class="border-b-2 pb-3 text-sm font-medium transition"
                [class]="activeTab() === tab.id
                  ? 'border-neutral-900 text-neutral-900'
                  : 'border-transparent text-neutral-500 hover:text-neutral-700'"
              >
                {{ tab.label }}
              </button>
            }
          </nav>
        </div>

        <!-- ── General ─────────────────────────────────────────────── -->
        @if (activeTab() === 'general') {
          <div class="max-w-xl space-y-4">
            <div>
              <label class="mb-1 block text-sm font-medium text-neutral-700">Nombre para mostrar</label>
              <input [(ngModel)]="form.displayName" class="w-full rounded-lg border border-neutral-300 px-3 py-2 text-sm" />
            </div>
            <div>
              <label class="mb-1 block text-sm font-medium text-neutral-700">Nombre técnico</label>
              <input [(ngModel)]="form.name" class="w-full rounded-lg border border-neutral-300 px-3 py-2 font-mono text-sm" />
            </div>
            <div>
              <label class="mb-1 block text-sm font-medium text-neutral-700">Conexión</label>
              <select [(ngModel)]="form.toolConnectionId" class="w-full rounded-lg border border-neutral-300 px-3 py-2 text-sm">
                @for (conn of connections(); track conn.id) {
                  <option [ngValue]="conn.id">{{ conn.name }} — {{ conn.baseUrl }}</option>
                }
              </select>
            </div>
            <div class="grid grid-cols-2 gap-4">
              <div>
                <label class="mb-1 block text-sm font-medium text-neutral-700">Categoría</label>
                <input [(ngModel)]="form.category" class="w-full rounded-lg border border-neutral-300 px-3 py-2 text-sm" placeholder="Trámites" />
              </div>
              <div>
                <label class="mb-1 block text-sm font-medium text-neutral-700">Clasificación de datos</label>
                <select [(ngModel)]="form.dataClassification" class="w-full rounded-lg border border-neutral-300 px-3 py-2 text-sm">
                  <option value="Public">Pública</option>
                  <option value="Internal">Interna</option>
                  <option value="Personal">Personal</option>
                  <option value="Sensitive">Sensible</option>
                </select>
              </div>
            </div>
            <div>
              <label class="mb-1 block text-sm font-medium text-neutral-700">Contacto de soporte</label>
              <input [(ngModel)]="form.supportContact" class="w-full rounded-lg border border-neutral-300 px-3 py-2 text-sm" placeholder="soporte@institucion.gob.do" />
            </div>
          </div>
        }

        <!-- ── Instrucciones IA ─────────────────────────────────────── -->
        @if (activeTab() === 'ia') {
          <div class="max-w-2xl space-y-4">
            <div>
              <label class="mb-1 block text-sm font-medium text-neutral-700">Qué hace y cuándo usarla</label>
              <textarea [(ngModel)]="form.aiDescription" rows="4" class="w-full rounded-lg border border-neutral-300 px-3 py-2 text-sm"></textarea>
              <p class="mt-1 text-xs text-neutral-500">
                Es la descripción de la función que ve el modelo; determina cuándo decide llamarla.
              </p>
            </div>
            <div>
              <label class="mb-1 block text-sm font-medium text-neutral-700">
                Notas de uso (cuándo NO usarla, requisitos previos, cómo presentar el resultado)
              </label>
              <textarea [(ngModel)]="form.aiUsageNotes" rows="6" class="w-full rounded-lg border border-neutral-300 px-3 py-2 text-sm"
                placeholder="No la uses para expedientes de otras instituciones. Pide siempre el número de expediente antes de llamarla. Comunica el estado en una frase corta."></textarea>
              <p class="mt-1 text-xs text-neutral-500">
                Se agrega a las instrucciones del sistema cuando la herramienta está activa en un bot.
              </p>
            </div>
          </div>
        }

        <!-- ── Técnica ──────────────────────────────────────────────── -->
        @if (activeTab() === 'tecnica') {
          <div class="max-w-3xl space-y-6">
            <div class="flex gap-3">
              <div>
                <label class="mb-1 block text-sm font-medium text-neutral-700">Método</label>
                <select [(ngModel)]="form.httpMethod" class="rounded-lg border border-neutral-300 px-3 py-2 text-sm">
                  <option>GET</option>
                  <option>POST</option>
                  <option>PUT</option>
                  <option>PATCH</option>
                  <option>DELETE</option>
                </select>
              </div>
              <div class="flex-1">
                <label class="mb-1 block text-sm font-medium text-neutral-700">Ruta (relativa a la conexión)</label>
                <input [(ngModel)]="form.pathTemplate" class="w-full rounded-lg border border-neutral-300 px-3 py-2 font-mono text-sm" placeholder="/expedientes/{numero}" />
                <p class="mt-1 text-xs text-neutral-500">
                  Usa <code class="rounded bg-neutral-100 px-1">&#123;parametro&#125;</code> para insertar valores; los valores se codifican automáticamente.
                </p>
              </div>
            </div>

            <!-- Parameters -->
            <div>
              <div class="mb-2 flex items-center justify-between">
                <label class="text-sm font-medium text-neutral-700">Parámetros (lo que el modelo debe proporcionar)</label>
                <button (click)="params.set([...params(), emptyParam()])" class="text-sm text-blue-600 hover:underline">+ Agregar</button>
              </div>
              @if (params().length === 0) {
                <p class="rounded-lg border border-dashed border-neutral-300 p-4 text-center text-xs text-neutral-500">
                  Sin parámetros: la herramienta se llama sin argumentos.
                </p>
              }
              @for (p of params(); track $index; let i = $index) {
                <div class="mb-2 grid grid-cols-12 items-center gap-2 rounded-lg border border-neutral-200 p-2">
                  <input [(ngModel)]="p.name" placeholder="numero" class="col-span-2 rounded border border-neutral-300 px-2 py-1.5 font-mono text-xs" />
                  <select [(ngModel)]="p.type" class="col-span-2 rounded border border-neutral-300 px-2 py-1.5 text-xs">
                    <option value="string">texto</option>
                    <option value="integer">entero</option>
                    <option value="number">decimal</option>
                    <option value="boolean">sí/no</option>
                  </select>
                  <label class="col-span-1 flex items-center gap-1 text-xs text-neutral-600">
                    <input type="checkbox" [(ngModel)]="p.required" /> req.
                  </label>
                  <input [(ngModel)]="p.description" placeholder="Descripción para el modelo (en español)" class="col-span-4 rounded border border-neutral-300 px-2 py-1.5 text-xs" />
                  <input [(ngModel)]="p.enumCsv" placeholder="valores permitidos (a,b,c)" class="col-span-2 rounded border border-neutral-300 px-2 py-1.5 text-xs" />
                  <button (click)="removeParam(i)" class="col-span-1 text-xs text-red-500 hover:underline">Quitar</button>
                </div>
              }
            </div>

            <!-- Query params -->
            <div>
              <div class="mb-2 flex items-center justify-between">
                <label class="text-sm font-medium text-neutral-700">Parámetros de query</label>
                <button (click)="queryRows.set([...queryRows(), { name: '', value: '' }])" class="text-sm text-blue-600 hover:underline">+ Agregar</button>
              </div>
              @for (q of queryRows(); track $index; let i = $index) {
                <div class="mb-2 flex items-center gap-2">
                  <input [(ngModel)]="q.name" placeholder="tipo" class="w-40 rounded border border-neutral-300 px-2 py-1.5 font-mono text-xs" />
                  <input [(ngModel)]="q.value" placeholder="&#123;tipo&#125; o valor fijo" class="flex-1 rounded border border-neutral-300 px-2 py-1.5 font-mono text-xs" />
                  <button (click)="removeRow(queryRows, i)" class="text-xs text-red-500 hover:underline">Quitar</button>
                </div>
              }
            </div>

            <!-- Headers -->
            <div>
              <div class="mb-2 flex items-center justify-between">
                <label class="text-sm font-medium text-neutral-700">Headers adicionales (no secretos)</label>
                <button (click)="headerRows.set([...headerRows(), { name: '', value: '' }])" class="text-sm text-blue-600 hover:underline">+ Agregar</button>
              </div>
              @for (h of headerRows(); track $index; let i = $index) {
                <div class="mb-2 flex items-center gap-2">
                  <input [(ngModel)]="h.name" placeholder="Accept" class="w-40 rounded border border-neutral-300 px-2 py-1.5 font-mono text-xs" />
                  <input [(ngModel)]="h.value" placeholder="application/json" class="flex-1 rounded border border-neutral-300 px-2 py-1.5 font-mono text-xs" />
                  <button (click)="removeRow(headerRows, i)" class="text-xs text-red-500 hover:underline">Quitar</button>
                </div>
              }
            </div>

            @if (form.httpMethod !== 'GET') {
              <div>
                <label class="mb-1 block text-sm font-medium text-neutral-700">Cuerpo (plantilla JSON)</label>
                <textarea [(ngModel)]="form.bodyTemplateJson" rows="5" class="w-full rounded-lg border border-neutral-300 px-3 py-2 font-mono text-xs"
                  placeholder="Plantilla JSON con &#123;parametros&#125;"></textarea>
                <p class="mt-1 text-xs text-neutral-500">
                  Un valor que sea exactamente "&#123;parametro&#125;" conserva el tipo del argumento (números siguen siendo números).
                </p>
              </div>
            }

            <!-- Response mapping -->
            <div>
              <div class="mb-2 flex items-center justify-between">
                <label class="text-sm font-medium text-neutral-700">
                  Selección de respuesta (opcional — vacío devuelve la respuesta completa)
                </label>
                <button (click)="mappingRows.set([...mappingRows(), { name: '', value: '' }])" class="text-sm text-blue-600 hover:underline">+ Agregar</button>
              </div>
              @for (m of mappingRows(); track $index; let i = $index) {
                <div class="mb-2 flex items-center gap-2">
                  <input [(ngModel)]="m.value" placeholder="data.estado (ruta en la respuesta)" class="flex-1 rounded border border-neutral-300 px-2 py-1.5 font-mono text-xs" />
                  <input [(ngModel)]="m.name" placeholder="estado (alias)" class="w-40 rounded border border-neutral-300 px-2 py-1.5 font-mono text-xs" />
                  <button (click)="removeRow(mappingRows, i)" class="text-xs text-red-500 hover:underline">Quitar</button>
                </div>
              }
            </div>

            <!-- Error mapping -->
            <div>
              <div class="mb-2 flex items-center justify-between">
                <label class="text-sm font-medium text-neutral-700">Mensajes por código de error</label>
                <button (click)="errorRows.set([...errorRows(), { name: '', value: '' }])" class="text-sm text-blue-600 hover:underline">+ Agregar</button>
              </div>
              @for (e of errorRows(); track $index; let i = $index) {
                <div class="mb-2 flex items-center gap-2">
                  <input [(ngModel)]="e.name" placeholder="404 (o default)" class="w-32 rounded border border-neutral-300 px-2 py-1.5 font-mono text-xs" />
                  <input [(ngModel)]="e.value" placeholder="No se encontró el expediente." class="flex-1 rounded border border-neutral-300 px-2 py-1.5 text-xs" />
                  <button (click)="removeRow(errorRows, i)" class="text-xs text-red-500 hover:underline">Quitar</button>
                </div>
              }
            </div>

            <!-- Diacritics normalization -->
            <label class="flex items-start gap-3">
              <input type="checkbox" [(ngModel)]="form.normalizeArgDiacritics" class="mt-0.5" />
              <span>
                <span class="block text-sm font-medium text-neutral-700">Normalizar tildes en argumentos</span>
                <span class="block text-xs text-neutral-500">
                  Quita acentos de los argumentos antes de enviarlos (á→a, ñ→n). Útil para APIs que
                  buscan o geocodifican mejor sin acentos (p. ej. clima).
                </span>
              </span>
            </label>
          </div>
        }

        <!-- ── Políticas ────────────────────────────────────────────── -->
        @if (activeTab() === 'politicas') {
          <div class="max-w-xl space-y-5">
            <label class="flex items-start gap-3">
              <input type="checkbox" [(ngModel)]="form.requiresAuthenticatedUser" class="mt-0.5" />
              <span>
                <span class="block text-sm font-medium text-neutral-700">Requiere usuario autenticado</span>
                <span class="block text-xs text-neutral-500">
                  Si el bot es público y el usuario no inició sesión, la herramienta se bloquea y el bot
                  le pide iniciar sesión. Desactívalo solo para información realmente pública (p. ej. clima).
                </span>
              </span>
            </label>

            <div>
              <label class="mb-1 block text-sm font-medium text-neutral-700">Máximo de llamadas por conversación</label>
              <input type="number" [(ngModel)]="form.maxCallsPerConversation" min="0" class="w-40 rounded-lg border border-neutral-300 px-3 py-2 text-sm" />
              <p class="mt-1 text-xs text-neutral-500">0 o vacío = sin límite.</p>
            </div>

            <div>
              <label class="mb-1 block text-sm font-medium text-neutral-700">Timeout específico (ms)</label>
              <input type="number" [(ngModel)]="form.timeoutMsOverride" min="0" max="30000" class="w-40 rounded-lg border border-neutral-300 px-3 py-2 text-sm" />
              <p class="mt-1 text-xs text-neutral-500">
                0 o vacío = usa el timeout de la conexión. En bots con voz, mantenlo por debajo de 15000 ms.
              </p>
            </div>

            <div class="rounded-lg border border-neutral-200 bg-neutral-50 p-4 text-xs text-neutral-600">
              En esta versión todas las herramientas son de <strong>solo lectura</strong>: las operaciones
              que modifican estado (crear citas, enviar solicitudes) llegarán en una fase posterior con
              confirmación explícita del usuario.
            </div>
          </div>
        }

        <!-- ── Pruebas ──────────────────────────────────────────────── -->
        @if (activeTab() === 'pruebas') {
          <div class="max-w-2xl space-y-4">
            <p class="text-sm text-neutral-500">
              Ejecuta la herramienta con valores de prueba a través del mismo pipeline que usa el bot
              (validación, guardas de red, credenciales). La ejecución queda registrada como prueba.
            </p>

            @if (params().length === 0) {
              <p class="text-sm text-neutral-500">Esta herramienta no define parámetros.</p>
            }
            @for (p of params(); track p.name) {
              <div>
                <label class="mb-1 block text-sm font-medium text-neutral-700">
                  {{ p.name }}
                  @if (p.required) { <span class="text-red-500">*</span> }
                </label>
                @if (p.type === 'boolean') {
                  <input type="checkbox" [checked]="testArgs()[p.name] === 'true'" (change)="setTestArg(p.name, $any($event.target).checked ? 'true' : 'false')" />
                } @else {
                  <input
                    [type]="p.type === 'string' ? 'text' : 'number'"
                    [value]="testArgs()[p.name] ?? ''"
                    (input)="setTestArg(p.name, $any($event.target).value)"
                    class="w-full rounded-lg border border-neutral-300 px-3 py-2 text-sm"
                    [placeholder]="p.description"
                  />
                }
              </div>
            }

            <button
              (click)="onRunTest()"
              [disabled]="testing()"
              class="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-blue-700 disabled:opacity-50"
            >
              {{ testing() ? 'Ejecutando…' : '▶ Ejecutar prueba' }}
            </button>

            @if (testResult(); as r) {
              <div class="rounded-lg border p-4 text-sm"
                [class]="r.outcome === 'Success' ? 'border-emerald-200 bg-emerald-50' : 'border-red-200 bg-red-50'">
                <div class="mb-2 flex items-center gap-3">
                  <span class="font-semibold">{{ outcomeLabel(r.outcome) }}</span>
                  @if (r.httpStatus) { <span class="text-xs">HTTP {{ r.httpStatus }}</span> }
                  <span class="text-xs">{{ r.latencyMs }} ms</span>
                </div>
                <div class="mb-2 font-mono text-xs text-neutral-600">{{ r.resolvedRequestSummary }}</div>
                @if (r.validationErrors && r.validationErrors.length > 0) {
                  <ul class="mb-2 list-inside list-disc text-xs text-red-700">
                    @for (v of r.validationErrors; track v) { <li>{{ v }}</li> }
                  </ul>
                }
                @if (r.errorMessage) {
                  <div class="mb-2 text-xs text-red-700">{{ r.errorMessage }}</div>
                }
                @if (r.redactedResponseJson) {
                  <pre class="max-h-64 overflow-auto rounded bg-white p-2 font-mono text-xs">{{ prettyJson(r.redactedResponseJson) }}</pre>
                }
              </div>
            }
          </div>
        }

        <!-- ── Historial ────────────────────────────────────────────── -->
        @if (activeTab() === 'historial') {
          @if (loadingExecutions()) {
            <div class="py-16 text-center text-sm text-neutral-500">Cargando ejecuciones…</div>
          } @else if (executions().length === 0) {
            <div class="py-16 text-center text-sm text-neutral-500">Aún no hay ejecuciones de esta herramienta.</div>
          } @else {
            <div class="overflow-x-auto rounded-lg border border-neutral-200">
              <table class="w-full text-sm">
                <thead class="bg-neutral-50 text-left text-xs uppercase text-neutral-500">
                  <tr>
                    <th class="px-4 py-3">Fecha</th>
                    <th class="px-4 py-3">Resultado</th>
                    <th class="px-4 py-3">HTTP</th>
                    <th class="px-4 py-3">Latencia</th>
                    <th class="px-4 py-3">Solicitud</th>
                    <th class="px-4 py-3">Tipo</th>
                  </tr>
                </thead>
                <tbody class="divide-y divide-neutral-100">
                  @for (e of executions(); track e.id) {
                    <tr class="cursor-pointer hover:bg-neutral-50" (click)="selectedExecution.set(selectedExecution()?.id === e.id ? null : e)">
                      <td class="px-4 py-2 text-xs text-neutral-600">{{ e.createdAt | date: 'dd/MM/yy HH:mm:ss' }}</td>
                      <td class="px-4 py-2">
                        <span class="rounded-full px-2 py-0.5 text-xs font-medium"
                          [class]="e.outcome === 'Success' ? 'bg-emerald-100 text-emerald-700' : 'bg-red-100 text-red-700'">
                          {{ outcomeLabel(e.outcome) }}
                        </span>
                      </td>
                      <td class="px-4 py-2 text-xs">{{ e.httpStatus ?? '—' }}</td>
                      <td class="px-4 py-2 text-xs">{{ e.latencyMs }} ms</td>
                      <td class="max-w-md truncate px-4 py-2 font-mono text-xs text-neutral-600">{{ e.resolvedRequestSummary }}</td>
                      <td class="px-4 py-2 text-xs">{{ e.isTest ? 'Prueba' : 'Conversación' }}</td>
                    </tr>
                    @if (selectedExecution()?.id === e.id) {
                      <tr>
                        <td colspan="6" class="bg-neutral-50 px-4 py-3">
                          <div class="grid gap-3 md:grid-cols-2">
                            <div>
                              <div class="mb-1 text-xs font-medium text-neutral-500">Argumentos</div>
                              <pre class="max-h-48 overflow-auto rounded bg-white p-2 font-mono text-xs">{{ prettyJson(e.argumentsJson) }}</pre>
                            </div>
                            <div>
                              <div class="mb-1 text-xs font-medium text-neutral-500">
                                {{ e.errorMessage ? 'Error' : 'Respuesta (redactada)' }}
                              </div>
                              <pre class="max-h-48 overflow-auto rounded bg-white p-2 font-mono text-xs">{{ e.errorMessage ?? prettyJson(e.resultSnippetJson ?? '{}') }}</pre>
                            </div>
                          </div>
                        </td>
                      </tr>
                    }
                  }
                </tbody>
              </table>
            </div>
          }
        }
      </app-page-shell>
    }
  `,
})
export class ToolEdit implements OnInit {
  private readonly api = inject(Api);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly tabs = [
    { id: 'general', label: 'General' },
    { id: 'ia', label: 'Instrucciones IA' },
    { id: 'tecnica', label: 'Técnica' },
    { id: 'politicas', label: 'Políticas' },
    { id: 'pruebas', label: 'Pruebas' },
    { id: 'historial', label: 'Historial' },
  ] as const;

  readonly tool = signal<ToolDefinitionResponse | null>(null);
  readonly connections = signal<ToolConnectionResponse[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly activeTab = signal<string>('general');

  readonly params = signal<ParamRow[]>([]);
  readonly queryRows = signal<KeyValueRow[]>([]);
  readonly headerRows = signal<KeyValueRow[]>([]);
  readonly mappingRows = signal<KeyValueRow[]>([]);
  readonly errorRows = signal<KeyValueRow[]>([]);

  readonly testArgs = signal<Record<string, string>>({});
  readonly testing = signal(false);
  readonly testResult = signal<ToolTestResponse | null>(null);

  readonly executions = signal<ToolExecutionResponse[]>([]);
  readonly loadingExecutions = signal(false);
  readonly selectedExecution = signal<ToolExecutionResponse | null>(null);

  form = {
    name: '',
    displayName: '',
    category: '',
    supportContact: '',
    dataClassification: 'Internal',
    toolConnectionId: 0,
    aiDescription: '',
    aiUsageNotes: '',
    httpMethod: 'GET',
    pathTemplate: '/',
    bodyTemplateJson: '',
    normalizeArgDiacritics: false,
    requiresAuthenticatedUser: true,
    maxCallsPerConversation: 0,
    timeoutMsOverride: 0,
  };

  readonly statusLabel = computed(() => {
    const t = this.tool();
    return t ? (STATUS_LABELS[t.status] ?? t.status) : '';
  });

  outcomeLabel(outcome: string): string {
    return OUTCOME_LABELS[outcome] ?? outcome;
  }

  emptyParam(): ParamRow {
    return { name: '', type: 'string', required: true, description: '', enumCsv: '' };
  }

  removeParam(index: number) {
    this.params.set(this.params().filter((_, i) => i !== index));
  }

  removeRow(rows: ReturnType<typeof signal<KeyValueRow[]>>, index: number) {
    rows.set(rows().filter((_, i) => i !== index));
  }

  setTestArg(name: string, value: string) {
    this.testArgs.set({ ...this.testArgs(), [name]: value });
  }

  prettyJson(json: string): string {
    try {
      return JSON.stringify(JSON.parse(json), null, 2);
    } catch {
      return json;
    }
  }

  async ngOnInit() {
    const id = Number(this.route.snapshot.params['id']);
    try {
      const [tool, connections] = await Promise.all([
        this.api.invoke(getToolDefinition, { id }),
        this.api.invoke(getToolConnections, {}),
      ]);
      this.connections.set(connections);
      this.applyTool(tool);
    } catch {
      toast.error('No se pudo cargar la herramienta.');
      await this.router.navigate(['/tools']);
      return;
    } finally {
      this.loading.set(false);
    }

    void this.loadExecutions();
  }

  private applyTool(tool: ToolDefinitionResponse) {
    this.tool.set(tool);
    this.form = {
      name: tool.name,
      displayName: tool.displayName,
      category: tool.category ?? '',
      supportContact: tool.supportContact ?? '',
      dataClassification: tool.dataClassification,
      toolConnectionId: tool.toolConnectionId,
      aiDescription: tool.aiDescription,
      aiUsageNotes: tool.aiUsageNotes ?? '',
      httpMethod: tool.httpMethod,
      pathTemplate: tool.pathTemplate,
      bodyTemplateJson: tool.bodyTemplateJson ?? '',
      normalizeArgDiacritics: tool.normalizeArgDiacritics,
      requiresAuthenticatedUser: tool.requiresAuthenticatedUser,
      maxCallsPerConversation: tool.maxCallsPerConversation ?? 0,
      timeoutMsOverride: tool.timeoutMsOverride ?? 0,
    };
    this.params.set(this.schemaToParams(tool.parametersSchemaJson));
    this.queryRows.set(this.jsonToRows(tool.queryParamsJson));
    this.headerRows.set(this.jsonToRows(tool.staticHeadersJson));
    this.errorRows.set(this.jsonToRows(tool.errorMappingJson));
    this.mappingRows.set(this.mappingToRows(tool.responseMappingJson));
  }

  async onSave() {
    this.saving.set(true);
    try {
      const updated = await this.api.invoke(updateToolDefinition, {
        id: this.tool()!.id,
        body: {
          name: this.form.name.trim(),
          displayName: this.form.displayName.trim(),
          category: this.form.category,
          supportContact: this.form.supportContact,
          dataClassification: this.form.dataClassification,
          toolConnectionId: this.form.toolConnectionId,
          aiDescription: this.form.aiDescription.trim(),
          aiUsageNotes: this.form.aiUsageNotes,
          httpMethod: this.form.httpMethod,
          pathTemplate: this.form.pathTemplate.trim(),
          parametersSchemaJson: this.paramsToSchema(),
          queryParamsJson: this.rowsToJson(this.queryRows()),
          staticHeadersJson: this.rowsToJson(this.headerRows()),
          bodyTemplateJson: this.form.httpMethod === 'GET' ? '' : this.form.bodyTemplateJson,
          responseMappingJson: this.rowsToMapping(this.mappingRows()),
          errorMappingJson: this.rowsToJson(this.errorRows()),
          normalizeArgDiacritics: this.form.normalizeArgDiacritics,
          requiresAuthenticatedUser: this.form.requiresAuthenticatedUser,
          maxCallsPerConversation: this.form.maxCallsPerConversation || 0,
          timeoutMsOverride: this.form.timeoutMsOverride || 0,
        },
      });
      this.applyTool(updated);
      toast.success('Herramienta guardada.');
    } catch (err) {
      toast.error(this.extractError(err) ?? 'No se pudo guardar la herramienta.');
    } finally {
      this.saving.set(false);
    }
  }

  async onPublish() {
    try {
      await this.api.invoke(publishToolDefinition, { id: this.tool()!.id });
      this.tool.set({ ...this.tool()!, status: 'Published' });
      toast.success('Herramienta publicada. Los bots asociados ya pueden usarla.');
    } catch (err) {
      toast.error(this.extractError(err) ?? 'No se pudo publicar.');
    }
  }

  async onUnpublish() {
    try {
      await this.api.invoke(unpublishToolDefinition, { id: this.tool()!.id });
      this.tool.set({ ...this.tool()!, status: 'Draft' });
      toast.info('Herramienta despublicada.');
    } catch {
      toast.error('No se pudo despublicar.');
    }
  }

  async onDisable() {
    if (!confirm('¿Deshabilitar esta herramienta? Dejará de funcionar en todos los bots de inmediato.')) return;
    try {
      await this.api.invoke(disableToolDefinition, { id: this.tool()!.id });
      this.tool.set({ ...this.tool()!, status: 'Disabled' });
      toast.info('Herramienta deshabilitada.');
    } catch {
      toast.error('No se pudo deshabilitar.');
    }
  }

  async onDelete() {
    const t = this.tool();
    if (!t || !confirm(`¿Eliminar la herramienta "${t.displayName}"?`)) return;
    try {
      await this.api.invoke(deleteToolDefinition, { id: t.id });
      toast.info('Herramienta eliminada.');
      await this.router.navigate(['/tools']);
    } catch (err) {
      toast.error(this.extractError(err) ?? 'No se pudo eliminar (¿está asociada a bots?).');
    }
  }

  async onRunTest() {
    const args: Record<string, unknown> = {};
    for (const p of this.params()) {
      const raw = this.testArgs()[p.name];
      if (raw === undefined || raw === '') continue;
      if (p.type === 'integer' || p.type === 'number') args[p.name] = Number(raw);
      else if (p.type === 'boolean') args[p.name] = raw === 'true';
      else args[p.name] = raw;
    }

    this.testing.set(true);
    this.testResult.set(null);
    try {
      const result = await this.api.invoke(testToolDefinition, {
        id: this.tool()!.id,
        body: { arguments: args },
      });
      this.testResult.set(result);
      void this.loadExecutions();
    } catch {
      toast.error('No se pudo ejecutar la prueba.');
    } finally {
      this.testing.set(false);
    }
  }

  async loadExecutions() {
    this.loadingExecutions.set(true);
    try {
      const page = await this.api.invoke(getToolExecutions, {
        id: this.tool()!.id,
        page: 1,
        pageSize: 25,
      });
      this.executions.set(page.items ?? []);
    } catch {
      /* the tab shows the empty state */
    } finally {
      this.loadingExecutions.set(false);
    }
  }

  // ── Schema/rows conversion ───────────────────────────────────────────────

  private schemaToParams(schemaJson: string): ParamRow[] {
    try {
      const schema = JSON.parse(schemaJson) as {
        properties?: Record<string, { type?: string; description?: string; enum?: string[] }>;
        required?: string[];
      };
      const required = new Set(schema.required ?? []);
      return Object.entries(schema.properties ?? {}).map(([name, prop]) => ({
        name,
        type: (prop.type as ParamRow['type']) ?? 'string',
        required: required.has(name),
        description: prop.description ?? '',
        enumCsv: (prop.enum ?? []).join(','),
      }));
    } catch {
      return [];
    }
  }

  private paramsToSchema(): string {
    const properties: Record<string, unknown> = {};
    const required: string[] = [];
    for (const p of this.params()) {
      if (!p.name.trim()) continue;
      const prop: Record<string, unknown> = { type: p.type };
      if (p.description.trim()) prop['description'] = p.description.trim();
      const enumValues = p.enumCsv.split(',').map((v) => v.trim()).filter(Boolean);
      if (enumValues.length > 0) prop['enum'] = enumValues;
      properties[p.name.trim()] = prop;
      if (p.required) required.push(p.name.trim());
    }
    const schema: Record<string, unknown> = { type: 'object', properties };
    if (required.length > 0) schema['required'] = required;
    return JSON.stringify(schema);
  }

  private jsonToRows(json: string | null | undefined): KeyValueRow[] {
    if (!json) return [];
    try {
      return Object.entries(JSON.parse(json) as Record<string, string>).map(
        ([name, value]) => ({ name, value: String(value) }),
      );
    } catch {
      return [];
    }
  }

  private rowsToJson(rows: KeyValueRow[]): string {
    const entries = rows.filter((r) => r.name.trim());
    if (entries.length === 0) return '';
    return JSON.stringify(Object.fromEntries(entries.map((r) => [r.name.trim(), r.value])));
  }

  private mappingToRows(json: string | null | undefined): KeyValueRow[] {
    if (!json) return [];
    try {
      return (JSON.parse(json) as { path?: string; name?: string }[]).map((m) => ({
        name: m.name ?? '',
        value: m.path ?? '',
      }));
    } catch {
      return [];
    }
  }

  private rowsToMapping(rows: KeyValueRow[]): string {
    const entries = rows.filter((r) => r.value.trim());
    if (entries.length === 0) return '';
    return JSON.stringify(entries.map((r) => ({ path: r.value.trim(), name: r.name.trim() || r.value.trim() })));
  }

  private extractError(err: unknown): string | null {
    const e = err as { error?: { error?: string } };
    return e?.error?.error ?? null;
  }
}
