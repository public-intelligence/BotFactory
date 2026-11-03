import { Component, computed, effect, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormBuilder } from '@angular/forms';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';


import { BotEditStore } from './bot-edit.store';
import { InfoTooltip } from '../../shared/info-tooltip';
import { resolveProductUrl } from '../../core/product-url';
import { Api } from '../../api/api';
import { updateBot } from '../../api/functions';
import { AuthService } from '../../core/services/auth.service';
import { toast } from 'ngx-sonner';

const ACCESS_MANAGE_PERMISSIONS = new Set(['superadmin', 'bot:admin', 'bot:publish']);

@Component({
  selector: 'bf-bot-edit-general',
  imports: [ReactiveFormsModule, InfoTooltip],
  template: `
    <div class="space-y-8">
      <!-- Información Básica -->
      <section>
        <h3 class="mb-4 text-base font-semibold text-neutral-900">
          Información Básica
        </h3>
        <div class="grid grid-cols-2 gap-4">
          <div>
            <label
              class="mb-1 block text-sm font-medium text-neutral-900"
              >Nombre del bot</label
            >
            <input
              type="text"
              [formControl]="form.controls.botName"
              class="w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none"
            />
          </div>
        </div>
        <div class="mt-4">
          <label
            class="mb-1 inline-flex items-center text-sm font-medium text-neutral-900"
          >
            URL Slug
            <app-info-tooltip
              text="Identificador público del bot. Forma parte de la URL donde los usuarios acceden al chat."
            />
          </label>
          <input
            type="text"
            [formControl]="form.controls.urlSlug"
            class="w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none"
          />
          <p class="mt-1 text-xs text-neutral-400">
            URL: {{ productUrl }}/chat/{{ form.controls.urlSlug.value }}
          </p>
        </div>
      </section>

      <!-- Acceso -->
      <section>
        <h3 class="mb-4 inline-flex items-center text-base font-semibold text-neutral-900">
          Acceso
          <app-info-tooltip
            text="Define quién puede conversar con el bot publicado. La administración del bot siempre requiere iniciar sesión."
          />
        </h3>
        @if (!canManageAccess()) {
          <p class="mb-3 rounded-lg bg-neutral-50 px-4 py-3 text-sm text-neutral-500">
            Necesitas permiso de publicación para cambiar el modo de acceso.
          </p>
        }
        <div class="space-y-3">
          @for (option of accessOptions; track option.value) {
            <label
              class="flex items-start gap-3 rounded-lg border px-4 py-3 transition"
              [class.border-blue-400]="form.controls.requiresAuthentication.value === option.value"
              [class.border-neutral-200]="form.controls.requiresAuthentication.value !== option.value"
              [class.cursor-pointer]="canManageAccess()"
              [class.opacity-60]="!canManageAccess()"
              [class.hover:bg-neutral-50]="canManageAccess()"
            >
              <input
                type="radio"
                name="accessMode"
                class="mt-0.5 h-4 w-4 border-neutral-300"
                [checked]="form.controls.requiresAuthentication.value === option.value"
                [disabled]="!canManageAccess()"
                (change)="onAccessModeChange(option.value)"
              />
              <span>
                <span class="block text-sm font-medium text-neutral-900">{{ option.label }}</span>
                <span class="block text-xs text-neutral-500">{{ option.description }}</span>
              </span>
            </label>
          }
        </div>
        @if (!form.controls.requiresAuthentication.value) {
          <p class="mt-3 rounded-lg bg-amber-50 px-4 py-3 text-xs leading-relaxed text-amber-800">
            Cualquier persona con el enlace podrá conversar con este bot sin identificarse.
            Asegúrate de que la base de conocimiento no contenga información interna o sensible.
          </p>

          <div class="mt-4 max-w-xs">
            <label class="mb-1 inline-flex items-center text-sm font-medium text-neutral-900">
              Límite de solicitudes por minuto
              <app-info-tooltip
                text="Máximo de inicios/reanudaciones de sesión anónima por minuto para este bot (todas las IPs). Vacío = límite estándar de la plataforma."
              />
            </label>
            <input
              type="number"
              min="0"
              [formControl]="form.controls.publicRequestsPerMinute"
              placeholder="Estándar de la plataforma"
              class="w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none"
              [attr.disabled]="canManageAccess() ? null : true"
            />
          </div>
        }
        @if (!form.controls.requiresAuthentication.value && bot()?.publishedState !== 'published') {
          <p class="mt-3 rounded-lg bg-amber-50 px-4 py-3 text-xs leading-relaxed text-amber-800">
            El bot está en borrador: el acceso público solo funciona cuando el bot está publicado.
          </p>
        }
      </section>

      <!-- Compartir -->
      <section>
        <h3 class="mb-4 inline-flex items-center text-base font-semibold text-neutral-900">
          Compartir
          <app-info-tooltip
            text="Enlace directo del bot y código para incrustarlo en otros sitios web."
          />
        </h3>

        <!-- Enlace directo -->
        <div class="mb-4">
          <label class="mb-1 block text-sm font-medium text-neutral-900">Enlace directo</label>
          <div class="flex items-center gap-2">
            <input
              type="text"
              readonly
              [value]="directLink()"
              class="w-full rounded-lg border border-neutral-200 bg-neutral-50 px-3 py-2 font-mono text-xs text-neutral-700 focus:outline-none"
            />
            <button
              type="button"
              (click)="copyToClipboard(directLink(), 'Enlace copiado.')"
              class="shrink-0 rounded-lg border border-neutral-200 px-3 py-2 text-sm font-medium text-neutral-700 transition hover:bg-neutral-50"
            >
              Copiar
            </button>
          </div>
          <p class="mt-1 text-xs text-neutral-400">
            @if (form.controls.requiresAuthentication.value) {
              Los visitantes deberán iniciar sesión con GOB-ID para conversar.
            } @else {
              Enlace público estable: no cambia aunque cambies el slug.
            }
          </p>
        </div>

        <!-- Incrustar -->
        <label
          class="flex items-start gap-3 rounded-lg border border-neutral-200 px-4 py-3 transition"
          [class.cursor-pointer]="canManageAccess()"
          [class.opacity-60]="!canManageAccess()"
          [class.hover:bg-neutral-50]="canManageAccess()"
        >
          <input
            type="checkbox"
            class="mt-0.5 h-4 w-4 rounded border-neutral-300"
            [checked]="form.controls.embedEnabled.value"
            [disabled]="!canManageAccess()"
            (change)="onEmbedToggle()"
          />
          <span>
            <span class="block text-sm font-medium text-neutral-900">Permitir incrustar en otros sitios</span>
            <span class="block text-xs text-neutral-500">
              Genera un código &lt;iframe&gt; para insertar el bot en portales externos. Solo disponible para bots públicos.
            </span>
          </span>
        </label>

        @if (form.controls.embedEnabled.value) {
          @if (form.controls.requiresAuthentication.value) {
            <p class="mt-3 rounded-lg bg-amber-50 px-4 py-3 text-xs leading-relaxed text-amber-800">
              El bot requiere autenticación: el inicio de sesión GOB-ID no funciona dentro de un iframe.
              Cambia el acceso a «Público» para que la incrustación funcione.
            </p>
          }

          <div class="mt-4">
            <label class="mb-1 inline-flex items-center text-sm font-medium text-neutral-900">
              Dominios permitidos
              <app-info-tooltip
                text="Sitios (origines) que pueden incrustar este bot, uno por línea. Vacío = ningún sitio externo puede incrustarlo."
              />
            </label>
            <textarea
              rows="3"
              [formControl]="form.controls.allowedEmbedOrigins"
              placeholder="https://www.ministerio.gob.do&#10;https://portal.ejemplo.gob.do"
              class="w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 font-mono text-xs text-neutral-900 focus:border-neutral-400 focus:outline-none"
              [attr.disabled]="canManageAccess() ? null : true"
            ></textarea>
            @if (form.controls.embedEnabled.value && !form.controls.allowedEmbedOrigins.value.trim()) {
              <p class="mt-1 text-xs text-amber-700">
                Sin dominios configurados, ningún sitio externo podrá mostrar el bot (solo la vista previa).
              </p>
            }
          </div>

          <div class="mt-4">
            <label class="mb-1 block text-sm font-medium text-neutral-900">Código para incrustar</label>
            <div class="flex items-start gap-2">
              <textarea
                rows="6"
                readonly
                class="w-full resize-none rounded-lg border border-neutral-200 bg-neutral-50 px-3 py-2 font-mono text-xs text-neutral-700 focus:outline-none"
              >{{ embedSnippet() }}</textarea>
              <button
                type="button"
                (click)="copyToClipboard(embedSnippet(), 'Código copiado.')"
                class="shrink-0 rounded-lg border border-neutral-200 px-3 py-2 text-sm font-medium text-neutral-700 transition hover:bg-neutral-50"
              >
                Copiar
              </button>
            </div>
            <p class="mt-1 text-xs text-neutral-400">
              El atributo <span class="font-mono">allow="microphone; autoplay"</span> es necesario para la voz.
              El sitio anfitrión no debe bloquear el micrófono con su propia política de permisos.
            </p>
          </div>

          <div class="mt-4">
            <button
              type="button"
              (click)="previewOpen.set(!previewOpen())"
              class="rounded-lg border border-neutral-200 px-3 py-2 text-sm font-medium text-neutral-700 transition hover:bg-neutral-50"
            >
              {{ previewOpen() ? 'Ocultar vista previa' : 'Vista previa' }}
            </button>
            @if (previewOpen()) {
              @if (canPreviewEmbed()) {
                <div class="mt-3 overflow-hidden rounded-xl border border-neutral-200">
                  <iframe
                    [src]="embedPreviewUrl()"
                    title="Vista previa del bot incrustado"
                    width="100%"
                    height="480"
                    loading="lazy"
                    allow="microphone; autoplay"
                    style="border: 0"
                  ></iframe>
                </div>
              } @else {
                <p class="mt-3 rounded-lg bg-neutral-50 px-4 py-3 text-xs text-neutral-500">
                  La vista previa requiere que el bot esté publicado, con acceso público e incrustación
                  habilitada, y con los cambios guardados.
                </p>
              }
            }
          </div>
        }
      </section>

      <!-- Interacción -->
      <section>
        <h3 class="mb-4 inline-flex items-center text-base font-semibold text-neutral-900">
          Interacción
          <app-info-tooltip
            text="Define cómo pueden conversar los usuarios con el bot. Al menos un modo debe estar habilitado."
          />
        </h3>
        <div class="space-y-3">
          @for (toggle of interactionToggles; track toggle.key) {
            <label
              class="flex cursor-pointer items-start gap-3 rounded-lg border border-neutral-200 px-4 py-3 transition hover:bg-neutral-50"
            >
              <input
                type="checkbox"
                class="mt-0.5 h-4 w-4 rounded border-neutral-300"
                [checked]="form.controls[toggle.key].value"
                [disabled]="isInteractionToggleDisabled(toggle.key)"
                (change)="onInteractionToggle(toggle.key)"
              />
              <span>
                <span class="block text-sm font-medium text-neutral-900">{{ toggle.label }}</span>
                <span class="block text-xs text-neutral-500">{{ toggle.description }}</span>
              </span>
            </label>
          }
        </div>
      </section>

      <!-- Saludo Inicial -->
      <section>
        <h3 class="mb-4 inline-flex items-center text-base font-semibold text-neutral-900">
          Saludo Inicial
          <app-info-tooltip
            text="Mensaje que el bot dice al inicio de cada conversación, antes de que el usuario hable. Dejar vacío para que el bot espere en silencio."
          />
        </h3>
        <textarea
          rows="2"
          [formControl]="form.controls.greetingMessage"
          placeholder="¡Hola! Estoy para servirte."
          class="w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none"
        ></textarea>
        <p class="mt-1 text-xs text-neutral-400">
          {{ form.controls.greetingMessage.value.length }} caracteres.
        </p>
      </section>

      <!-- Prompt del Sistema -->
      <section>
        <h3 class="mb-4 inline-flex items-center text-base font-semibold text-neutral-900">
          Prompt del Sistema
          <app-info-tooltip
            text="Instrucciones base que definen la personalidad, tono y reglas del bot. El modelo las recibe en cada conversación."
          />
        </h3>
        <textarea
          rows="5"
          [formControl]="form.controls.masterPrompt"
          class="w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none"
        ></textarea>
        <p class="mt-1 text-xs text-neutral-400">
          {{ form.controls.masterPrompt.value.length }} caracteres. Define el comportamiento del bot.
        </p>
      </section>

      <!-- Configuración de Conocimiento -->
      <section>
        <h3 class="mb-4 text-base font-semibold text-neutral-900">
          Configuración de Conocimiento
        </h3>
        <div class="grid grid-cols-2 gap-4">
          <div>
            <label
              class="mb-1 inline-flex items-center text-sm font-medium text-neutral-900"
            >
              Documentos recuperados (Top K)
              <app-info-tooltip
                text="Cantidad de documentos de la base de conocimiento que se recuperan por cada pregunta para alimentar al modelo."
              />
            </label>
            <input
              type="number"
              [formControl]="form.controls.topK"
              class="w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none"
            />
          </div>
          <div>
            <label
              class="mb-1 inline-flex items-center text-sm font-medium text-neutral-900"
            >
              Umbral de similitud (0–1)
              <app-info-tooltip
                text="Qué tan cercano debe ser un documento a la pregunta para que el sistema lo recupere (junto al Top K). Mientras menor el número, más estricto el filtro y más relevantes deben ser los documentos."
              />
            </label>
            <input
              type="number"
              [formControl]="form.controls.similarityThreshold"
              min="0"
              max="1"
              step="0.05"
              class="w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none"
            />
            <p class="mt-1 text-xs text-neutral-400">
              Distancia coseno máxima. Menor = más estricto.
            </p>
          </div>
          <div>
            <label
              class="mb-1 inline-flex items-center text-sm font-medium text-neutral-900"
            >
              Mensaje cuando no hay respuesta
              <app-info-tooltip
                text="Mensaje que el bot responde cuando no encuentra contexto suficiente en la base de conocimiento para contestar la pregunta del usuario."
              />
            </label>
            <input
              type="text"
              [formControl]="form.controls.groundedOnlyMessage"
              class="w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none"
            />
          </div>
        </div>
      </section>

      <!-- Footer Actions -->
      <div
        class="flex items-center justify-end gap-3 border-t border-neutral-100 pt-6"
      >
        <button
          type="button"
          (click)="onDiscard()"
          class="rounded-lg px-4 py-2 text-sm font-medium text-neutral-600 transition hover:text-neutral-900"
        >
          Descartar Cambios
        </button>
        <button
          type="button"
          (click)="onSave()"
          [disabled]="saving()"
          class="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-blue-700 disabled:opacity-50"
        >
          {{ saving() ? 'Guardando...' : 'Guardar' }}
        </button>
      </div>
    </div>
  `,
})
export class BotEditGeneral {
  private readonly store = inject(BotEditStore);
  private readonly api = inject(Api);
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly sanitizer = inject(DomSanitizer);

  readonly saving = signal(false);
  readonly previewOpen = signal(false);
  readonly productUrl = resolveProductUrl();
  readonly bot = this.store.bot;

  // Iframe src must be a stable SafeResourceUrl: a fresh object per change
  // detection cycle would re-bind [src] and reload the preview in a loop.
  private previewUrlCache: { key: string; url: SafeResourceUrl } | null = null;

  readonly canManageAccess = computed(() => {
    const perms = this.auth.user()?.permissions ?? [];
    return perms.some((p) => ACCESS_MANAGE_PERMISSIONS.has(p));
  });

  readonly accessOptions = [
    {
      value: true,
      label: 'Autenticado (GOB-ID)',
      description: 'Los usuarios deben iniciar sesión con GOB-ID antes de conversar.',
    },
    {
      value: false,
      label: 'Público',
      description: 'Cualquier persona con el enlace puede conversar sin iniciar sesión.',
    },
  ];

  readonly form = this.fb.nonNullable.group({
    botName: [''],
    urlSlug: [''],
    masterPrompt: [''],
    greetingMessage: [''],
    topK: [5],
    similarityThreshold: [0.4],
    groundedOnlyMessage: [''],
    enableChat: [false],
    enableVoice: [true],
    requiresAuthentication: [true],
    embedEnabled: [false],
    allowedEmbedOrigins: [''],
    publicRequestsPerMinute: [null as number | null],
  });

  readonly interactionToggles = [
    {
      key: 'enableChat' as const,
      label: 'Habilitar chat',
      description: 'Los usuarios pueden escribir mensajes de texto.',
    },
    {
      key: 'enableVoice' as const,
      label: 'Habilitar voz',
      description: 'Los usuarios pueden hablar por micrófono.',
    },
  ];

  constructor() {
    effect(() => {
      const bot = this.store.bot();
      if (bot) {
        this.form.patchValue({
          botName: bot.botName,
          urlSlug: bot.urlSlug,
          masterPrompt: bot.masterPrompt,
          greetingMessage: bot.greetingMessage ?? '',
          topK: bot.topK,
          similarityThreshold: bot.similarityThreshold,
          groundedOnlyMessage: bot.groundedOnlyMessage ?? '',
          enableChat: bot.enableChat,
          enableVoice: bot.enableVoice,
          requiresAuthentication: bot.requiresAuthentication,
          embedEnabled: bot.embedEnabled,
          allowedEmbedOrigins: this.originsForDisplay(bot.allowedEmbedOrigins),
          publicRequestsPerMinute: bot.publicRequestsPerMinute ?? null,
        }, { emitEvent: false });
      }
    });
  }

  /** Stored comma-separated; edited one-per-line. */
  private originsForDisplay(stored: string): string {
    return stored ? stored.split(',').join('\n') : '';
  }

  onAccessModeChange(requiresAuthentication: boolean) {
    if (!this.canManageAccess()) return;
    this.form.controls.requiresAuthentication.setValue(requiresAuthentication);
  }

  onEmbedToggle() {
    if (!this.canManageAccess()) return;
    this.form.controls.embedEnabled.setValue(!this.form.controls.embedEnabled.value);
  }

  directLink(): string {
    const bot = this.store.bot();
    if (!bot) return '';
    // Public bots share the stable opaque id; authenticated bots keep the
    // human slug (their URL is only reachable after login anyway).
    const key = this.form.controls.requiresAuthentication.value
      ? (this.form.controls.urlSlug.value || bot.urlSlug)
      : bot.publicId;
    return `${this.productUrl}/chat/${key}`;
  }

  embedSnippet(): string {
    const bot = this.store.bot();
    if (!bot) return '';
    const title = (this.form.controls.botName.value || bot.botName || 'Asistente virtual')
      .replace(/"/g, '&quot;');
    return [
      '<iframe',
      `  src="${this.productUrl}/api/embed/${bot.publicId}"`,
      `  title="${title}"`,
      '  width="100%"',
      '  height="600"',
      '  style="border: 0; border-radius: 12px"',
      '  loading="lazy"',
      '  allow="microphone; autoplay"',
      '></iframe>',
    ].join('\n');
  }

  /** Preview reflects SAVED state — the embed route validates against the DB. */
  canPreviewEmbed(): boolean {
    const bot = this.store.bot();
    return !!bot
      && bot.publishedState === 'published'
      && !bot.requiresAuthentication
      && bot.embedEnabled;
  }

  embedPreviewUrl(): SafeResourceUrl {
    const bot = this.store.bot();
    const key = bot ? `${this.productUrl}/api/embed/${bot.publicId}` : '';
    if (this.previewUrlCache?.key !== key) {
      this.previewUrlCache = {
        key,
        url: this.sanitizer.bypassSecurityTrustResourceUrl(key),
      };
    }
    return this.previewUrlCache.url;
  }

  async copyToClipboard(text: string, successMessage: string) {
    try {
      await navigator.clipboard.writeText(text);
      toast.success(successMessage);
    } catch {
      toast.error('No se pudo copiar al portapapeles.');
    }
  }

  isInteractionToggleDisabled(key: 'enableChat' | 'enableVoice'): boolean {
    const values = this.form.getRawValue();
    if (key === 'enableChat') return values.enableChat && !values.enableVoice;
    return values.enableVoice && !values.enableChat;
  }

  onInteractionToggle(key: 'enableChat' | 'enableVoice') {
    if (this.isInteractionToggleDisabled(key)) return;
    this.form.controls[key].setValue(!this.form.controls[key].value);
  }

  onDiscard() {
    const bot = this.store.bot();
    if (bot) {
        this.form.patchValue({
          botName: bot.botName,
          urlSlug: bot.urlSlug,
          masterPrompt: bot.masterPrompt,
          greetingMessage: bot.greetingMessage ?? '',
          topK: bot.topK,
          similarityThreshold: bot.similarityThreshold,
          groundedOnlyMessage: bot.groundedOnlyMessage ?? '',
          enableChat: bot.enableChat,
          enableVoice: bot.enableVoice,
          requiresAuthentication: bot.requiresAuthentication,
          embedEnabled: bot.embedEnabled,
          allowedEmbedOrigins: this.originsForDisplay(bot.allowedEmbedOrigins),
          publicRequestsPerMinute: bot.publicRequestsPerMinute ?? null,
        }, { emitEvent: false });
    }
  }

  async onSave() {
    const bot = this.store.bot();
    if (!bot || this.saving()) return;

    this.saving.set(true);

    try {
      const values = this.form.getRawValue();
      const updated = await this.api.invoke(updateBot, {
        id: bot.id,
        body: {
          botName: values.botName,
          urlSlug: values.urlSlug,
          masterPrompt: values.masterPrompt,
          greetingMessage: values.greetingMessage.trim() || null,
          topK: values.topK,
          similarityThreshold: values.similarityThreshold,
          groundedOnlyMessage: values.groundedOnlyMessage || null,
          enableChat: values.enableChat,
          enableVoice: values.enableVoice,
          requiresAuthentication: values.requiresAuthentication,
          embedEnabled: values.embedEnabled,
          allowedEmbedOrigins: values.allowedEmbedOrigins,
          // 0 clears the per-bot override (backend maps <=0 to "platform default").
          publicRequestsPerMinute: values.publicRequestsPerMinute ?? 0,
        },
      });
      this.store.bot.set(updated);
      toast.success('Cambios guardados.');
    } catch {
      toast.error('Error al guardar los cambios.');
    } finally {
      this.saving.set(false);
    }
  }
}
