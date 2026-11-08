import {
  Component,
  computed,
  effect,
  inject,
  OnDestroy,
  signal,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { Api } from '../../api/api';
import {
  createChat,
  createPublicChat,
  getBotBySlug,
  resumeConversation,
  resumePublicConversation,
} from '../../api/functions';
import { PublicBotResponse } from '../../api/models/public-bot-response';
import { AuthService } from '../../core/services/auth.service';
import { PublicSessionService } from '../../core/services/public-session.service';
import {
  ConversationLifecycleService,
  type ClientCloseReason,
} from '../../core/services/conversation-lifecycle.service';
import { ConversationStreamService } from '../../core/services/conversation-stream.service';
import { LiveKitService } from '../../core/services/livekit.service';
import { ThinkingSoundService } from '../../core/services/thinking-sound.service';
import { BotBrandingService } from '../../core/services/bot-branding.service';
import { BrandingService } from '../../core/services/branding.service';
import { themeStyleFromBot } from './bot-theme';
import { extractContacts } from './contact-extract';
import { BottomBar } from './components/bottom-bar';
import { ChatPanel } from './components/chat-panel';
import {
  ContactCard,
  type ContactCardData,
} from './components/contact-card';
import { DebugDrawer } from './components/debug-drawer';
import { StatusIndicator } from './components/status-indicator';
import { TextSessionView } from './components/text-session-view';
import {
  VoiceVisualizer,
  type AgentActivity,
} from './components/voice-visualizer';
import {
  canSeeOperatorErrors,
  canSeeTelemetryLogs,
  isIdleAgentClose,
  mapPipelineErrorToMessage,
  mapSessionEndToMessage,
  connectErrorMessage,
  isConversationClosedError,
  mapSendTurnError,
  mapSessionPausedToMessage,
  MSG_CONVERSATION_CLOSED,
  MSG_INACTIVITY_PAUSE,
  type AudioFallbackReason,
} from './chat-session-messages';
import {
  resolvePreferredMode,
  writeStoredInteractionMode,
  type InteractionMode,
} from './interaction-mode.storage';
import { shouldUsePublicSession } from './public-session-mode';

type TerminateMode = 'pause' | 'terminal' | 'userInitiated';

const ADMIN_PERMISSIONS = new Set(['superadmin', 'bot:admin']);
const AGENT_ABSENT_GRACE_MS = 15_000;
const AGENT_NEVER_JOINED_MS = 30_000;
const RECONNECT_STALL_MS = 30_000;

@Component({
  selector: 'bf-chat',
  standalone: true,
  imports: [
    VoiceVisualizer,
    StatusIndicator,
    BottomBar,
    ChatPanel,
    ContactCard,
    DebugDrawer,
    TextSessionView,
  ],
  providers: [ConversationStreamService],
  template: `
    <div class="bf-chat-root fixed inset-0" [style]="themeStyle()">
      <div class="absolute inset-0 overflow-hidden">
      <!-- Top bar: logo + bot name (hidden on the 404 screen) -->
      @if (stage() !== 'not-found') {
        <header class="pointer-events-none absolute inset-x-0 top-0 z-20 flex items-start justify-between px-6 pt-6">
          <div class="pointer-events-auto flex items-center gap-2.5">
            @if (logoUrl()) {
              <img
                [src]="logoUrl()!"
                [alt]="bot()?.botName ?? 'Logo'"
                class="h-8 w-8 rounded-lg object-contain"
              />
            }
            <h1 class="bf-chat-header-title text-lg font-semibold tracking-tight">
              {{ bot()?.botName ?? 'Cargando…' }}
            </h1>
          </div>
        </header>
      }

      <!-- Main stage -->
      <main
        class="relative z-10 flex h-full w-full flex-col"
        [class.items-center]="!(stage() === 'session' && isTextMode())"
        [class.justify-center]="!(stage() === 'session' && isTextMode())"
      >
        @switch (stage()) {
          @case ('not-found') {
            <div class="flex flex-col items-center gap-8 px-6 text-center">
              <div class="relative flex h-48 w-48 items-center justify-center">
                <!-- Soft glow halo -->
                <div class="absolute inset-0 rounded-full bg-gradient-to-br from-rose-500/10 via-fuchsia-500/5 to-transparent blur-2xl"></div>
                <!-- Big 404 mark -->
                <div class="relative flex h-40 w-40 items-center justify-center rounded-full border border-white/10 bg-white/[0.03]">
                  <span class="bg-gradient-to-br from-white to-neutral-500 bg-clip-text font-mono text-5xl font-bold tracking-tight text-transparent">
                    404
                  </span>
                </div>
              </div>

              <div class="max-w-md space-y-3">
                <h2 class="text-2xl font-semibold text-white">
                  Este bot no existe
                </h2>
                <p class="text-sm leading-relaxed text-neutral-400">
                  No pudimos encontrar un bot publicado con la dirección
                  <span class="rounded bg-white/[0.06] px-1.5 py-0.5 font-mono text-xs text-neutral-300">/chat/{{ slug() }}</span>.
                  Verifica el enlace o vuelve al inicio.
                </p>
              </div>

              @if (auth.isAuthenticated() && !embedded) {
                <div class="flex flex-wrap items-center justify-center gap-3">
                  <button
                    type="button"
                    class="flex items-center gap-2 rounded-full bg-white px-5 py-2.5 text-sm font-semibold text-neutral-900 shadow-lg transition-transform hover:scale-[1.02]"
                    (click)="onGoHome()"
                  >
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="h-4 w-4">
                      <path d="m3 9 9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" />
                      <polyline points="9 22 9 12 15 12 15 22" />
                    </svg>
                    Volver al inicio
                  </button>
                </div>
              } @else if (!auth.isAuthenticated() && !embedded) {
                <p class="text-xs text-neutral-500">
                  ¿Es un bot privado? <button class="font-medium text-neutral-300 underline-offset-2 hover:underline" (click)="onLogin()">Inicia sesión</button>.
                </p>
              }
            </div>
          }

          @case ('auth-required') {
            <div class="flex flex-col items-center gap-8 px-6 text-center">
              <div class="flex h-60 w-60 items-center justify-center rounded-full bg-white/5">
                <div class="h-40 w-40 animate-[bf-breathe_3.2s_ease-in-out_infinite] rounded-full bg-white/70"></div>
              </div>

              <div class="max-w-md space-y-2">
                <h2 class="text-2xl font-semibold text-white">
                  Inicia sesión para empezar a hablar
                </h2>
                <p class="text-sm text-neutral-400">
                  @if (embedded) {
                    Este asistente requiere iniciar sesión, lo cual no está disponible dentro de una página incrustada. Ábrelo en una pestaña nueva.
                  } @else {
                    Necesitas una cuenta para conversar con este bot. Te llevaremos a iniciar sesión y volverás automáticamente aquí.
                  }
                </p>
              </div>

              @if (embedded) {
                <button
                  type="button"
                  class="flex items-center gap-2 rounded-full bg-white px-6 py-3 text-sm font-semibold text-neutral-900 shadow-lg transition-transform hover:scale-[1.02]"
                  (click)="onOpenDirectLink()"
                >
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="h-4 w-4">
                    <path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6" />
                    <polyline points="15 3 21 3 21 9" />
                    <line x1="10" y1="14" x2="21" y2="3" />
                  </svg>
                  Abrir en una pestaña nueva
                </button>
              } @else {
                <button
                  type="button"
                  class="flex items-center gap-2 rounded-full bg-white px-6 py-3 text-sm font-semibold text-neutral-900 shadow-lg transition-transform hover:scale-[1.02] disabled:opacity-50"
                  [disabled]="loggingIn()"
                  (click)="onLogin()"
                >
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="h-4 w-4">
                    <path d="M15 3h4a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-4" />
                    <polyline points="10 17 15 12 10 7" />
                    <line x1="15" y1="12" x2="3" y2="12" />
                  </svg>
                  {{ loggingIn() ? 'Redirigiendo…' : 'Iniciar sesión' }}
                </button>
              }

              @if (error(); as errMsg) {
                <p class="max-w-sm rounded-lg border border-red-600 bg-red-50 px-4 py-2 text-sm font-medium text-red-950">
                  {{ errMsg }}
                </p>
              }
            </div>
          }

          @case ('pre-connect') {
            <div class="flex flex-col items-center gap-8 px-6 text-center">
              <div class="bf-chat-hero-ring flex h-60 w-60 items-center justify-center rounded-full">
                <div class="bf-chat-hero-blob h-40 w-40 animate-[bf-breathe_3.2s_ease-in-out_infinite] rounded-full"></div>
              </div>

              <div class="max-w-md space-y-2">
                <h2 class="bf-chat-header-title text-2xl font-semibold">
                  {{ bot()?.botName ?? 'Bot Factory' }}
                </h2>
                <p class="bf-chat-muted text-sm">
                  @if (bot()?.enableVoice && bot()?.enableChat) {
                    Habla con tu micrófono. También puedes escribir si lo prefieres.
                  } @else if (bot()?.enableChat) {
                    Escribe tus mensajes y recibe respuestas en tiempo real.
                  } @else {
                    Conecta tu micrófono y comienza a conversar. Tu voz se transcribe en tiempo real y las respuestas se reproducen con audio.
                  }
                </p>
              </div>

              <div class="flex w-full max-w-xs flex-col items-center gap-4">
                @if (bot()?.enableVoice) {
                  <button
                    type="button"
                    class="bf-chat-cta flex w-full items-center justify-center gap-2 rounded-full px-6 py-3.5 text-sm font-semibold shadow-lg transition-transform hover:scale-[1.02] disabled:opacity-50"
                    [disabled]="connecting() || !bot()"
                    (click)="onStartSession('voice')"
                  >
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="h-4 w-4">
                      <path d="M12 14a3 3 0 0 0 3-3V6a3 3 0 1 0-6 0v5a3 3 0 0 0 3 3Z" />
                      <path d="M19 11a1 1 0 1 0-2 0 5 5 0 0 1-10 0 1 1 0 1 0-2 0 7 7 0 0 0 6 6.92V20H9a1 1 0 1 0 0 2h6a1 1 0 1 0 0-2h-2v-2.08A7 7 0 0 0 19 11Z" />
                    </svg>
                    {{ connecting() ? 'Conectando…' : 'Conectar y Hablar' }}
                  </button>
                }

                @if (bot()?.enableChat && bot()?.enableVoice) {
                  <button
                    type="button"
                    class="bf-chat-muted text-sm underline-offset-2 transition-opacity hover:opacity-80 hover:underline disabled:opacity-50"
                    [disabled]="connecting() || !bot()"
                    (click)="onStartSession('text')"
                  >
                    {{ connecting() ? 'Conectando…' : 'Prefiero escribir por chat' }}
                  </button>
                } @else if (bot()?.enableChat) {
                  <button
                    type="button"
                    class="bf-chat-cta flex w-full items-center justify-center gap-2 rounded-full px-6 py-3.5 text-sm font-semibold shadow-lg transition-transform hover:scale-[1.02] disabled:opacity-50"
                    [disabled]="connecting() || !bot()"
                    (click)="onStartSession('text')"
                  >
                    {{ connecting() ? 'Conectando…' : 'Comenzar conversación' }}
                  </button>
                }
              </div>

              @if (error(); as errMsg) {
                <p class="max-w-sm rounded-lg border border-red-600 bg-red-50 px-4 py-2 text-sm font-medium text-red-950">
                  {{ errMsg }}
                </p>
              }

              <!-- Privacy/consent notice for anonymous public use. Wording
                   pending legal review — see docs/public-access.md. -->
              @if (isPublicBot() && !auth.isAuthenticated()) {
                <p class="max-w-md text-[11px] leading-relaxed text-neutral-500">
                  Esta conversación puede quedar registrada para mejorar el servicio.
                  No compartas datos personales sensibles como cédula, contraseñas o información financiera.
                </p>
              }
            </div>
          }

          @case ('connecting') {
            <div class="flex flex-col items-center gap-4">
              <div
                class="h-16 w-16 animate-spin rounded-full border-2"
                style="border-color: color-mix(in srgb, var(--bf-fg) 22%, transparent); border-top-color: var(--bf-accent)"
              ></div>
              <p class="bf-chat-muted text-sm">{{ connectionSubtitle() }}</p>
            </div>
          }

          @case ('session-ended') {
            <div class="flex flex-col items-center gap-8 px-6 text-center">
              <div class="flex h-60 w-60 items-center justify-center rounded-full bg-white/5">
                <div class="h-40 w-40 rounded-full bg-white/40"></div>
              </div>

              <div class="max-w-md space-y-2">
                <h2 class="text-2xl font-semibold text-white">
                  Conversación finalizada
                </h2>
                <p class="text-sm text-neutral-400">
                  {{ sessionEndedMessage() }}
                </p>
              </div>

              <button
                type="button"
                class="flex items-center gap-2 rounded-full bg-white px-6 py-3 text-sm font-semibold text-neutral-900 shadow-lg transition-transform hover:scale-[1.02] disabled:opacity-50"
                [disabled]="connecting() || !bot()"
                (click)="onStartSession()"
              >
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="h-4 w-4">
                  <path d="M12 14a3 3 0 0 0 3-3V6a3 3 0 1 0-6 0v5a3 3 0 0 0 3 3Z" />
                  <path d="M19 11a1 1 0 1 0-2 0 5 5 0 0 1-10 0 1 1 0 1 0-2 0 7 7 0 0 0 6 6.92V20H9a1 1 0 1 0 0 2h6a1 1 0 1 0 0-2h-2v-2.08A7 7 0 0 0 19 11Z" />
                </svg>
                {{ connecting() ? 'Conectando…' : 'Nueva conversación' }}
              </button>
            </div>
          }

          @case ('session') {
            @if (isTextMode()) {
              <bf-text-session-view
                #textSession
                class="h-full min-h-0 w-full self-stretch"
                [items]="stream.displayItems()"
                [loading]="stream.loading()"
                [sending]="stream.sendBusy()"
                [thinking]="stream.sendBusy() && !stream.streamingText()"
                [showEvents]="adminMode()"
                [showVoiceSwitch]="!!bot()?.enableVoice"
                [isAdmin]="isAdmin()"
                [fallbackReason]="audioFallbackReason()"
                [turnError]="stream.turnError()"
                [sessionError]="error()"
                (send)="onSendText($event)"
                (stop)="onStopTurn()"
                (useVoice)="onSwitchToVoice()"
                (openParams)="paramsOpen.set(true)"
                (toggleAdmin)="toggleAdminMode()"
              />
            } @else {
              <div class="flex h-full w-full flex-col items-center justify-center gap-10">
                <div class="flex h-96 w-96 items-center justify-center">
                  <bf-voice-visualizer
                    [activity]="activity()"
                    [audioTrack]="lk.agentAudioTrack()"
                    [localAudioTrack]="lk.localAudioTrack()"
                    [muted]="!lk.isMicrophoneEnabled()"
                  />
                </div>

                <bf-status-indicator
                  [activity]="activity()"
                  [connectionLabel]="statusOverride()"
                />
              </div>
            }
          }
        }
      </main>

      <!-- Error toast above bottom bar -->
      @if (error() && stage() === 'session' && !isTextMode()) {
        <div class="pointer-events-auto absolute bottom-32 left-1/2 z-30 -translate-x-1/2 rounded-lg bg-red-500/20 px-4 py-2 text-sm text-red-200 shadow-lg backdrop-blur">
          {{ error() }}
        </div>
      }

      <!-- Voice contact card: links/phones from the latest assistant turn -->
      @if (contactCard() && stage() === 'session' && !isTextMode()) {
        <div class="pointer-events-none absolute bottom-32 left-1/2 z-30 w-[min(100%-2rem,22rem)] -translate-x-1/2">
          <bf-contact-card [data]="contactCard()!" (closed)="dismissContactCard()" />
        </div>
      }

      <!-- Bottom bar (only during session). Swaps to a paused-actions pill
           when the conversation is paused. Only ONE is rendered at a time so
           there's no invisible overlay capturing clicks. -->
      @if (stage() === 'session' && !isTextMode()) {
        <div class="pointer-events-none absolute inset-x-0 bottom-0 z-20 flex justify-center pb-8">
          @if (activity() === 'paused') {
            <div
              class="pointer-events-auto flex items-center gap-2 rounded-full border border-white/10 bg-neutral-900/80 px-3 py-2 shadow-2xl backdrop-blur-xl"
            >
              <button
                type="button"
                class="flex h-12 items-center gap-2 rounded-full bg-white px-5 text-sm font-semibold text-neutral-900 transition-transform duration-150 ease-out hover:scale-[1.02] active:scale-[0.97] disabled:opacity-50"
                [disabled]="connecting() || !bot()"
                (click)="onResume()"
              >
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="h-4 w-4">
                  <path d="M7 5.5a1 1 0 0 1 1.55-.83l11 6.5a1 1 0 0 1 0 1.66l-11 6.5A1 1 0 0 1 7 18.5v-13Z" />
                </svg>
                {{ connecting() ? 'Conectando…' : 'Reanudar' }}
              </button>
              <button
                type="button"
                class="flex h-12 items-center gap-2 rounded-full bg-neutral-800 px-4 text-sm font-medium text-neutral-200 transition-colors duration-150 ease-out hover:bg-neutral-700 active:scale-[0.97] disabled:opacity-50"
                [disabled]="connecting() || !bot()"
                (click)="onStartSession()"
              >
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="h-4 w-4">
                  <path d="M12 4a1 1 0 0 1 1 1v6h6a1 1 0 1 1 0 2h-6v6a1 1 0 1 1-2 0v-6H5a1 1 0 1 1 0-2h6V5a1 1 0 0 1 1-1Z" />
                </svg>
                Nueva
              </button>
              <span class="mx-1 h-8 w-px bg-white/10"></span>
              <button
                type="button"
                class="flex h-12 items-center gap-2 rounded-full bg-red-500/90 px-4 text-sm font-semibold text-white transition-colors duration-150 ease-out hover:bg-red-500 active:scale-[0.97]"
                (click)="onDisconnect()"
                title="Terminar sesión"
                aria-label="Cerrar sesión"
              >
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="h-4 w-4">
                  <path d="M6.2 4.8a1 1 0 0 1 1.4 0L12 9.2l4.4-4.4a1 1 0 1 1 1.4 1.4L13.4 10.6l4.4 4.4a1 1 0 1 1-1.4 1.4L12 12l-4.4 4.4a1 1 0 1 1-1.4-1.4l4.4-4.4-4.4-4.4a1 1 0 0 1 0-1.4Z" />
                </svg>
                Cerrar
              </button>
            </div>
          } @else {
            <bf-bottom-bar
              [micEnabled]="lk.isMicrophoneEnabled()"
              [panelOpen]="chatOpen()"
              [showPanel]="canSeeLogs()"
              [showUseChat]="!!bot()?.enableChat"
              (toggleMic)="onToggleMic()"
              (togglePanel)="toggleChat()"
              (useChat)="onSwitchToText()"
              (pause)="onPause()"
              (close)="onDisconnect()"
            />
          }
        </div>
      }

      </div>

      @if (chatOpen() && canSeeLogs()) {
        <button
          type="button"
          class="pointer-events-auto fixed inset-0 z-30 bg-black/40 backdrop-blur-[1px]"
          aria-label="Cerrar panel de conversación"
          (click)="chatOpen.set(false)"
        ></button>
      }

      <bf-chat-panel
        [open]="chatOpen() && canSeeLogs()"
        [items]="stream.items()"
        [loading]="stream.loading()"
        [showEvents]="adminMode()"
        [isAdmin]="isAdmin()"
        (closed)="chatOpen.set(false)"
        (toggleAdmin)="toggleAdminMode()"
        (openParams)="paramsOpen.set(true)"
      />

      @if (isAdmin()) {
        <bf-debug-drawer
          [open]="paramsOpen()"
          [bot]="bot()"
          (closed)="paramsOpen.set(false)"
        />
      }
    </div>
  `,
  styles: [
    `
      @keyframes bf-breathe {
        0%, 100% { transform: scale(1); }
        50% { transform: scale(1.04); }
      }
      .bf-chat-root {
        background: var(--bf-bg);
        color: var(--bf-fg);
      }
      .bf-chat-cta {
        background: var(--bf-accent);
        color: var(--bf-cta-fg);
      }
      .bf-chat-header-title {
        color: var(--bf-primary);
      }
      .bf-chat-muted {
        color: var(--bf-muted);
      }
      .bf-chat-hero-ring {
        background: color-mix(in srgb, var(--bf-accent) 18%, transparent);
      }
      .bf-chat-hero-blob {
        background: color-mix(in srgb, var(--bf-accent) 85%, transparent);
        box-shadow: 0 0 80px 20px color-mix(in srgb, var(--bf-accent) 30%, transparent);
      }
    `,
  ],
})
export class Chat implements OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(Api);
  private readonly conversationLifecycle = inject(ConversationLifecycleService);
  private readonly publicSession = inject(PublicSessionService);
  readonly auth = inject(AuthService);
  readonly lk = inject(LiveKitService);
  private readonly thinkingSound = inject(ThinkingSoundService);
  private readonly branding = inject(BotBrandingService);
  private readonly brandingService = inject(BrandingService);
  readonly stream = inject(ConversationStreamService);
  private readonly textSession = viewChild<TextSessionView>('textSession');

  readonly bot = signal<PublicBotResponse | null>(null);
  readonly logoUrl = signal<string | null>(null);
  readonly themeStyle = computed(() => themeStyleFromBot(this.bot()));
  readonly connecting = signal(false);
  readonly loggingIn = signal(false);
  readonly error = signal<string | null>(null);
  readonly chatOpen = signal(false);
  readonly paramsOpen = signal(false);
  readonly slug = signal<string>('');
  readonly notFound = signal(false);
  readonly loadingBot = signal(true);
  private readonly conversationId = signal<number | null>(null);
  private readonly sessionEndedExplicitly = signal(false);
  readonly sessionEndedMessage = signal<string | null>(null);
  readonly sessionPausedMessage = signal<string | null>(null);
  readonly interactionMode = signal<InteractionMode>('voice');
  readonly textSessionActive = signal(false);
  readonly audioFallbackReason = signal<AudioFallbackReason | null>(null);
  private sessionTeardownInProgress = false;
  private readonly pageHideHandler = () => this.closeSessionIfNeeded('user_aborted');

  /** Voice-only contact card (URLs + phones) from the latest assistant message. */
  readonly contactCard = signal<ContactCardData | null>(null);
  private contactCardMessageId: number | null = null;

  readonly isAdmin = computed(() => {
    const perms = this.auth.user()?.permissions ?? [];
    return perms.some((p) => ADMIN_PERMISSIONS.has(p));
  });

  // Rendered inside an iframe via /api/embed/{publicId} (route data flag).
  // Suppresses navigation that would take over the host page.
  readonly embedded: boolean = this.route.snapshot.data['embedded'] === true;

  readonly isPublicBot = computed(
    () => this.bot()?.requiresAuthentication === false,
  );

  /**
   * Anonymous visitors talk to public bots through the /api/public surface
   * with an in-memory token. Logged-in users keep the authenticated flow even
   * on public bots, so their conversations stay attributed to them.
   */
  private usePublicSession(): boolean {
    return shouldUsePublicSession(this.isPublicBot(), this.auth.isAuthenticated());
  }

  readonly showOperatorErrors = computed(() =>
    canSeeOperatorErrors(this.auth.user()?.permissions ?? []),
  );

  readonly canSeeLogs = computed(() =>
    canSeeTelemetryLogs(this.auth.user()?.permissions ?? []),
  );

  // Reactive view of the URL query params so admin-mode toggles re-render.
  private readonly queryParams = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });

  // Admin mode: URL-driven (`?admin=1`), gated on isAdmin. When on, the debug
  // drawer is visible and events show inline in the chat feed.
  readonly adminMode = computed(() => {
    if (!this.isAdmin()) return false;
    return this.queryParams().get('admin') === '1';
  });

  readonly isTextMode = computed(
    () => this.interactionMode() === 'text' && this.textSessionActive(),
  );

  readonly stage = computed<
    | 'not-found'
    | 'loading'
    | 'auth-required'
    | 'pre-connect'
    | 'connecting'
    | 'session'
    | 'session-ended'
  >(() => {
    // 404 wins over everything else: if the bot doesn't exist (or isn't
    // published), unauthenticated and authenticated visitors alike must see
    // the same dead-end screen rather than a misleading login prompt.
    if (this.notFound()) return 'not-found';
    // Paused conversations stay on the session screen so the visualizer
    // morphs in place rather than the layout collapsing to a different view.
    if (this.sessionPausedMessage()) return 'session';
    if (this.sessionEndedMessage()) return 'session-ended';

    if (this.textSessionActive()) return 'session';

    const state = this.lk.connectionState();
    if (state === 'connected' || state === 'reconnecting') return 'session';
    if (state === 'connecting' || this.connecting()) return 'connecting';

    // Wait for the slug lookup before deciding whether to show the login gate
    // or the connect screen, so we don't briefly flash the wrong state.
    if (this.loadingBot()) return 'loading';
    // Public bots skip the login gate entirely for anonymous visitors.
    if (!this.auth.isAuthenticated() && !this.isPublicBot()) return 'auth-required';
    return 'pre-connect';
  });

  readonly activity = computed<AgentActivity>(() => {
    if (this.sessionPausedMessage()) return 'paused';
    if (this.lk.connectionState() !== 'connected') return 'idle';
    const raw = (this.lk.agentState() ?? '').toLowerCase();
    if (raw.includes('listen')) return 'listening';
    if (raw.includes('think') || raw.includes('process')) return 'thinking';
    if (raw.includes('speak')) return 'speaking';
    return 'idle';
  });

  readonly connectionSubtitle = computed(() => {
    if (this.connecting() || this.lk.connectionState() === 'connecting') {
      return 'Conectando…';
    }
    switch (this.lk.connectionState()) {
      case 'connected':
        return this.lk.isAgentConnected()
          ? 'Conectado'
          : 'Conectado — esperando al agente…';
      case 'reconnecting':
        return 'Reconectando…';
      default:
        return 'Desconectado';
    }
  });

  readonly statusOverride = computed<string | null>(() => {
    if (this.sessionPausedMessage()) return 'En pausa';
    const state = this.lk.connectionState();
    if (state === 'reconnecting') return 'Reconectando…';
    if (state === 'connected' && !this.lk.isAgentConnected()) {
      if (this.lk.agentEverConnected()) {
        return 'Reconectando con el agente…';
      }
      return 'Esperando al agente…';
    }
    if (!this.lk.isMicrophoneEnabled() && this.activity() === 'listening') {
      return 'Activa el micrófono para hablar';
    }
    return null;
  });

  constructor() {
    // Resolve the bot from the URL slug as soon as the component mounts.
    const slug = this.route.snapshot.params['slug'] ?? '';
    this.slug.set(slug);
    void this.loadBot(slug);

    effect((onCleanup) => {
      let cancelled = false;
      onCleanup(() => {
        cancelled = true;
        this.thinkingSound.stop();
      });

      const bot = this.bot();
      // Typing sound is voice-only; text chat uses a visual typing indicator.
      const shouldPlay =
        bot?.enableTypingSound &&
        this.lk.connectionState() === 'connected' &&
        this.activity() === 'thinking';

      if (!shouldPlay || !bot) {
        this.thinkingSound.stop();
        return;
      }

      const slug = this.slug();
      void (async () => {
        try {
          const url = this.thinkingSound.resolveUrl(bot, slug);
          if (cancelled) return;
          await this.thinkingSound.play(url);
        } catch {
          if (!cancelled) this.thinkingSound.stop();
        }
      })();
    });

    effect(() => {
      if (!this.isTextMode() || !this.stream.turnInFlight()) return;
      this.textSession()?.clearDraft();
    });

    // Voice contact card: show when a new assistant message contains URLs/phones.
    effect(() => {
      if (this.isTextMode() || this.stage() !== 'session') {
        if (this.contactCard()) this.dismissContactCard();
        return;
      }

      const items = this.stream.items();
      let latestMessage: {
        id: number;
        role: string;
        content: string;
      } | null = null;
      for (let i = items.length - 1; i >= 0; i--) {
        const item = items[i];
        if (item.kind !== 'message') continue;
        latestMessage = item.data;
        break;
      }
      if (!latestMessage) return;

      if (latestMessage.role === 'User') {
        if (this.contactCard()) this.dismissContactCard();
        return;
      }

      if (latestMessage.role !== 'Assistant') return;
      if (latestMessage.id === this.contactCardMessageId) return;

      const contacts = extractContacts(latestMessage.content);
      if (
        contacts.urls.length === 0 &&
        contacts.phones.length === 0 &&
        contacts.emails.length === 0
      ) {
        this.contactCardMessageId = latestMessage.id;
        return;
      }

      this.showContactCard(contacts, latestMessage.id);
    });

    effect(() => {
      const paused = this.lk.sessionPaused();
      const convId = this.conversationId();
      if (!paused || convId === null || this.sessionTeardownInProgress) return;
      void this.terminateSession({
        mode: 'pause',
        message: mapSessionPausedToMessage(paused),
      });
    });

    effect(() => {
      const end = this.lk.sessionEnd();
      const convId = this.conversationId();
      if (
        !end ||
        convId === null ||
        this.connecting() ||
        this.sessionTeardownInProgress ||
        this.sessionPausedMessage() ||
        this.isTextInteraction()
      ) {
        return;
      }
      if (isIdleAgentClose(end)) {
        void this.terminateSession({
          mode: 'pause',
          message: mapSessionPausedToMessage({ reason: end.reason }),
        });
        return;
      }

      void this.terminateSession({
        mode: 'terminal',
        message: mapSessionEndToMessage(end, this.showOperatorErrors()),
        closeReason: 'user_aborted',
        skipApiClose: end.source === 'close',
      });
    });

    effect(() => {
      const pipelineErr = this.lk.pipelineError();
      const convId = this.conversationId();
      if (!pipelineErr || convId === null || this.sessionTeardownInProgress) {
        return;
      }
      const message = mapPipelineErrorToMessage(
        pipelineErr,
        this.showOperatorErrors(),
      );
      if (this.showOperatorErrors()) {
        console.error(
          '[voice]',
          pipelineErr.operatorMessage ?? message,
          pipelineErr.rawError ?? '',
        );
      }
      if (this.canFallbackToText()) {
        void this.fallbackToTextMode('pipeline_error', message);
        return;
      }
      this.error.set(message);
    });

    effect(() => {
      const state = this.lk.connectionState();
      const convId = this.conversationId();
      if (
        state !== 'disconnected' ||
        convId === null ||
        this.connecting() ||
        this.sessionTeardownInProgress ||
        this.sessionEndedMessage() ||
        this.sessionPausedMessage() ||
        this.isTextInteraction()
      ) {
        return;
      }
      const end = this.lk.sessionEnd();
      if (end?.source === 'close') return;
      void this.terminateSession({
        mode: 'terminal',
        message:
          'Se perdió la conexión. Puedes iniciar una nueva conversación.',
        closeReason: 'user_aborted',
      });
    });

    effect((onCleanup) => {
      const state = this.lk.connectionState();
      const agentConnected = this.lk.isAgentConnected();
      const everConnected = this.lk.agentEverConnected();
      const convId = this.conversationId();

      if (
        state !== 'connected' ||
        agentConnected ||
        !everConnected ||
        convId === null ||
        this.sessionPausedMessage() ||
        this.isTextInteraction()
      ) {
        return;
      }

      const timer = window.setTimeout(() => {
        void this.terminateSession({
          mode: 'terminal',
          message: 'La conversación terminó. El asistente se desconectó.',
          closeReason: 'user_aborted',
        });
      }, AGENT_ABSENT_GRACE_MS);

      onCleanup(() => window.clearTimeout(timer));
    });

    effect((onCleanup) => {
      const state = this.lk.connectionState();
      const everConnected = this.lk.agentEverConnected();
      const agentState = this.lk.agentState();
      const hasAgentAudio = this.lk.agentAudioTrack() !== null;
      const convId = this.conversationId();

      if (
        state !== 'connected' ||
        everConnected ||
        !!agentState ||
        hasAgentAudio ||
        convId === null ||
        this.sessionPausedMessage() ||
        this.isTextInteraction()
      ) {
        return;
      }

      const timer = window.setTimeout(() => {
        // Re-check at fire time — agent may have joined just before the timer.
        if (
          this.lk.agentEverConnected() ||
          this.lk.agentState() ||
          this.lk.agentAudioTrack() !== null
        ) {
          return;
        }
        if (this.canFallbackToText()) {
          void this.fallbackToTextMode(
            'agent_timeout',
            'No pudimos conectar con el asistente.',
          );
          return;
        }
        void this.terminateSession({
          mode: 'terminal',
          message: 'No pudimos conectar con el asistente. Intenta de nuevo.',
          closeReason: 'user_aborted',
        });
      }, AGENT_NEVER_JOINED_MS);

      onCleanup(() => window.clearTimeout(timer));
    });

    effect((onCleanup) => {
      if (this.lk.connectionState() !== 'reconnecting') return;
      if (this.conversationId() === null) return;
      if (this.isTextInteraction()) return;

      const timer = window.setTimeout(() => {
        if (
          this.lk.connectionState() === 'reconnecting' &&
          !this.sessionPausedMessage()
        ) {
          void this.terminateSession({
            mode: 'terminal',
            message: 'No pudimos restablecer la conexión.',
            closeReason: 'user_aborted',
          });
        }
      }, RECONNECT_STALL_MS);

      onCleanup(() => window.clearTimeout(timer));
    });

    // Mirror admin-mode → events subscription on the existing hub connection.
    effect(() => {
      const on = this.adminMode();
      if (!this.stream || this.conversationId() === null) return;
      void (on ? this.stream.subscribeEvents() : this.stream.unsubscribeEvents());
    });

    if (typeof window !== 'undefined') {
      window.addEventListener('pagehide', this.pageHideHandler);
    }
  }

  ngOnDestroy() {
    if (typeof window !== 'undefined') {
      window.removeEventListener('pagehide', this.pageHideHandler);
    }
    this.dismissContactCard();
    void this.stream.cancelActiveTurn();
    this.closeSessionIfNeeded('user_aborted');
    this.thinkingSound.reset();
    this.lk.disconnect();
    void this.stream.disconnect();
    this.publicSession.clear();
  }

  private showContactCard(data: ContactCardData, messageId: number): void {
    this.contactCardMessageId = messageId;
    this.contactCard.set(data);
  }

  /** Bound from the contact-card template `(closed)` output. */
  dismissContactCard(): void {
    if (this.contactCard() !== null) {
      this.contactCard.set(null);
    }
  }

  private async loadBot(slug: string) {
    if (!slug) {
      this.notFound.set(true);
      this.loadingBot.set(false);
      return;
    }

    try {
      const bot = await this.api.invoke(getBotBySlug, { slug });
      this.bot.set(bot);
      try {
        this.logoUrl.set(this.branding.resolveLogoUrl(bot, slug));
      } catch {
        this.logoUrl.set(null);
      }
      void this.brandingService.loadFaviconForChat(slug);
    } catch (err) {
      // The generated client throws on non-2xx. A 404 means the bot doesn't
      // exist or isn't published for this caller — render the 404 page rather
      // than a generic error.
      if (err instanceof HttpErrorResponse && err.status === 404) {
        this.notFound.set(true);
      } else {
        this.error.set('Error al cargar el bot.');
      }
    } finally {
      this.loadingBot.set(false);
    }
  }

  onGoHome() {
    void this.router.navigateByUrl('/');
  }

  /** Embedded mode: escape the iframe for flows it can't host (login). */
  onOpenDirectLink() {
    const key = this.bot()?.urlSlug || this.slug();
    window.open(`/chat/${key}`, '_blank', 'noopener');
  }

  async onLogin() {
    this.loggingIn.set(true);
    this.error.set(null);
    try {
      const returnUrl = this.router.url || `/chat/${this.route.snapshot.params['slug']}`;
      this.auth.setPostLoginReturnUrl(returnUrl);
      const providerUrl = await this.auth.getLoginUrl('gobid');
      window.location.href = providerUrl;
    } catch (err: unknown) {
      const message =
        err instanceof Error ? err.message : 'No se pudo iniciar sesión.';
      this.error.set(message);
      this.loggingIn.set(false);
    }
  }

  async onStartSession(mode?: InteractionMode) {
    const bot = this.bot();
    if (!bot) return;

    const slug = this.slug();
    const resolvedMode =
      mode ??
      resolvePreferredMode(slug, bot.enableChat, bot.enableVoice);
    writeStoredInteractionMode(slug, resolvedMode);

    this.thinkingSound.unlock();
    if (bot.enableTypingSound) {
      void this.thinkingSound.preload(this.thinkingSound.resolveUrl(bot, slug));
    }

    this.connecting.set(true);
    this.sessionEndedMessage.set(null);
    this.sessionPausedMessage.set(null);
    this.lk.clearSessionEnd();
    this.lk.clearSessionPaused();
    this.error.set(null);
    this.audioFallbackReason.set(null);
    this.interactionMode.set(resolvedMode);

    try {
      const previousId = this.conversationId();
      if (previousId !== null) {
        try {
          await this.closeConversationForMode(bot, previousId, 'user_ended');
        } catch {
          /* best-effort */
        }
        this.conversationId.set(null);
        this.sessionEndedExplicitly.set(false);
      }

      const isPublic = this.usePublicSession();
      const chat = isPublic
        ? await this.startPublicSession(bot, resolvedMode)
        : await this.api.invoke(createChat, {
            id: bot.id,
            mode: resolvedMode,
          });
      this.conversationId.set(chat.conversationId);
      this.sessionEndedExplicitly.set(false);

      await this.stream.connect(bot.id, chat.conversationId, {
        withEvents: this.adminMode(),
        publicAccessId: isPublic ? bot.publicId : undefined,
      });

      if (resolvedMode === 'text') {
        this.textSessionActive.set(true);
        this.lk.disconnect();
        this.lk.clearSessionEnd();
        this.lk.clearSessionPaused();
        return;
      }

      this.textSessionActive.set(false);
      if (!chat.token || !chat.url) {
        throw new Error('No se recibió la sesión de voz.');
      }
      await this.lk.connect(chat.token, chat.url);
      try {
        await this.lk.enableMicrophone();
      } catch {
        if (this.canFallbackToText()) {
          await this.fallbackToTextMode('mic_denied');
          return;
        }
        throw new Error('No se pudo activar el micrófono.');
      }
    } catch (err: unknown) {
      if (this.canFallbackToText() && this.interactionMode() === 'voice') {
        const convId = this.conversationId();
        if (convId !== null) {
          await this.fallbackToTextMode('connect_failed');
          return;
        }
      }
      this.error.set(connectErrorMessage(err));
      this.lk.disconnect();
      this.textSessionActive.set(false);
      void this.stream.disconnect();
    } finally {
      this.connecting.set(false);
    }
  }

  /** @deprecated use onStartSession */
  async onConnect() {
    await this.onStartSession('voice');
  }

  private async startPublicSession(
    bot: PublicBotResponse,
    mode: InteractionMode,
  ): Promise<{ conversationId: number; token?: string | null; url?: string | null }> {
    // Best-effort observability context: page URL (path only — the backend
    // also strips query strings) and, when framed, the host page's origin
    // from the initial referrer.
    const inIframe = typeof window !== 'undefined' && window.self !== window.top;
    let parentOrigin: string | null = null;
    if (inIframe && document.referrer) {
      try {
        parentOrigin = new URL(document.referrer).origin;
      } catch {
        /* unparseable referrer — skip */
      }
    }

    const chat = await this.api.invoke(createPublicChat, {
      publicId: bot.publicId,
      mode,
      body: {
        sourceUrl: location.origin + location.pathname,
        parentOrigin,
        embedded: this.embedded || inIframe,
      },
    });
    this.publicSession.start(bot.publicId, chat.accessToken, chat.accessTokenExpiresAt);
    return chat;
  }

  private async resumeConversationForMode(
    bot: PublicBotResponse,
    conversationId: number,
    mode: InteractionMode,
  ): Promise<{ conversationId: number; token?: string | null; url?: string | null }> {
    if (this.usePublicSession()) {
      const chat = await this.api.invoke(resumePublicConversation, {
        publicId: bot.publicId,
        id: conversationId,
        mode,
      });
      // Resume re-mints the anonymous token; keep the freshest one.
      this.publicSession.refresh(chat.accessToken, chat.accessTokenExpiresAt);
      return chat;
    }
    return this.api.invoke(resumeConversation, {
      botId: bot.id,
      id: conversationId,
      mode,
    });
  }

  private async closeConversationForMode(
    bot: PublicBotResponse,
    conversationId: number,
    reason: ClientCloseReason,
  ): Promise<void> {
    if (this.usePublicSession()) {
      await this.conversationLifecycle.closePublic(bot.publicId, conversationId, reason);
      return;
    }
    await this.conversationLifecycle.close(bot.id, conversationId, reason);
  }

  async onResume() {
    const bot = this.bot();
    const convId = this.conversationId();
    if (!bot || convId === null) return;

    this.thinkingSound.unlock();
    if (bot.enableTypingSound) {
      void this.thinkingSound.preload(this.thinkingSound.resolveUrl(bot, this.slug()));
    }

    this.connecting.set(true);
    this.sessionEndedMessage.set(null);
    this.sessionPausedMessage.set(null);
    this.lk.clearSessionPaused();
    this.lk.clearSessionEnd();
    this.error.set(null);

    try {
      const chat = await this.resumeConversationForMode(bot, convId, 'voice');
      this.conversationId.set(chat.conversationId);
      this.sessionEndedExplicitly.set(false);
      this.interactionMode.set('voice');
      this.textSessionActive.set(false);
      this.audioFallbackReason.set(null);
      if (!chat.token || !chat.url) {
        throw new Error('No se recibió la sesión de voz.');
      }
      await this.lk.connect(chat.token, chat.url);
      await this.lk.enableMicrophone();
      if (this.stream.state() === 'disconnected') {
        await this.stream.connect(bot.id, chat.conversationId, {
          withEvents: this.adminMode(),
          publicAccessId: this.usePublicSession() ? bot.publicId : undefined,
        });
      }
    } catch (err: unknown) {
      this.lk.disconnect();
      if (err instanceof HttpErrorResponse && err.status === 410) {
        this.conversationId.set(null);
        this.sessionPausedMessage.set(null);
        this.sessionEndedMessage.set(MSG_CONVERSATION_CLOSED);
        return;
      }
      if (err instanceof HttpErrorResponse && err.status === 409) {
        this.sessionPausedMessage.set(MSG_INACTIVITY_PAUSE);
        this.error.set(
          'No se pudo reanudar en este momento. Intenta de nuevo o inicia una nueva conversación.',
        );
        return;
      }
      this.sessionPausedMessage.set(MSG_INACTIVITY_PAUSE);
      this.error.set(connectErrorMessage(err, 'Error al reanudar.'));
    } finally {
      this.connecting.set(false);
    }
  }

  async onSwitchToText() {
    this.dismissContactCard();
    const bot = this.bot();
    if (!bot?.enableChat) {
      this.toggleChat();
      return;
    }
    await this.switchToTextMode();
  }

  async onSwitchToVoice() {
    await this.switchToVoiceMode();
  }

  async onSendText(text: string) {
    try {
      await this.stream.sendTurn(text);
      this.error.set(null);
    } catch (err: unknown) {
      // The server closes a text conversation that sat idle for a while. The person only
      // wants to keep talking: open a new one behind the scenes and send the message again.
      if (isConversationClosedError(err) && (await this.startFreshTextConversation())) {
        try {
          await this.stream.sendTurn(text);
          this.error.set(null);
        } catch (retryErr: unknown) {
          this.error.set(mapSendTurnError(retryErr));
        }
        return;
      }
      this.error.set(mapSendTurnError(err));
    }
  }

  /**
   * Opens a new text conversation for the same bot after the server closed the old one,
   * keeping what is already on screen. Returns false when it could not, and the person
   * then sees the usual «conversación cerrada» message.
   */
  private async startFreshTextConversation(): Promise<boolean> {
    const bot = this.bot();
    if (!bot) return false;
    try {
      const isPublic = this.usePublicSession();
      const chat = isPublic
        ? await this.startPublicSession(bot, 'text')
        : await this.api.invoke(createChat, { id: bot.id, mode: 'text' });
      this.conversationId.set(chat.conversationId);
      this.sessionEndedExplicitly.set(false);
      await this.stream.connect(bot.id, chat.conversationId, {
        withEvents: this.adminMode(),
        publicAccessId: isPublic ? bot.publicId : undefined,
        keepItems: true,
      });
      return true;
    } catch {
      return false;
    }
  }

  onStopTurn() {
    void this.stream.cancelActiveTurn();
  }

  private isTextInteraction(): boolean {
    return this.interactionMode() === 'text';
  }

  private canFallbackToText(): boolean {
    const bot = this.bot();
    return !!bot?.enableChat && !!bot?.enableVoice;
  }

  private async fallbackToTextMode(
    reason: AudioFallbackReason,
    message?: string,
  ): Promise<void> {
    writeStoredInteractionMode(this.slug(), 'text');
    this.interactionMode.set('text');
    this.textSessionActive.set(true);
    this.audioFallbackReason.set(reason);
    this.lk.disconnect();
    this.lk.clearSessionEnd();
    this.lk.clearSessionPaused();
    this.lk.clearPipelineError();
    this.connecting.set(false);
    if (message) this.error.set(message);
    else this.error.set(null);
    await this.moveConversationToText();

    const bot = this.bot();
    const convId = this.conversationId();
    if (bot && convId !== null && this.stream.state() === 'disconnected') {
      await this.stream.connect(bot.id, convId, {
        withEvents: this.adminMode(),
      });
    }
  }

  private async switchToTextMode(): Promise<void> {
    if (this.stream.turnInFlight()) {
      await this.stream.cancelActiveTurn();
    }
    writeStoredInteractionMode(this.slug(), 'text');
    this.interactionMode.set('text');
    this.textSessionActive.set(true);
    this.audioFallbackReason.set(null);
    this.lk.disconnect();
    this.lk.clearSessionEnd();
    this.lk.clearSessionPaused();
    this.lk.clearPipelineError();
    this.chatOpen.set(false);
    await this.moveConversationToText();
  }

  /**
   * The call is over and the person goes on by text. Tell Bot Factory: the conversation moves to the text
   * channel (otherwise it keeps the voice rule and is closed after about 30 s without messages, and the
   * bot loses what was said) and the call's room is retired, which frees the agent and its
   * speech-recognition slot at once instead of minutes later. Best effort: the chat works either way.
   */
  private async moveConversationToText(): Promise<void> {
    const bot = this.bot();
    const convId = this.conversationId();
    if (!bot || convId === null) return;
    try {
      await this.resumeConversationForMode(bot, convId, 'text');
    } catch {
      // Nothing to show: the room times out on its own and the next message reopens the conversation if needed.
    }
  }

  private async switchToVoiceMode(): Promise<void> {
    const bot = this.bot();
    const convId = this.conversationId();
    if (!bot?.enableVoice || convId === null) return;

    if (this.stream.turnInFlight()) {
      await this.stream.cancelActiveTurn();
    }

    writeStoredInteractionMode(this.slug(), 'voice');
    this.interactionMode.set('voice');
    this.textSessionActive.set(false);
    this.audioFallbackReason.set(null);
    this.connecting.set(true);
    this.error.set(null);

    try {
      const chat = await this.resumeConversationForMode(bot, convId, 'voice');
      if (!chat.token || !chat.url) {
        throw new Error('No se recibió la sesión de voz.');
      }
      await this.lk.connect(chat.token, chat.url);
      await this.lk.enableMicrophone();
    } catch (err: unknown) {
      this.lk.disconnect();
      this.textSessionActive.set(true);
      this.interactionMode.set('text');
      this.audioFallbackReason.set('connect_failed');
      const message =
        err instanceof Error ? err.message : 'No se pudo activar la voz.';
      this.error.set(message);
      // The call never started: its room (and agent) must not wait out LiveKit's timeout.
      await this.moveConversationToText();
    } finally {
      this.connecting.set(false);
    }
  }

  async onPause() {
    this.dismissContactCard();
    await this.terminateSession({
      mode: 'pause',
      message: 'Pausaste la conversación. Puedes reanudarla cuando quieras.',
    });
  }

  async onDisconnect() {
    this.dismissContactCard();
    await this.terminateSession({
      mode: 'userInitiated',
      closeReason: 'user_ended',
    });
  }

  private async terminateSession(opts: {
    mode: TerminateMode;
    message?: string;
    closeReason?: ClientCloseReason;
    skipApiClose?: boolean;
  }): Promise<void> {
    if (this.sessionTeardownInProgress) return;
    this.sessionTeardownInProgress = true;

    try {
      const shouldCloseApi =
        opts.mode === 'userInitiated' ||
        (opts.mode === 'terminal' && !opts.skipApiClose);
      if (shouldCloseApi && opts.closeReason) {
        await this.endSession(opts.closeReason);
      }

      this.thinkingSound.reset();
      this.lk.disconnect();
      this.lk.clearSessionEnd();
      this.lk.clearSessionPaused();
      this.lk.clearPipelineError();
      if (opts.mode !== 'pause') {
        void this.stream.cancelActiveTurn();
        void this.stream.disconnect();
        this.textSessionActive.set(false);
      }
      if (opts.mode !== 'pause') this.chatOpen.set(false);
      this.connecting.set(false);

      if (opts.mode === 'pause') {
        this.sessionEndedMessage.set(null);
        this.sessionPausedMessage.set(opts.message ?? MSG_INACTIVITY_PAUSE);
      } else if (opts.mode === 'userInitiated') {
        this.conversationId.set(null);
        this.sessionEndedMessage.set(null);
        this.sessionPausedMessage.set(null);
        this.error.set(null);
      } else {
        this.conversationId.set(null);
        this.sessionPausedMessage.set(null);
        this.sessionEndedMessage.set(opts.message ?? '');
      }
    } finally {
      this.sessionTeardownInProgress = false;
    }
  }

  private closeSessionIfNeeded(reason: ClientCloseReason): void {
    if (this.sessionEndedExplicitly()) return;
    if (this.sessionPausedMessage()) return;

    const bot = this.bot();
    const conversationId = this.conversationId();
    if (!bot || conversationId === null) return;

    this.sessionEndedExplicitly.set(true);
    if (this.usePublicSession()) {
      const token = this.publicSession.token();
      if (token) {
        this.conversationLifecycle.closePublicOnUnload(bot.publicId, conversationId, reason, token);
      }
      return;
    }
    this.conversationLifecycle.closeOnUnload(bot.id, conversationId, reason);
  }

  private async endSession(reason: ClientCloseReason): Promise<void> {
    if (this.sessionEndedExplicitly()) return;

    const bot = this.bot();
    const conversationId = this.conversationId();
    if (!bot || conversationId === null) return;

    this.sessionEndedExplicitly.set(true);
    try {
      await this.closeConversationForMode(bot, conversationId, reason);
    } catch {
      // Still disconnect LiveKit if the close API fails.
    }
  }

  async onToggleMic() {
    this.dismissContactCard();
    try {
      await this.lk.toggleMicrophone();
    } catch (err: unknown) {
      const message =
        err instanceof Error ? err.message : 'No se pudo cambiar el micrófono.';
      this.error.set(message);
    }
  }

  toggleChat() {
    this.chatOpen.update((v) => !v);
  }

  toggleAdminMode() {
    this.setAdminMode(!this.adminMode());
  }

  setAdminMode(on: boolean) {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { admin: on ? '1' : null },
      queryParamsHandling: 'merge',
    });
  }
}
