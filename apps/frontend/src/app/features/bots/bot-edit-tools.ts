import { Component, inject, signal, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { toast } from 'ngx-sonner';
import { Api } from '../../api/api';
import { getBotTools, updateBotTools } from '../../api/functions';
import type { BotToolResponse } from '../../api/models/bot-tool-response';
import {
  mapBotToolResponseToRow,
  mapBotToolRowsToUpdatePayload,
  type BotToolRow,
} from './bot-edit-tools.helpers';

/**
 * Herramientas tab on the bot editor: enables the organization's published
 * tools for this bot. kb_search is shown as a read-only system row — it is
 * always active and cannot be configured (external-tools plan §4.5).
 */
@Component({
  selector: 'bf-bot-edit-tools',
  imports: [FormsModule, RouterLink],
  template: `
    @if (loading()) {
      <div class="py-16 text-center text-sm text-neutral-500">Cargando herramientas…</div>
    } @else {
      <div class="max-w-3xl space-y-4">
        <p class="text-sm text-neutral-500">
          Selecciona qué herramientas externas puede usar este bot. Solo aparecen herramientas
          <strong>publicadas</strong> de tu organización;
          <a routerLink="/tools" class="text-blue-600 hover:underline">adminístralas aquí</a>.
        </p>

        <!-- System tool: always active, not configurable -->
        <div class="flex items-center justify-between rounded-lg border border-neutral-200 bg-neutral-50 p-4">
          <div>
            <div class="flex items-center gap-2">
              <span class="font-medium text-neutral-900">Base de conocimiento</span>
              <span class="rounded-full bg-neutral-200 px-2 py-0.5 text-xs text-neutral-600">
                herramienta del sistema
              </span>
            </div>
            <p class="mt-1 text-xs text-neutral-500">
              Búsqueda en los documentos del bot (kb_search). Siempre activa; se gestiona desde la
              pestaña Base de Conocimiento.
            </p>
          </div>
          <span class="text-xs font-medium text-emerald-600">Siempre activa</span>
        </div>

        @if (rows().length === 0) {
          <div class="rounded-lg border border-dashed border-neutral-300 p-8 text-center text-sm text-neutral-500">
            Tu organización aún no tiene herramientas publicadas.
          </div>
        }

        @for (row of rows(); track row.toolDefinitionId) {
          <div class="rounded-lg border border-neutral-200 p-4">
            <div class="flex items-center justify-between">
              <div>
                <div class="flex items-center gap-2">
                  <span class="font-medium text-neutral-900">{{ row.displayName }}</span>
                  <span class="font-mono text-xs text-neutral-400">{{ row.name }}</span>
                  @if (row.requiresAuthenticatedUser) {
                    <span
                      class="rounded-full bg-amber-100 px-2 py-0.5 text-xs text-amber-700"
                      title="Bloqueada para usuarios anónimos en bots públicos"
                    >
                      requiere sesión
                    </span>
                  }
                </div>
                @if (row.category) {
                  <div class="mt-0.5 text-xs text-neutral-500">{{ row.category }}</div>
                }
              </div>

              <button
                role="switch"
                [attr.aria-checked]="row.enabled"
                (click)="row.enabled = !row.enabled; markDirty()"
                class="relative inline-flex h-6 w-11 items-center rounded-full transition"
                [class]="row.enabled ? 'bg-emerald-500' : 'bg-neutral-300'"
              >
                <span
                  class="inline-block h-4 w-4 transform rounded-full bg-white transition"
                  [class]="row.enabled ? 'translate-x-6' : 'translate-x-1'"
                ></span>
              </button>
            </div>

            @if (row.enabled) {
              <button
                (click)="row.showInstructions = !row.showInstructions"
                class="mt-2 text-xs text-blue-600 hover:underline"
              >
                {{ row.showInstructions ? 'Ocultar' : 'Instrucciones específicas para este bot' }}
              </button>
              @if (row.showInstructions) {
                <textarea
                  [(ngModel)]="row.additionalInstructions"
                  (ngModelChange)="markDirty()"
                  rows="2"
                  maxlength="2000"
                  class="mt-2 w-full rounded-lg border border-neutral-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
                  placeholder="Ej.: En este bot, pide siempre la cédula antes de consultar."
                ></textarea>
              }
            }
          </div>
        }

        @if (rows().length > 0) {
          <div class="flex justify-end">
            <button
              (click)="onSave()"
              [disabled]="saving() || !dirty()"
              class="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-blue-700 disabled:opacity-50"
            >
              {{ saving() ? 'Guardando…' : 'Guardar cambios' }}
            </button>
          </div>
        }
      </div>
    }
  `,
})
export class BotEditTools implements OnInit {
  private readonly api = inject(Api);
  private readonly route = inject(ActivatedRoute);

  readonly rows = signal<BotToolRow[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly dirty = signal(false);

  private botId = 0;

  ngOnInit() {
    this.botId = Number(this.route.parent?.snapshot.params['id']);
    void this.load();
  }

  markDirty() {
    this.dirty.set(true);
  }

  private async load() {
    this.loading.set(true);
    try {
      const tools = await this.api.invoke(getBotTools, { botId: this.botId });
      this.rows.set(tools.map((t: BotToolResponse) => mapBotToolResponseToRow(t)));
    } catch {
      toast.error('No se pudieron cargar las herramientas del bot.');
    } finally {
      this.loading.set(false);
    }
  }

  async onSave() {
    this.saving.set(true);
    try {
      await this.api.invoke(updateBotTools, {
        botId: this.botId,
        body: {
          tools: mapBotToolRowsToUpdatePayload(this.rows()),
        },
      });
      this.dirty.set(false);
      toast.success('Herramientas del bot actualizadas.');
    } catch {
      toast.error('No se pudieron guardar los cambios.');
    } finally {
      this.saving.set(false);
    }
  }
}
