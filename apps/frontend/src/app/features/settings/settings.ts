import { Component, inject, OnInit, signal } from '@angular/core';
import { PageShellComponent, LayoutService } from '@public-intelligence/ui-core';
import { Api } from '../../api/api';
import { getAppSettings, bulkUpsertAppSettings } from '../../api/functions';
import { AppSettingResponse } from '../../api/models/app-setting-response';
import { VoicesService } from '../bots/voices.service';
import { BrandingService } from '../../core/services/branding.service';
import { InfoTooltip } from '../../shared/info-tooltip';
import { toast } from 'ngx-sonner';

interface ApiSection {
  id: string;
  serviceType: string;
  label: string;
  provider: string;
  description: string;
  infoTooltip: string;
  keyPrefix: string;
  hasKey: boolean;
  sharedWith: string | null;
  sharedWithServiceType: string | null;
  newKey: string;
}

@Component({
  selector: 'bf-settings',
  imports: [PageShellComponent, InfoTooltip],
  template: `
    <app-page-shell title="Configuración">
      <div class="space-y-6">
        <!-- Branding -->
        <div class="rounded-lg border border-neutral-200 bg-white p-6">
          <h3 class="text-base font-semibold text-neutral-900">Marca</h3>
          <p class="mt-1 text-sm text-neutral-500">
            Logo del panel de administración y favicon del navegador (también en las páginas de chat).
          </p>

          <div class="mt-6 grid gap-6 sm:grid-cols-2">
            <!-- Logo -->
            <div>
              <p class="mb-2 text-sm font-medium text-neutral-800">Logo</p>
              <div class="flex items-center gap-4 rounded-lg border border-neutral-200 bg-neutral-50 p-4">
                <img
                  [src]="branding.logoUrl()"
                  alt="Logo"
                  class="h-12 w-auto max-w-[120px] object-contain"
                />
                <div class="min-w-0 flex-1">
                  <p class="truncate text-sm text-neutral-700">
                    @if (branding.hasLogo()) {
                      <span class="font-medium">Personalizado:</span>
                      {{ branding.logoFilename() }}
                    } @else {
                      <span class="font-medium">Predeterminado</span>
                    }
                  </p>
                  <div class="mt-3 flex flex-wrap gap-2">
                    <label
                      class="cursor-pointer rounded-lg border border-neutral-300 bg-white px-3 py-1.5 text-sm font-medium text-neutral-800 transition hover:bg-neutral-100"
                      [class.opacity-50]="logoUploading()"
                    >
                      {{ logoUploading() ? 'Subiendo…' : 'Subir' }}
                      <input
                        type="file"
                        accept=".png,.webp,.jpg,.jpeg,image/png,image/webp,image/jpeg"
                        class="sr-only"
                        [disabled]="logoUploading()"
                        (change)="onLogoSelected($event)"
                      />
                    </label>
                    @if (branding.hasLogo()) {
                      <button
                        type="button"
                        (click)="onRemoveLogo()"
                        [disabled]="logoUploading()"
                        class="rounded-lg border border-neutral-300 bg-white px-3 py-1.5 text-sm font-medium text-neutral-800 transition hover:bg-neutral-100 disabled:opacity-50"
                      >
                        Restablecer
                      </button>
                    }
                  </div>
                </div>
              </div>
            </div>

            <!-- Favicon -->
            <div>
              <p class="mb-2 text-sm font-medium text-neutral-800">Favicon</p>
              <div class="flex items-center gap-4 rounded-lg border border-neutral-200 bg-neutral-50 p-4">
                <img
                  [src]="branding.faviconUrl() ?? 'favicon-32.png'"
                  alt="Favicon"
                  class="h-8 w-8 object-contain"
                />
                <div class="min-w-0 flex-1">
                  <p class="truncate text-sm text-neutral-700">
                    @if (branding.hasFavicon()) {
                      <span class="font-medium">Personalizado:</span>
                      {{ branding.faviconFilename() }}
                    } @else {
                      <span class="font-medium">Predeterminado</span>
                    }
                  </p>
                  <div class="mt-3 flex flex-wrap gap-2">
                    <label
                      class="cursor-pointer rounded-lg border border-neutral-300 bg-white px-3 py-1.5 text-sm font-medium text-neutral-800 transition hover:bg-neutral-100"
                      [class.opacity-50]="faviconUploading()"
                    >
                      {{ faviconUploading() ? 'Subiendo…' : 'Subir' }}
                      <input
                        type="file"
                        accept=".png,.webp,.jpg,.jpeg,.ico,image/png,image/webp,image/jpeg,image/x-icon"
                        class="sr-only"
                        [disabled]="faviconUploading()"
                        (change)="onFaviconSelected($event)"
                      />
                    </label>
                    @if (branding.hasFavicon()) {
                      <button
                        type="button"
                        (click)="onRemoveFavicon()"
                        [disabled]="faviconUploading()"
                        class="rounded-lg border border-neutral-300 bg-white px-3 py-1.5 text-sm font-medium text-neutral-800 transition hover:bg-neutral-100 disabled:opacity-50"
                      >
                        Restablecer
                      </button>
                    }
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- Warning Banner -->
        <div
          class="flex items-center gap-3 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3"
        >
          <!-- Lucide: triangle-alert -->
          <svg
            class="h-5 w-5 shrink-0 text-amber-500"
            xmlns="http://www.w3.org/2000/svg"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
          >
            <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3" />
            <path d="M12 9v4" />
            <path d="M12 17h.01" />
          </svg>
          <p class="text-sm text-amber-800">
            Las llaves de API afectan todos los bots de la organización
          </p>
        </div>

        <!-- API Key Sections -->
        @for (section of apiSections(); track section.id) {
          <div
            class="rounded-lg border border-neutral-200 bg-white p-6"
          >
            <div class="flex items-start justify-between">
              <div class="flex-1">
                <h3 class="text-base font-semibold text-neutral-900">
                  {{ section.label }} —
                  <span class="font-normal text-neutral-600">{{
                    section.provider
                  }}</span>
                  <app-info-tooltip [text]="section.infoTooltip" />
                </h3>
                <p class="mt-1 text-sm text-neutral-500">
                  {{ section.description }}
                </p>
              </div>
            </div>

            @if (section.sharedWith) {
              <div class="mt-4 flex items-center justify-between">
                <div class="flex items-center gap-2">
                  <span class="text-sm text-neutral-600">
                    Reusar clave de {{ section.sharedWith }}
                  </span>
                </div>
                <button
                  role="switch"
                  [attr.aria-checked]="isShared(section.id)"
                  [attr.aria-label]="'Reusar clave de ' + section.sharedWith"
                  (click)="toggleShared(section.id)"
                  class="relative h-6 w-11 shrink-0 rounded-full transition-colors"
                  [class]="
                    isShared(section.id) ? 'bg-blue-600' : 'bg-neutral-200'
                  "
                >
                  <span
                    class="absolute top-0.5 h-5 w-5 rounded-full bg-white shadow transition-transform"
                    [class]="
                      isShared(section.id) ? 'left-[22px]' : 'left-0.5'
                    "
                  ></span>
                </button>
              </div>
              @if (!isShared(section.id)) {
                <div class="mt-3 flex items-center gap-3">
                  <input
                    type="password"
                    [placeholder]="section.hasKey ? section.keyPrefix + '••••••••••••' : 'Ingresa la API Key...'"
                    (input)="onKeyInput(section.id, $event)"
                    class="flex-1 rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-600 font-mono placeholder:font-sans placeholder:text-neutral-400 focus:border-neutral-400 focus:outline-none"
                  />
                  @if (section.hasKey) {
                    <span
                      class="rounded-full bg-green-50 px-3 py-1 text-xs font-medium text-green-700"
                    >
                      Activa
                    </span>
                  }
                </div>
              }
            } @else {
              <div class="mt-4 flex items-center gap-3">
                <input
                  type="password"
                  [placeholder]="section.hasKey ? section.keyPrefix + '••••••••••••' : 'Ingresa la API Key...'"
                  (input)="onKeyInput(section.id, $event)"
                  class="flex-1 rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-600 font-mono placeholder:font-sans placeholder:text-neutral-400 focus:border-neutral-400 focus:outline-none"
                />
                @if (section.hasKey) {
                  <span
                    class="rounded-full bg-green-50 px-3 py-1 text-xs font-medium text-green-700"
                  >
                    Activa
                  </span>
                }
              </div>
            }
          </div>
        }

        <!-- Save Button -->
        <div class="flex justify-end">
          <button
            (click)="onSave()"
            [disabled]="saving()"
            class="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-blue-700 disabled:opacity-50"
          >
            {{ saving() ? 'Guardando...' : 'Guardar Configuración' }}
          </button>
        </div>
      </div>
    </app-page-shell>
  `,
})
export class Settings implements OnInit {
  private readonly layout = inject(LayoutService);
  private readonly api = inject(Api);
  private readonly voicesService = inject(VoicesService);
  readonly branding = inject(BrandingService);

  readonly saving = signal(false);
  readonly loading = signal(true);
  readonly logoUploading = signal(false);
  readonly faviconUploading = signal(false);

  readonly sharedState: Record<string, ReturnType<typeof signal<boolean>>> = {
    tts: signal(true),
    embeddings: signal(true),
  };

  private readonly shareMap: Record<string, string> = {
    tts: 'stt',
    embeddings: 'llm',
  };

  readonly apiSections = signal<ApiSection[]>([
    {
      id: 'llm',
      serviceType: 'Llm',
      label: 'LLM',
      provider: 'Gemini',
      description: 'Modelo de lenguaje principal para generación de respuestas.',
      infoTooltip:
        'Modelo de inteligencia artificial que genera las respuestas de texto del bot a partir de la pregunta del usuario y el contexto recuperado.',
      keyPrefix: '',
      hasKey: false,
      sharedWith: null,
      sharedWithServiceType: null,
      newKey: '',
    },
    {
      id: 'stt',
      serviceType: 'Stt',
      label: 'STT',
      provider: 'ElevenLabs',
      description: 'Speech-to-Text para la transcripción de voz.',
      infoTooltip:
        'Servicio que convierte la voz del usuario en texto para que el bot pueda procesarla.',
      keyPrefix: '',
      hasKey: false,
      sharedWith: null,
      sharedWithServiceType: null,
      newKey: '',
    },
    {
      id: 'tts',
      serviceType: 'Tts',
      label: 'TTS',
      provider: 'ElevenLabs',
      description: 'Text-to-Speech para hablar con voz.',
      infoTooltip:
        'Servicio que convierte las respuestas de texto del bot en voz hablada para el usuario.',
      keyPrefix: '',
      hasKey: false,
      sharedWith: 'STT',
      sharedWithServiceType: 'Stt',
      newKey: '',
    },
    {
      id: 'embeddings',
      serviceType: 'Embeddings',
      label: 'Embeddings',
      provider: 'Gemini',
      description: 'Embeddings de texto para la búsqueda semántica.',
      infoTooltip:
        'Servicio que convierte los documentos de la base de conocimiento en vectores numéricos para encontrar los más relevantes a cada pregunta.',
      keyPrefix: '',
      hasKey: false,
      sharedWith: 'LLM',
      sharedWithServiceType: 'Llm',
      newKey: '',
    },
  ]);

  isShared(id: string): boolean {
    return this.sharedState[id]?.() ?? false;
  }

  toggleShared(id: string): void {
    const s = this.sharedState[id];
    if (s) s.update((v) => !v);
  }

  onKeyInput(id: string, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.apiSections.update(sections =>
      sections.map(s => s.id === id ? { ...s, newKey: value } : s)
    );
  }

  async ngOnInit() {
    this.layout.setPageTitle('Configuración');
    await Promise.all([this.loadSettings(), this.branding.loadForAdmin()]);
  }

  async onLogoSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.logoUploading.set(true);
    try {
      await this.branding.uploadLogo(file);
      toast.success('Logo actualizado.');
    } catch (err) {
      toast.error(this.extractError(err) ?? 'No se pudo subir el logo.');
    } finally {
      this.logoUploading.set(false);
      input.value = '';
    }
  }

  async onRemoveLogo() {
    if (!this.branding.hasLogo()) return;
    this.logoUploading.set(true);
    try {
      await this.branding.removeLogo();
      toast.success('Se usará el logo predeterminado.');
    } catch (err) {
      toast.error(this.extractError(err) ?? 'No se pudo restablecer el logo.');
    } finally {
      this.logoUploading.set(false);
    }
  }

  async onFaviconSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.faviconUploading.set(true);
    try {
      await this.branding.uploadFavicon(file);
      toast.success('Favicon actualizado.');
    } catch (err) {
      toast.error(this.extractError(err) ?? 'No se pudo subir el favicon.');
    } finally {
      this.faviconUploading.set(false);
      input.value = '';
    }
  }

  async onRemoveFavicon() {
    if (!this.branding.hasFavicon()) return;
    this.faviconUploading.set(true);
    try {
      await this.branding.removeFavicon();
      toast.success('Se usará el favicon predeterminado.');
    } catch (err) {
      toast.error(this.extractError(err) ?? 'No se pudo restablecer el favicon.');
    } finally {
      this.faviconUploading.set(false);
    }
  }

  private extractError(err: unknown): string | null {
    const e = err as { error?: { error?: string }; status?: number; message?: string };
    if (e?.error?.error) return e.error.error;
    if (e?.status === 404) return 'El servidor no tiene el endpoint de marca (¿API reiniciada / migraciones aplicadas?).';
    if (e?.status === 403) return 'No tienes permiso para actualizar la configuración.';
    if (e?.status === 401) return 'Sesión expirada. Vuelve a iniciar sesión.';
    return null;
  }

  private async loadSettings() {
    try {
      const settings = await this.api.invoke(getAppSettings);
      this.applySettingsToSections(settings);
    } catch {
      toast.error('Error al cargar la configuración.');
    } finally {
      this.loading.set(false);
    }
  }

  private applySettingsToSections(settings: AppSettingResponse[]) {
    const byServiceType = new Map(settings.map(s => [s.serviceType, s]));

    this.apiSections.update(sections =>
      sections.map(section => {
        const setting = byServiceType.get(section.serviceType);
        if (setting) {
          return {
            ...section,
            keyPrefix: setting.keyPrefix,
            hasKey: setting.hasKey,
            newKey: '',
          };
        }
        return { ...section, keyPrefix: '', hasKey: false, newKey: '' };
      })
    );

    // If TTS has its own key, unshare it
    const tts = byServiceType.get('Tts');
    const stt = byServiceType.get('Stt');
    if (tts && stt && tts.keyPrefix !== stt.keyPrefix) {
      this.sharedState['tts'].set(false);
    }

    // If Embeddings has its own key, unshare it
    const emb = byServiceType.get('Embeddings');
    const llm = byServiceType.get('Llm');
    if (emb && llm && emb.keyPrefix !== llm.keyPrefix) {
      this.sharedState['embeddings'].set(false);
    }
  }

  async onSave() {
    if (this.saving()) return;
    this.saving.set(true);

    try {
      const sections = this.apiSections();
      const settingsToSave: { serviceType: string; apiKey: string }[] = [];

      for (const section of sections) {
        // Skip shared sections — they'll get the key from their source
        if (section.sharedWith && this.isShared(section.id)) {
          const sourceId = this.shareMap[section.id];
          const source = sections.find(s => s.id === sourceId);
          if (source?.newKey) {
            settingsToSave.push({ serviceType: section.serviceType, apiKey: source.newKey });
          }
          continue;
        }

        if (section.newKey) {
          settingsToSave.push({ serviceType: section.serviceType, apiKey: section.newKey });
        }
      }

      if (settingsToSave.length === 0) {
        toast.info('No hay cambios para guardar.');
        return;
      }

      await this.api.invoke(bulkUpsertAppSettings, {
        body: {
          settings: settingsToSave,
        },
      });

      // Reload all settings to get the full state (including unchanged ones)
      const allSettings = await this.api.invoke(getAppSettings);
      this.applySettingsToSections(allSettings);
      toast.success('Configuración guardada.');

      // Prime voices cache when an ElevenLabs (TTS/STT) key was just saved.
      const ttsTouched = settingsToSave.some(
        (s) => s.serviceType === 'Tts' || s.serviceType === 'Stt',
      );
      if (ttsTouched) {
        this.voicesService.refresh().catch(() => {
          // surface separately — don't block the success toast
        });
      }
    } catch {
      toast.error('Error al guardar la configuración.');
    } finally {
      this.saving.set(false);
    }
  }
}
