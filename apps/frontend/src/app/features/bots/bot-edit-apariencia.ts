import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { map, startWith } from 'rxjs/operators';
import { BotEditStore } from './bot-edit.store';
import { InfoTooltip } from '../../shared/info-tooltip';
import { Api } from '../../api/api';
import {
  deleteBotLogo,
  getBot,
  updateBot,
  uploadBotLogo,
} from '../../api/functions';
import {
  DEFAULT_THEME_ACCENT,
  DEFAULT_THEME_BACKGROUND,
  DEFAULT_THEME_PRIMARY,
  themeStyleFromBot,
} from '../chat/bot-theme';
import { toast } from 'ngx-sonner';

/** Empty (clear to default) or #RGB / #RRGGBB. */
const HEX_OR_EMPTY = /^$|^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$/;
const HEX_ONLY = /^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$/;

type ThemeColorKey = 'themeBackground' | 'themePrimary' | 'themeAccent';

@Component({
  selector: 'bf-bot-edit-apariencia',
  imports: [ReactiveFormsModule, InfoTooltip],
  styles: [
    `
      .bf-theme-preview {
        background: var(--bf-bg);
        color: var(--bf-fg);
      }
      .bf-theme-preview-cta {
        background: var(--bf-accent);
        color: var(--bf-cta-fg);
      }
      .bf-theme-preview-bot {
        background: var(--bf-bot-bubble);
        color: var(--bf-fg);
      }
      .bf-theme-preview-user {
        background: var(--bf-accent);
        color: var(--bf-cta-fg);
      }
    `,
  ],
  template: `
    <div class="space-y-8">
      <section>
        <h3 class="mb-1 text-base font-semibold text-neutral-900">Logo</h3>
        <p class="mb-4 text-sm text-neutral-500">
          Se muestra en la cabecera del chat y en la pantalla de bienvenida.
          PNG, JPEG, WebP o SVG, máximo 2&nbsp;MB.
        </p>

        <div class="flex flex-wrap items-center gap-4">
          <div
            class="flex h-20 w-20 items-center justify-center overflow-hidden rounded-2xl border border-neutral-200 bg-neutral-50"
          >
            @if (logoPreviewUrl()) {
              <img
                [src]="logoPreviewUrl()!"
                alt="Logo del bot"
                class="h-full w-full object-contain p-2"
              />
            } @else {
              <span class="text-xs text-neutral-400">Sin logo</span>
            }
          </div>

          <div class="space-y-2">
            @if (bot()?.hasCustomLogo) {
              <p class="text-sm text-neutral-700">
                <span class="font-medium">Actual:</span>
                {{ bot()?.logoFilename }}
              </p>
            }
            <div class="flex flex-wrap gap-2">
              <label
                class="cursor-pointer rounded-lg border border-neutral-200 bg-white px-3 py-1.5 text-sm font-medium text-neutral-700 transition hover:bg-neutral-50"
                [class.opacity-50]="logoUploading()"
              >
                {{ logoUploading() ? 'Subiendo…' : 'Subir logo' }}
                <input
                  type="file"
                  accept=".png,.jpg,.jpeg,.webp,.svg,image/png,image/jpeg,image/webp,image/svg+xml"
                  class="sr-only"
                  [disabled]="logoUploading()"
                  (change)="onLogoSelected($event)"
                />
              </label>
              @if (bot()?.hasCustomLogo) {
                <button
                  type="button"
                  class="rounded-lg border border-neutral-200 px-3 py-1.5 text-sm font-medium text-neutral-700 transition hover:bg-neutral-50 disabled:opacity-50"
                  [disabled]="logoUploading()"
                  (click)="onRemoveLogo()"
                >
                  Quitar logo
                </button>
              }
            </div>
          </div>
        </div>
      </section>

      <section>
        <h3 class="mb-1 inline-flex items-center text-base font-semibold text-neutral-900">
          Colores
          <app-info-tooltip
            text="Personalizan el fondo, textos e interacciones del chat público. Déjalos vacíos para usar el tema oscuro predeterminado."
          />
        </h3>
        <p class="mb-4 text-sm text-neutral-500">
          Usa colores de tu sitio para que el asistente se sienta parte de tu marca.
        </p>

        <div class="grid gap-4 sm:grid-cols-3">
          @for (field of colorFields; track field.key) {
            <div>
              <label class="mb-1 block text-sm font-medium text-neutral-900">
                {{ field.label }}
              </label>
              <div class="flex items-center gap-2">
                <input
                  type="color"
                  class="h-10 w-12 cursor-pointer rounded border border-neutral-200 bg-white p-1"
                  [value]="colorPickerValue(field.key)"
                  (input)="onColorPicker(field.key, $event)"
                />
                <input
                  type="text"
                  [formControl]="form.controls[field.key]"
                  [placeholder]="field.placeholder"
                  class="w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 font-mono text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none"
                  [class.border-red-400]="form.controls[field.key].invalid && form.controls[field.key].touched"
                />
              </div>
              <p class="mt-1 text-xs text-neutral-400">{{ field.hint }}</p>
            </div>
          }
        </div>

        <button
          type="button"
          class="mt-4 text-sm font-medium text-neutral-600 underline-offset-2 hover:text-neutral-900 hover:underline"
          (click)="onResetDefaults()"
        >
          Restablecer valores por defecto
        </button>
      </section>

      <section>
        <h3 class="mb-4 text-base font-semibold text-neutral-900">Vista previa</h3>
        <div
          class="bf-theme-preview overflow-hidden rounded-xl border border-neutral-200 shadow-sm"
          [style]="previewStyle()"
        >
          <div class="flex items-center gap-2 px-4 pt-4">
            @if (logoPreviewUrl()) {
              <img
                [src]="logoPreviewUrl()!"
                alt=""
                class="h-8 w-8 rounded-lg object-contain"
              />
            }
            <span class="text-sm font-semibold" [style.color]="'var(--bf-primary)'">
              {{ bot()?.botName ?? 'Bot' }}
            </span>
          </div>

          <div class="flex flex-col items-center gap-3 px-6 py-8 text-center">
            <div
              class="flex h-24 w-24 items-center justify-center overflow-hidden rounded-full"
              [style.background]="'color-mix(in srgb, var(--bf-accent) 18%, transparent)'"
            >
              <div
                class="h-14 w-14 rounded-full"
                [style.background]="'color-mix(in srgb, var(--bf-accent) 85%, transparent)'"
              ></div>
            </div>
            <p class="text-base font-semibold" [style.color]="'var(--bf-primary)'">
              {{ bot()?.botName ?? 'Bot' }}
            </p>
            <button
              type="button"
              class="bf-theme-preview-cta rounded-full px-5 py-2 text-sm font-semibold"
              tabindex="-1"
            >
              Conectar y Hablar
            </button>
          </div>

          <div
            class="space-y-2 border-t px-4 py-4"
            [style.borderColor]="'color-mix(in srgb, var(--bf-fg) 12%, transparent)'"
          >
            <div class="bf-theme-preview-bot max-w-[85%] rounded-lg px-3 py-2 text-left text-xs">
              ¡Hola! ¿En qué puedo ayudarte?
            </div>
            <div class="bf-theme-preview-user ml-auto max-w-[85%] rounded-lg px-3 py-2 text-right text-xs">
              Quiero información sobre un trámite
            </div>
          </div>
        </div>
      </section>

      <div class="flex justify-end border-t border-neutral-200 pt-4">
        <button
          type="button"
          (click)="onSave()"
          [disabled]="saving() || form.invalid"
          class="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-blue-700 disabled:opacity-50"
        >
          {{ saving() ? 'Guardando...' : 'Guardar' }}
        </button>
      </div>
    </div>
  `,
})
export class BotEditApariencia {
  private readonly store = inject(BotEditStore);
  private readonly api = inject(Api);
  private readonly fb = inject(FormBuilder);

  readonly bot = this.store.bot;
  readonly saving = signal(false);
  readonly logoUploading = signal(false);
  readonly logoPreviewUrl = signal<string | null>(null);

  readonly colorFields: {
    key: ThemeColorKey;
    label: string;
    hint: string;
    placeholder: string;
  }[] = [
    {
      key: 'themeBackground',
      label: 'Fondo',
      hint: 'Fondo de la página de chat',
      placeholder: DEFAULT_THEME_BACKGROUND,
    },
    {
      key: 'themePrimary',
      label: 'Principal',
      hint: 'Títulos y acentos de marca',
      placeholder: DEFAULT_THEME_PRIMARY,
    },
    {
      key: 'themeAccent',
      label: 'Acento / CTA',
      hint: 'Botones y mensajes del usuario',
      placeholder: DEFAULT_THEME_ACCENT,
    },
  ];

  readonly form = this.fb.nonNullable.group({
    themeBackground: ['', [Validators.pattern(HEX_OR_EMPTY)]],
    themePrimary: ['', [Validators.pattern(HEX_OR_EMPTY)]],
    themeAccent: ['', [Validators.pattern(HEX_OR_EMPTY)]],
  });

  private readonly formValues = toSignal(
    this.form.valueChanges.pipe(
      startWith(this.form.getRawValue()),
      map(() => this.form.getRawValue()),
    ),
    { initialValue: this.form.getRawValue() },
  );

  readonly previewStyle = computed(() => {
    const v = this.formValues();
    return themeStyleFromBot({
      themeBackground: v.themeBackground || null,
      themePrimary: v.themePrimary || null,
      themeAccent: v.themeAccent || null,
    });
  });

  constructor() {
    effect(() => {
      const bot = this.store.bot();
      if (!bot) return;
      this.form.patchValue(
        {
          themeBackground: bot.themeBackground ?? '',
          themePrimary: bot.themePrimary ?? '',
          themeAccent: bot.themeAccent ?? '',
        },
        { emitEvent: true },
      );
      this.refreshLogoPreview(bot.id, bot.hasCustomLogo, bot.logoVersion);
    });
  }

  colorPickerValue(key: ThemeColorKey): string {
    const raw = this.form.controls[key].value?.trim();
    if (raw && HEX_ONLY.test(raw)) {
      return raw.length === 4
        ? ('#' + raw[1] + raw[1] + raw[2] + raw[2] + raw[3] + raw[3]).toUpperCase()
        : raw.toUpperCase();
    }
    if (key === 'themeBackground') return DEFAULT_THEME_BACKGROUND;
    if (key === 'themePrimary') return DEFAULT_THEME_PRIMARY;
    return DEFAULT_THEME_ACCENT;
  }

  onColorPicker(key: ThemeColorKey, event: Event) {
    const value = (event.target as HTMLInputElement).value.toUpperCase();
    this.form.controls[key].setValue(value);
    this.form.controls[key].markAsDirty();
  }

  onResetDefaults() {
    this.form.setValue({
      themeBackground: '',
      themePrimary: '',
      themeAccent: '',
    });
    this.form.markAsDirty();
  }

  async onLogoSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    const bot = this.store.bot();
    if (!file || !bot) return;

    this.logoUploading.set(true);
    try {
      const updated = await this.api.invoke(uploadBotLogo, {
        botId: bot.id,
        body: { file },
      });
      this.store.bot.set(updated);
      toast.success('Logo guardado.');
    } catch {
      toast.error('No se pudo subir el logo.');
    } finally {
      this.logoUploading.set(false);
      input.value = '';
    }
  }

  async onRemoveLogo() {
    const bot = this.store.bot();
    if (!bot?.hasCustomLogo) return;

    this.logoUploading.set(true);
    try {
      await this.api.invoke(deleteBotLogo, { botId: bot.id });
      const updated = await this.api.invoke(getBot, { id: bot.id });
      this.store.bot.set(updated);
      this.logoPreviewUrl.set(null);
      toast.success('Logo eliminado.');
    } catch {
      toast.error('No se pudo eliminar el logo.');
    } finally {
      this.logoUploading.set(false);
    }
  }

  async onSave() {
    const bot = this.store.bot();
    if (!bot || this.saving() || this.form.invalid) return;

    this.saving.set(true);
    try {
      const values = this.form.getRawValue();
      // Empty string clears to platform default (backend maps "" → null).
      const updated = await this.api.invoke(updateBot, {
        id: bot.id,
        body: {
          themeBackground: values.themeBackground.trim(),
          themePrimary: values.themePrimary.trim(),
          themeAccent: values.themeAccent.trim(),
        },
      });
      this.store.bot.set(updated);
      toast.success('Apariencia guardada.');
    } catch {
      toast.error('Error al guardar la apariencia.');
    } finally {
      this.saving.set(false);
    }
  }

  private refreshLogoPreview(botId: number, hasLogo: boolean, version: string | null | undefined) {
    if (!hasLogo) {
      this.logoPreviewUrl.set(null);
      return;
    }
    // The version, not the filename: re-uploading logo.png leaves the filename
    // identical, so the old preview would stick.
    const v = encodeURIComponent(version || String(Date.now()));
    this.logoPreviewUrl.set(`${this.api.rootUrl}/api/bots/${botId}/logo/download?v=${v}`);
  }
}
