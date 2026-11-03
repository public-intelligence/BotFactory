import { Component, inject, signal, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { PageShellComponent } from '@public-intelligence/ui-core';
import { toast } from 'ngx-sonner';
import { Api } from '../../api/api';
import { createToolDefinition, getToolConnections } from '../../api/functions';
import type { ToolConnectionResponse } from '../../api/models/tool-connection-response';

@Component({
  selector: 'bf-tool-create',
  imports: [FormsModule, RouterLink, PageShellComponent],
  template: `
    <app-page-shell title="Nueva herramienta">
      <nav breadcrumbs>
        <ol class="flex items-center gap-1 text-sm text-neutral-500">
          <li><a routerLink="/tools" class="hover:text-neutral-900">Herramientas</a></li>
          <li class="text-neutral-300">›</li>
          <li class="text-neutral-900">Nueva</li>
        </ol>
      </nav>

      <div class="max-w-xl space-y-4">
        @if (connections().length === 0 && !loading()) {
          <div class="rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-800">
            Primero crea una <a routerLink="/tools" class="font-medium underline">conexión</a> al
            servicio externo; toda herramienta pertenece a una conexión.
          </div>
        }

        <div>
          <label class="mb-1 block text-sm font-medium text-neutral-700">Nombre para mostrar</label>
          <input
            [ngModel]="displayName()"
            (ngModelChange)="onDisplayNameChange($event)"
            class="w-full rounded-lg border border-neutral-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
            placeholder="Consultar estado de expediente"
          />
        </div>

        <div>
          <label class="mb-1 block text-sm font-medium text-neutral-700">Nombre técnico</label>
          <input
            [ngModel]="name()"
            (ngModelChange)="onNameChange($event)"
            class="w-full rounded-lg border border-neutral-300 px-3 py-2 font-mono text-sm focus:border-blue-500 focus:outline-none"
            placeholder="consultar_expediente"
          />
          <p class="mt-1 text-xs text-neutral-500">
            snake_case; es el nombre que ve el modelo. Los nombres del sistema (kb_search) están reservados.
          </p>
        </div>

        <div>
          <label class="mb-1 block text-sm font-medium text-neutral-700">Conexión</label>
          <select
            [(ngModel)]="connectionId"
            class="w-full rounded-lg border border-neutral-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
          >
            <option [ngValue]="null">Selecciona una conexión…</option>
            @for (conn of connections(); track conn.id) {
              <option [ngValue]="conn.id">{{ conn.name }} — {{ conn.baseUrl }}</option>
            }
          </select>
        </div>

        <div>
          <label class="mb-1 block text-sm font-medium text-neutral-700">
            Descripción para la IA
          </label>
          <textarea
            [(ngModel)]="aiDescription"
            rows="3"
            class="w-full rounded-lg border border-neutral-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
            placeholder="Consulta el estado actual de un expediente del ministerio a partir de su número."
          ></textarea>
          <p class="mt-1 text-xs text-neutral-500">
            El modelo usa este texto para decidir cuándo llamar la herramienta.
          </p>
        </div>

        <div class="flex gap-2 pt-2">
          <button
            (click)="onCreate()"
            [disabled]="creating()"
            class="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-blue-700 disabled:opacity-50"
          >
            {{ creating() ? 'Creando…' : 'Crear herramienta' }}
          </button>
          <a
            routerLink="/tools"
            class="rounded-lg border border-neutral-300 px-4 py-2 text-sm transition hover:bg-neutral-50"
          >
            Cancelar
          </a>
        </div>
      </div>
    </app-page-shell>
  `,
})
export class ToolCreate implements OnInit {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  readonly connections = signal<ToolConnectionResponse[]>([]);
  readonly loading = signal(true);
  readonly creating = signal(false);
  readonly displayName = signal('');
  readonly name = signal('');
  private nameEdited = false;

  connectionId: number | null = null;
  aiDescription = '';

  async ngOnInit() {
    try {
      this.connections.set(await this.api.invoke(getToolConnections, {}));
    } catch {
      toast.error('No se pudieron cargar las conexiones.');
    } finally {
      this.loading.set(false);
    }
  }

  onDisplayNameChange(value: string) {
    this.displayName.set(value);
    if (!this.nameEdited) {
      this.name.set(this.slugify(value));
    }
  }

  onNameChange(value: string) {
    this.nameEdited = value.length > 0;
    this.name.set(value);
  }

  async onCreate() {
    if (!this.displayName().trim() || !this.name().trim()) {
      toast.error('Nombre y nombre técnico son obligatorios.');
      return;
    }
    if (this.connectionId === null) {
      toast.error('Selecciona una conexión.');
      return;
    }
    if (!this.aiDescription.trim()) {
      toast.error('La descripción para la IA es obligatoria.');
      return;
    }

    this.creating.set(true);
    try {
      const tool = await this.api.invoke(createToolDefinition, {
        body: {
          toolConnectionId: this.connectionId,
          name: this.name().trim(),
          displayName: this.displayName().trim(),
          category: null,
          supportContact: null,
          aiDescription: this.aiDescription.trim(),
          aiUsageNotes: null,
          httpMethod: 'GET',
          pathTemplate: '/',
          queryParamsJson: null,
          staticHeadersJson: null,
          bodyTemplateJson: null,
          parametersSchemaJson: '{"type":"object","properties":{}}',
          responseMappingJson: null,
          errorMappingJson: null,
          normalizeArgDiacritics: false,
          requiresAuthenticatedUser: true,
          maxCallsPerConversation: null,
          timeoutMsOverride: null,
          dataClassification: 'Internal',
        },
      });
      toast.success('Herramienta creada. Completa la configuración técnica.');
      await this.router.navigate(['/tools', tool.id, 'edit']);
    } catch (err) {
      const e = err as { error?: { error?: string } };
      toast.error(e?.error?.error ?? 'No se pudo crear la herramienta.');
    } finally {
      this.creating.set(false);
    }
  }

  private slugify(value: string): string {
    return value
      .normalize('NFD')
      .replace(/[̀-ͯ]/g, '')
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '_')
      .replace(/^_+|_+$/g, '')
      .replace(/^[0-9]+/, '')
      .slice(0, 64);
  }
}
