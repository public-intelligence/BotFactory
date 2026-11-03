import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule, FormBuilder } from '@angular/forms';
import { DropdownSearchComponent } from '@public-intelligence/ui-core';
import { BotEditStore } from './bot-edit.store';
import { InfoTooltip } from '../../shared/info-tooltip';
import { VoicesService } from './voices.service';
import { Api } from '../../api/api';
import {
  deleteBotThinkingSound,
  getBot,
  updateBot,
  uploadBotThinkingSound,
} from '../../api/functions';
import { DEFAULT_THINKING_SOUND_URL } from '../../core/services/thinking-sound.service';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'bf-bot-edit-voz',
  imports: [ReactiveFormsModule, DropdownSearchComponent, InfoTooltip],
  styles: [`
    :host ::ng-deep app-dropdown-search > div { display: block !important; width: 100%; }
    :host ::ng-deep app-dropdown-search button[type="button"] {
      width: 100%;
      min-width: 0;
      padding: 0.625rem 2.25rem 0.625rem 0.875rem !important;
      font-size: 0.9375rem !important;
    }
  `],
  template: `
    <div class="space-y-8">
      @if (!voiceEnabled()) {
        <p class="rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          La voz está deshabilitada para este bot. Activa <strong>Habilitar voz</strong> en la pestaña General para configurar estas opciones.
        </p>
      }
      <div [class.pointer-events-none]="!voiceEnabled()" [class.opacity-50]="!voiceEnabled()">
      <!-- Voz activa -->
      <section>
        <h3 class="mb-4 text-base font-semibold text-neutral-900">
          Configuración de Voz
        </h3>
        <div class="grid grid-cols-2 gap-4">
          <div>
            <div class="mb-1 flex items-center gap-1.5">
              <label class="inline-flex items-center text-sm font-medium text-neutral-900">
                Voz activa
                <app-info-tooltip
                  text="Voz que el bot usará al hablar. Las voces se gestionan en ElevenLabs y se cargan a partir de la llave de TTS configurada en Ajustes."
                />
              </label>
              @if (voicesService.configured()) {
                <button
                  type="button"
                  (click)="onRefreshVoices()"
                  [disabled]="voicesService.loading()"
                  [attr.aria-label]="'Actualizar lista de voces'"
                  title="Actualizar lista de voces"
                  class="rounded p-1 text-neutral-500 transition hover:bg-neutral-100 hover:text-neutral-900 disabled:opacity-50"
                >
                  <!-- Lucide: refresh-cw -->
                  <svg
                    xmlns="http://www.w3.org/2000/svg"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    stroke-width="2"
                    stroke-linecap="round"
                    stroke-linejoin="round"
                    class="h-3.5 w-3.5"
                    [class.animate-spin]="voicesService.loading()"
                  >
                    <path d="M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8" />
                    <path d="M21 3v5h-5" />
                    <path d="M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16" />
                    <path d="M8 16H3v5" />
                  </svg>
                </button>
              }
            </div>
            @if (voicesService.configured()) {
              @if (voiceOptions().length > 0) {
                @for (trackId of [selectedVoiceId()]; track trackId) {
                  <app-dropdown-search
                    class="block w-full"
                    [options]="voiceOptions()"
                    [value]="selectedVoiceId()"
                    (valueChange)="onVoiceChange($event)"
                  ></app-dropdown-search>
                }
              } @else {
                <div class="rounded-lg border border-neutral-200 bg-neutral-50 px-3 py-2 text-sm text-neutral-500">
                  {{ voicesService.loading() ? 'Cargando voces...' : 'Sin voces disponibles.' }}
                </div>
              }
              @if (selectedVoice()?.description) {
                <p class="mt-2 rounded-md bg-neutral-50 px-3 py-2 text-xs text-neutral-600">
                  {{ selectedVoice()!.description }}
                </p>
              } @else {
                <p class="mt-1 text-xs text-neutral-400">
                  Selecciona la voz de IA que utilizará el bot para responder.
                </p>
              }
            } @else {
              <div class="flex items-center justify-between gap-3 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-800">
                <span>Configura una llave de ElevenLabs en Ajustes para seleccionar una voz.</span>
                <button
                  type="button"
                  (click)="onRefreshVoices()"
                  [disabled]="voicesService.loading()"
                  class="shrink-0 font-medium text-amber-900 underline hover:no-underline disabled:opacity-50"
                >
                  {{ voicesService.loading() ? 'Comprobando...' : 'Reintentar' }}
                </button>
              </div>
            }
          </div>
        </div>
      </section>

      <!-- Tiempos y Silencios -->
      <section>
        <h3 class="mb-4 text-base font-semibold text-neutral-900">
          Tiempos y Silencios
        </h3>
        <div class="grid grid-cols-2 gap-4">
          <div>
            <label
              class="mb-1 inline-flex items-center text-sm font-medium text-neutral-900"
            >
              Fin de silencio (ms)
              <app-info-tooltip
                text="Silencio mínimo (VAD) antes de evaluar fin de frase. El agente también usa un detector semántico de turno (no solo silencio), así que pausas breves en medio de una idea no deberían cortar al usuario."
              />
            </label>
            <input
              type="number"
              [formControl]="form.controls.silenceEndMs"
              class="w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none"
            />
          </div>
          <div>
            <label
              class="mb-1 inline-flex items-center text-sm font-medium text-neutral-900"
            >
              Timeout sin habla (ms)
              <app-info-tooltip
                text="Tiempo máximo esperando que el usuario empiece a hablar antes de cancelar la escucha. También define cuándo el sistema cierra conversaciones abandonadas en la base de datos (el doble de este valor, mediante un trabajo en segundo plano)."
              />
            </label>
            <input
              type="number"
              [formControl]="form.controls.noSpeechTimeoutMs"
              class="w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none"
            />
          </div>
        </div>
        <p class="mt-1 text-xs text-neutral-400">
          Tiempo de silencio antes de finalizar que el usuario dejó de
          hablar.
        </p>
        <div class="mt-4 grid grid-cols-2 gap-4">
          <div>
            <label
              class="mb-1 inline-flex items-center text-sm font-medium text-neutral-900"
            >
              Duración máxima de abstinencia (ms)
              <app-info-tooltip
                text="Duración máxima de una sola intervención del usuario. Después de este tiempo se corta y se procesa lo capturado."
              />
            </label>
            <input
              type="number"
              [formControl]="form.controls.maxUtteranceMs"
              class="w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none"
            />
          </div>
        </div>
      </section>

      <!-- Detección de Voz (VAD) -->
      <section>
        <h3 class="mb-4 text-base font-semibold text-neutral-900">
          Detección de Voz (VAD)
        </h3>
        <div class="grid grid-cols-2 gap-4">
          <div>
            <label
              class="mb-1 inline-flex items-center text-sm font-medium text-neutral-900"
            >
              Sensibilidad (0–1)
              <app-info-tooltip
                text="Qué tan fácilmente se considera audio como voz. Más alto = menos sensible al ruido de fondo (recomendado 0.5–0.7 si el bot se interrumpe con ruidos)."
              />
            </label>
            <input
              type="number"
              min="0"
              max="1"
              step="0.05"
              [formControl]="form.controls.vadActivationThreshold"
              class="w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none"
            />
            <p class="mt-1 text-xs text-neutral-400">
              Umbral de activación. Más alto = ignora más ruido.
            </p>
          </div>
          <div>
            <label
              class="mb-1 inline-flex items-center text-sm font-medium text-neutral-900"
            >
              Mínimo de habla (ms)
              <app-info-tooltip
                text="Cuántos milisegundos de audio deben parecer voz antes de tratarlo como una intervención real. Sube este valor (150–300) para ignorar ruidos cortos como aplausos o tos."
              />
            </label>
            <input
              type="number"
              min="0"
              [formControl]="form.controls.vadMinSpeechDurationMs"
              class="w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none"
            />
            <p class="mt-1 text-xs text-neutral-400">
              Filtra ruidos breves (claps, toses, golpes).
            </p>
          </div>
          <div>
            <label
              class="mb-1 inline-flex items-center text-sm font-medium text-neutral-900"
            >
              Duración mínima para interrumpir (ms)
              <app-info-tooltip
                text="Cuántos milisegundos seguidos de habla del usuario debe escuchar el bot antes de detenerse. Sube este valor (1000–1500) si el bot se interrumpe con cualquier sonido o palabra suelta."
              />
            </label>
            <input
              type="number"
              min="0"
              [formControl]="form.controls.interruptionMinDurationMs"
              class="w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none"
            />
            <p class="mt-1 text-xs text-neutral-400">
              Habla continua antes de cortar al bot. Más alto = más difícil interrumpir.
            </p>
          </div>
          <div>
            <label
              class="mb-1 inline-flex items-center text-sm font-medium text-neutral-900"
            >
              Palabras mínimas para interrumpir
              <app-info-tooltip
                text="Cuántas palabras transcritas debe decir el usuario antes de que el bot deje de hablar. Con 0 cualquier sonido lo interrumpe; con 1–2 se necesita decir algo articulado."
              />
            </label>
            <input
              type="number"
              min="0"
              [formControl]="form.controls.interruptionMinWords"
              class="w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none"
            />
            <p class="mt-1 text-xs text-neutral-400">
              Palabras requeridas para interrumpir. 0 = cualquier sonido.
            </p>
          </div>
        </div>
      </section>

      <!-- Opciones de Comportamiento -->
      <section>
        <h3 class="mb-4 text-base font-semibold text-neutral-900">
          Opciones de Comportamiento
        </h3>
        <div class="space-y-5">
          @for (toggle of toggles; track toggle.key) {
            <div class="flex items-center justify-between">
              <div>
                <p class="inline-flex items-center text-sm font-medium text-neutral-900">
                  {{ toggle.label }}
                  <app-info-tooltip [text]="toggle.tooltip" />
                </p>
                <p class="text-xs text-neutral-500">
                  {{ toggle.description }}
                </p>
              </div>
              <button
                role="switch"
                [attr.aria-checked]="form.controls[toggle.key].value"
                [attr.aria-label]="toggle.label"
                (click)="onToggle(toggle.key)"
                class="relative h-6 w-11 shrink-0 rounded-full transition-colors"
                [class]="
                  form.controls[toggle.key].value ? 'bg-blue-600' : 'bg-neutral-200'
                "
              >
                <span
                  class="absolute top-0.5 h-5 w-5 rounded-full bg-white shadow transition-transform"
                  [class]="
                    form.controls[toggle.key].value ? 'left-[22px]' : 'left-0.5'
                  "
                ></span>
              </button>
            </div>
          }
        </div>
      </section>

      <!-- Sonido mientras piensa -->
      <section>
        <h3 class="mb-4 text-base font-semibold text-neutral-900">
          Sonido mientras piensa
        </h3>
        <p class="mb-4 text-sm text-neutral-500">
          Audio de fondo en la sesión de chat cuando el bot está procesando su respuesta.
          Activa o desactiva el sonido con «Sonido de escritura» arriba.
        </p>
        @if (store.bot(); as bot) {
          <div class="rounded-lg border border-neutral-200 bg-neutral-50 p-4">
            <p class="text-sm text-neutral-800">
              @if (bot.hasCustomThinkingSound) {
                <span class="font-medium">Personalizado:</span>
                {{ bot.thinkingSoundFilename }}
              } @else {
                <span class="font-medium">Predeterminado:</span>
                sonido de tecleo
              }
            </p>
            <div class="mt-4 flex flex-wrap items-center gap-3">
              <label
                class="cursor-pointer rounded-lg border border-neutral-300 bg-white px-4 py-2 text-sm font-medium text-neutral-800 transition hover:bg-neutral-100"
                [class.opacity-50]="thinkingSoundUploading()"
              >
                {{ thinkingSoundUploading() ? 'Subiendo…' : 'Subir .mp3' }}
                <input
                  type="file"
                  accept=".mp3,audio/mpeg"
                  class="sr-only"
                  [disabled]="thinkingSoundUploading()"
                  (change)="onThinkingSoundSelected($event)"
                />
              </label>
              <button
                type="button"
                class="rounded-lg border border-neutral-300 bg-white px-4 py-2 text-sm font-medium text-neutral-800 transition hover:bg-neutral-100"
                (click)="onPreviewThinkingSound()"
              >
                Escuchar
              </button>
              @if (bot.hasCustomThinkingSound) {
                <button
                  type="button"
                  class="rounded-lg border border-red-200 bg-white px-4 py-2 text-sm font-medium text-red-700 transition hover:bg-red-50"
                  [disabled]="thinkingSoundUploading()"
                  (click)="onRemoveThinkingSound()"
                >
                  Usar predeterminado
                </button>
              }
            </div>
          </div>
        }
      </section>

      </div>

      <!-- Footer Actions -->
      <div
        class="flex items-center justify-end gap-3 border-t border-neutral-100 pt-6"
      >
        <button
          type="button"
          (click)="onDiscard()"
          [disabled]="!voiceEnabled()"
          class="rounded-lg px-4 py-2 text-sm font-medium text-neutral-600 transition hover:text-neutral-900 disabled:opacity-50"
        >
          Descartar Cambios
        </button>
        <button
          type="button"
          (click)="onSave()"
          [disabled]="saving() || !voiceEnabled()"
          class="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-blue-700 disabled:opacity-50"
        >
          {{ saving() ? 'Guardando...' : 'Guardar' }}
        </button>
      </div>
    </div>
  `,
})
export class BotEditVoz {
  protected readonly store = inject(BotEditStore);
  private readonly api = inject(Api);
  private readonly fb = inject(FormBuilder);
  protected readonly voicesService = inject(VoicesService);

  readonly saving = signal(false);
  readonly thinkingSoundUploading = signal(false);

  readonly voiceEnabled = computed(() => this.store.bot()?.enableVoice ?? true);

  readonly voiceOptions = computed(() =>
    this.voicesService.voices().map((v) => ({ value: v.voiceId, label: v.name })),
  );

  readonly form = this.fb.nonNullable.group({
    voiceId: this.fb.nonNullable.control<string | null>(null),
    silenceEndMs: [700],
    noSpeechTimeoutMs: [15000],
    maxUtteranceMs: [20000],
    enableBargeIn: [true],
    enableTts: [true],
    enableTypingSound: [true],
    vadActivationThreshold: [0.6],
    vadMinSpeechDurationMs: [200],
    interruptionMinDurationMs: [1000],
    interruptionMinWords: [1],
  });

  readonly selectedVoiceId = toSignal(this.form.controls.voiceId.valueChanges, {
    initialValue: this.form.controls.voiceId.value,
  });

  readonly selectedVoice = computed(() =>
    this.voicesService.findById(this.selectedVoiceId()),
  );

  readonly toggles = [
    {
      key: 'enableBargeIn' as const,
      label: 'Habilitar Barge-in',
      description:
        'Permite al usuario interrumpir al bot mientras está hablando.',
      tooltip:
        'Si está activo, el bot deja de hablar en cuanto detecta que el usuario empezó a hablar, sin esperar a terminar la frase.',
    },
    {
      key: 'enableTts' as const,
      label: 'Habilitar TTS',
      description: 'Activa la respuesta por voz del asistente.',
      tooltip:
        'Activa la conversión de texto a voz. Si se desactiva, el bot solo responde con texto en pantalla.',
    },
    {
      key: 'enableTypingSound' as const,
      label: 'Sonido de escritura',
      description:
        'Reproduce un sonido de tecleo mientras el bot procesa su respuesta.',
      tooltip:
        'Reproduce un efecto de tecleo mientras el bot piensa, para indicar al usuario que algo está pasando.',
    },
  ];

  constructor() {
    this.voicesService.load();

    effect(() => {
      const bot = this.store.bot();
      if (bot) {
        this.form.patchValue({
          voiceId: bot.voiceId ?? null,
          silenceEndMs: bot.silenceEndMs,
          noSpeechTimeoutMs: bot.noSpeechTimeoutMs,
          maxUtteranceMs: bot.maxUtteranceMs,
          enableBargeIn: bot.enableBargeIn,
          enableTts: bot.enableTts,
          enableTypingSound: bot.enableTypingSound,
          vadActivationThreshold: bot.vadActivationThreshold,
          vadMinSpeechDurationMs: bot.vadMinSpeechDurationMs,
          interruptionMinDurationMs: bot.interruptionMinDurationMs,
          interruptionMinWords: bot.interruptionMinWords,
        });
      }
    });
  }

  onToggle(key: 'enableBargeIn' | 'enableTts' | 'enableTypingSound') {
    this.form.controls[key].setValue(!this.form.controls[key].value);
  }

  onVoiceChange(value: string | null) {
    this.form.controls.voiceId.setValue(value);
  }

  async onRefreshVoices() {
    try {
      await this.voicesService.refresh();
      toast.success('Lista de voces actualizada.');
    } catch {
      toast.error('No se pudo actualizar la lista de voces.');
    }
  }

  async onThinkingSoundSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    const bot = this.store.bot();
    if (!bot) return;

    this.thinkingSoundUploading.set(true);
    try {
      const updated = await this.api.invoke(uploadBotThinkingSound, {
        botId: bot.id,
        body: { file },
      });
      this.store.bot.set(updated);
      toast.success('Sonido personalizado guardado.');
    } catch {
      toast.error('Error al subir el archivo. Usa un .mp3 de hasta 5 MB.');
    } finally {
      this.thinkingSoundUploading.set(false);
      input.value = '';
    }
  }

  async onRemoveThinkingSound() {
    const bot = this.store.bot();
    if (!bot?.hasCustomThinkingSound) return;

    this.thinkingSoundUploading.set(true);
    try {
      await this.api.invoke(deleteBotThinkingSound, { botId: bot.id });
      const updated = await this.api.invoke(getBot, { id: bot.id });
      this.store.bot.set(updated);
      toast.success('Se usará el sonido predeterminado.');
    } catch {
      toast.error('No se pudo eliminar el sonido personalizado.');
    } finally {
      this.thinkingSoundUploading.set(false);
    }
  }

  async onPreviewThinkingSound() {
    const bot = this.store.bot();
    if (!bot) return;

    try {
      let url = DEFAULT_THINKING_SOUND_URL;
      if (bot.hasCustomThinkingSound) {
        const v = encodeURIComponent(bot.thinkingSoundVersion || String(Date.now()));
        url = `${this.api.rootUrl}/api/bots/${bot.id}/thinking-sound/download?v=${v}`;
      }
      const audio = new Audio(url);
      await audio.play();
    } catch {
      toast.error('No se pudo reproducir la vista previa.');
    }
  }

  onDiscard() {
    const bot = this.store.bot();
    if (bot) {
      this.form.patchValue({
        voiceId: bot.voiceId ?? null,
        silenceEndMs: bot.silenceEndMs,
        noSpeechTimeoutMs: bot.noSpeechTimeoutMs,
        maxUtteranceMs: bot.maxUtteranceMs,
        enableBargeIn: bot.enableBargeIn,
        enableTts: bot.enableTts,
        enableTypingSound: bot.enableTypingSound,
        vadActivationThreshold: bot.vadActivationThreshold,
        vadMinSpeechDurationMs: bot.vadMinSpeechDurationMs,
        interruptionMinDurationMs: bot.interruptionMinDurationMs,
        interruptionMinWords: bot.interruptionMinWords,
      });
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
          voiceId: values.voiceId ?? '',
          silenceEndMs: values.silenceEndMs,
          noSpeechTimeoutMs: values.noSpeechTimeoutMs,
          maxUtteranceMs: values.maxUtteranceMs,
          enableBargeIn: values.enableBargeIn,
          enableTts: values.enableTts,
          enableTypingSound: values.enableTypingSound,
          vadActivationThreshold: values.vadActivationThreshold,
          vadMinSpeechDurationMs: values.vadMinSpeechDurationMs,
          interruptionMinDurationMs: values.interruptionMinDurationMs,
          interruptionMinWords: values.interruptionMinWords,
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
