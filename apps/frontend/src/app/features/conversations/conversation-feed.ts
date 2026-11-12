import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  input,
  signal,
  viewChild,
} from '@angular/core';
import {
  itemKey,
  type ConversationItem,
  type ToolDisplayItem,
  type TurnMetricsData,
  type TurnPipelineStage,
  type TurnSegment,
  type TokenCounts,
  type TurnSummaryData,
  type TurnTokens,
  formatTokenCount,
  tokensByAnswer,
} from './conversation-item';
import { TooltipFlipDirective } from './tooltip-flip.directive';
import { linkifySegments } from '../chat/contact-extract';
import { AssistantMessageComponent } from '../chat/components/assistant-message';

// Human-readable description per pipeline stage, shown in the collapsed-row
// info tooltip so each step is explained without expanding the waterfall.
const PIPELINE_STAGE_DESC: Record<string, string> = {
  eou: 'detección de fin de turno',
  stt: 'transcripción de voz a texto',
  llm: 'respuesta del backend (Gemini + herramientas)',
  tts: 'síntesis de texto a voz',
};

const STAGE_DOT_CLASS: Record<string, string> = {
  stt: 'bg-cyan-400',
  llm: 'bg-violet-400',
  tts: 'bg-amber-400',
  retrieval: 'bg-emerald-400',
  kb_index: 'bg-emerald-400',
  lifecycle: 'bg-slate-400',
  tool: 'bg-orange-400',
};

const ERROR_EVENTS = new Set([
  'stt_failed',
  'tts_failed',
  'llm_error',
  'agent_error',
  'kb_index_failed',
  'network_degraded',
]);

@Component({
  selector: 'bf-conversation-feed',
  standalone: true,
  imports: [TooltipFlipDirective, AssistantMessageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
  '[class]': 'hostClasses()',
  },
  template: `
    <div
      #scrollContainer
      role="log"
      aria-live="polite"
      [attr.aria-label]="ariaLabel()"
      [class]="containerClasses()"
    >
      @if (loading()) {
        <div class="flex items-center justify-center py-16">
          <p class="text-sm" [class.text-neutral-400]="!dark()" [class.text-neutral-500]="dark()">
            Cargando mensajes…
          </p>
        </div>
      } @else if (visibleItems().length === 0) {
        <p class="py-8 text-center text-sm" [class.text-neutral-400]="!dark()" [class.text-neutral-500]="dark()">
          No hay mensajes en esta conversación.
        </p>
      } @else {
        @for (item of visibleItems(); track key(item)) {
          @if (item.kind === 'message') {
            <div
              class="flex"
              [class.justify-end]="isUser(item)"
              [class.justify-start]="!isUser(item)"
            >
              <div
                class="max-w-[80%] rounded-lg px-3 py-2 text-sm"
                [class.bg-blue-600]="isUser(item) && !branded()"
                [class.text-white]="isUser(item) && !branded()"
                [class.bg-neutral-100]="!isUser(item) && !dark()"
                [class.text-neutral-900]="!isUser(item) && !dark()"
                [class.border]="!isUser(item) && !dark()"
                [class.border-neutral-200]="!isUser(item) && !dark()"
                [class.bg-white]="!isUser(item) && dark() && !branded()"
                [class.bg-opacity-5]="!isUser(item) && dark() && !branded()"
                [class.text-neutral-100]="!isUser(item) && dark() && !branded()"
                [style.background]="brandedBubbleBg(item)"
                [style.color]="brandedBubbleFg(item)"
              >
                @if (!isUser(item)) {
                  <!-- The answer's closing contact block comes as buttons from the server (ContactLine):
                       the text goes without it, so the contacts are not shown twice. -->
                  <bf-assistant-message
                    [content]="item.data.displayContent ?? item.data.content"
                    [dark]="dark()"
                    [branded]="branded()"
                  />
                  @if (item.data.contacts?.length) {
                    <div class="mt-2.5 flex flex-wrap gap-1.5" role="group" aria-label="Datos de contacto">
                      @for (c of item.data.contacts; track c.href) {
                        <a
                          [href]="c.href"
                          [attr.target]="c.kind === 'url' ? '_blank' : null"
                          [attr.rel]="c.kind === 'url' ? 'noopener noreferrer' : null"
                          [attr.title]="c.detail ? c.detail + ' · ' + c.href : c.href"
                          class="inline-flex max-w-full items-center gap-1.5 rounded-full border px-2.5 py-1 text-xs font-medium no-underline transition-opacity hover:opacity-80"
                          [class.border-neutral-300]="!dark() && !branded()"
                          [class.bg-white]="!dark() && !branded()"
                          [class.text-blue-700]="!dark() && !branded()"
                          [class.border-white]="dark() && !branded()"
                          [class.border-opacity-20]="dark() && !branded()"
                          [class.text-sky-300]="dark() && !branded()"
                          [style.color]="branded() ? 'var(--bf-accent)' : null"
                          [style.border-color]="branded() ? 'color-mix(in srgb, var(--bf-accent) 45%, transparent)' : null"
                          [style.background]="branded() ? 'color-mix(in srgb, var(--bf-accent) 10%, transparent)' : null"
                        >
                          @switch (c.kind) {
                            @case ('phone') {
                              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="h-3.5 w-3.5 shrink-0" aria-hidden="true">
                                <path fill-rule="evenodd" d="M2 3.5A1.5 1.5 0 0 1 3.5 2h1.148a1.5 1.5 0 0 1 1.465 1.175l.513 2.31a1.5 1.5 0 0 1-1.02 1.762l-.993.331a11.05 11.05 0 0 0 5.81 5.81l.33-.993a1.5 1.5 0 0 1 1.763-1.02l2.31.513A1.5 1.5 0 0 1 18 13.352V14.5a1.5 1.5 0 0 1-1.5 1.5H15C8.096 16 2.5 10.404 2.5 3.5Z" clip-rule="evenodd" />
                              </svg>
                            }
                            @case ('email') {
                              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="h-3.5 w-3.5 shrink-0" aria-hidden="true">
                                <path d="M3 4a2 2 0 0 0-2 2v1.161l8.441 4.221a1.25 1.25 0 0 0 1.118 0L19 7.162V6a2 2 0 0 0-2-2H3Z" />
                                <path d="m19 8.839-7.77 3.885a2.75 2.75 0 0 1-2.46 0L1 8.839V14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V8.839Z" />
                              </svg>
                            }
                            @default {
                              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="h-3.5 w-3.5 shrink-0" aria-hidden="true">
                                <path fill-rule="evenodd" d="M4.25 5.5a.75.75 0 0 0-.75.75v8.5c0 .414.336.75.75.75h8.5a.75.75 0 0 0 .75-.75v-4a.75.75 0 0 1 1.5 0v4A2.25 2.25 0 0 1 12.75 17h-8.5A2.25 2.25 0 0 1 2 14.75v-8.5A2.25 2.25 0 0 1 4.25 4h5a.75.75 0 0 1 0 1.5h-5Zm7.25-.75a.75.75 0 0 1 .75-.75h3.5a.75.75 0 0 1 .75.75v3.5a.75.75 0 0 1-1.5 0V6.56l-5.22 5.22a.75.75 0 1 1-1.06-1.06l5.22-5.22h-1.69a.75.75 0 0 1-.75-.75Z" clip-rule="evenodd" />
                              </svg>
                            }
                          }
                          <span class="truncate">
                            @if (c.detail) {
                              <span class="opacity-70">{{ c.detail }}: </span>
                            }
                            {{ c.label }}
                          </span>
                        </a>
                      }
                    </div>
                  }
                } @else {
                <p class="whitespace-pre-wrap break-words">
                  @for (seg of linkifySegments(item.data.content); track $index) {
                    @if (seg.type === 'url') {
                      <a
                        [href]="seg.href"
                        target="_blank"
                        rel="noopener noreferrer"
                        class="underline underline-offset-2"
                        [class.text-blue-200]="isUser(item) && !branded()"
                        [class.text-blue-700]="!isUser(item) && !dark() && !branded()"
                        [class.text-sky-300]="!isUser(item) && dark() && !branded()"
                        [style.color]="branded() ? (isUser(item) ? 'var(--bf-cta-fg)' : 'var(--bf-accent)') : null"
                        >{{ seg.value }}</a
                      >
                    } @else if (seg.type === 'email' || seg.type === 'phone') {
                      <a
                        [href]="seg.href"
                        class="underline underline-offset-2"
                        [class.text-blue-200]="isUser(item) && !branded()"
                        [class.text-blue-700]="!isUser(item) && !dark() && !branded()"
                        [class.text-sky-300]="!isUser(item) && dark() && !branded()"
                        [style.color]="branded() ? (isUser(item) ? 'var(--bf-cta-fg)' : 'var(--bf-accent)') : null"
                        >{{ seg.value }}</a
                      >
                    } @else {
                      {{ seg.value }}
                    }
                  }
                </p>
                }
                <p
                  class="mt-1 text-xs"
                  [class.text-blue-200]="isUser(item) && !branded()"
                  [class.text-neutral-400]="!isUser(item) || branded()"
                  [style.color]="branded() && isUser(item) ? 'color-mix(in srgb, var(--bf-cta-fg) 70%, transparent)' : null"
                >
                  {{ formatTime(item.occurredAt) }}
                  @if (item.data.latencyMs) {
                    · {{ item.data.latencyMs }}ms
                  }
                  @if (item.data.provider) {
                    · {{ item.data.provider }}
                  }
                  @if (answerTokens(item); as tk) {
                    · <span [title]="answerTokensTitle(tk)">{{ formatTokenCount(tk.totalTokens) }} tokens</span>
                  }
                </p>
              </div>
            </div>
          } @else if (item.kind === 'tool-invocation') {
            <div class="flex items-center gap-2 px-2 py-1 text-xs"
                 [class.text-neutral-400]="!dark()"
                 [class.text-neutral-500]="dark()">
              <span class="inline-block h-2 w-2 shrink-0 rounded-full bg-orange-400"></span>
              <span class="font-mono italic">tool_call · {{ item.data.name }}</span>
              @if (item.data.latencyMs != null) {
                <span class="font-mono">· {{ item.data.latencyMs }}ms</span>
              }
              @if (item.data.summary) {
                <div class="group/info relative">
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor"
                       class="h-3.5 w-3.5 cursor-default"
                       [class.text-neutral-400]="!dark()"
                       [class.text-neutral-500]="dark()">
                    <path fill-rule="evenodd" d="M2.25 12c0-5.385 4.365-9.75 9.75-9.75S21.75 6.615 21.75 12s-4.365 9.75-9.75 9.75S2.25 17.385 2.25 12zm8.706-1.442c1.146-.573 2.437.463 2.126 1.706l-.709 2.836.042-.02a.75.75 0 01.67 1.34l-.04.022c-1.147.573-2.438-.463-2.127-1.706l.71-2.836-.042.02a.75.75 0 11-.671-1.34l.041-.022zM12 9a.75.75 0 100-1.5.75.75 0 000 1.5z" clip-rule="evenodd" />
                  </svg>
                  <div bfTooltipFlip class="pointer-events-none absolute bottom-full left-0 z-50 mb-1.5 w-max max-w-xs rounded bg-neutral-800 px-2.5 py-1.5 text-[11px] text-white opacity-0 transition-opacity group-hover/info:opacity-100 whitespace-pre-wrap break-all">
                    {{ item.data.summary }}
                  </div>
                </div>
              }
              <span class="ml-auto font-mono">{{ formatTime(item.occurredAt) }}</span>
            </div>
            @if (item.data.items.length > 0) {
              <div class="flex flex-wrap gap-1.5 pb-1 pl-6">
                @for (toolItem of item.data.items; track toolItem.id) {
                  <button
                    type="button"
                    (click)="selectedDisplayItem.set(toolItem)"
                    [class]="chunkPillClasses()"
                    [title]="toolItem.label"
                  >
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="h-3 w-3 opacity-70">
                      <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6Zm0 2 4 4h-4V4Z" />
                    </svg>
                    <span class="max-w-[180px] truncate">{{ toolItem.label }}</span>
                  </button>
                }
              </div>
            }
          } @else if (item.kind === 'turn-metrics') {
            <div class="flex items-center gap-2 px-2 py-1 text-xs"
                 [class.text-neutral-400]="!dark()"
                 [class.text-neutral-500]="dark()">
              <span class="inline-block h-2 w-2 shrink-0 rounded-full bg-violet-400"></span>
              <span class="font-mono italic">turn_completed</span>
              <div class="group/info relative">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor"
                     class="h-3.5 w-3.5 cursor-default"
                     [class.text-neutral-400]="!dark()"
                     [class.text-neutral-500]="dark()">
                  <path fill-rule="evenodd" d="M2.25 12c0-5.385 4.365-9.75 9.75-9.75S21.75 6.615 21.75 12s-4.365 9.75-9.75 9.75S2.25 17.385 2.25 12zm8.706-1.442c1.146-.573 2.437.463 2.126 1.706l-.709 2.836.042-.02a.75.75 0 01.67 1.34l-.04.022c-1.147.573-2.438-.463-2.127-1.706l.71-2.836-.042.02a.75.75 0 11-.671-1.34l.041-.022zM12 9a.75.75 0 100-1.5.75.75 0 000 1.5z" clip-rule="evenodd" />
                </svg>
                <div bfTooltipFlip class="pointer-events-none absolute bottom-full right-0 z-50 mb-1.5 w-max max-w-xs rounded bg-neutral-800 px-2.5 py-1.5 text-[11px] text-white opacity-0 transition-opacity group-hover/info:opacity-100 whitespace-pre-wrap">{{ pipelineExplain(item.data) }}</div>
              </div>
              <button type="button" (click)="toggleExpand(item)"
                      class="ml-auto flex items-center"
                      [attr.aria-label]="isExpanded(item) ? 'Colapsar detalle' : 'Expandir detalle'">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                     stroke-width="2" class="h-3.5 w-3.5 transition-transform" [class.rotate-90]="isExpanded(item)">
                  <path stroke-linecap="round" stroke-linejoin="round" d="M9 5l7 7-7 7" />
                </svg>
              </button>
              <span class="font-mono">{{ formatTime(item.occurredAt) }}</span>
            </div>
            @if (isExpanded(item)) {
              <div [class]="turnCardClasses()">
                <div [class]="turnTrackClasses()">
                  @for (st of item.data.stages; track st.key) {
                    <div
                      class="h-full"
                      [class]="pipelineBar(st.key)"
                      [style.width.%]="pipelinePct(st, item.data.stages)"
                      [title]="st.label + ' ' + st.ms + 'ms'"
                    ></div>
                  }
                </div>
                <div class="mt-2 flex flex-col gap-1 font-mono">
                  @for (st of item.data.stages; track st.key) {
                    <span class="flex w-full items-center gap-1.5">
                      <span class="inline-block h-2 w-2 shrink-0 rounded-full" [class]="pipelineBar(st.key)"></span>
                      <span class="uppercase">{{ st.label }}</span>
                      <span class="opacity-70">{{ stageDesc(st.key) }}</span>
                      <span class="ml-auto">{{ st.ms }}ms</span>
                    </span>
                  }
                </div>
              </div>
            }
          } @else if (item.kind === 'turn-summary') {
            <div class="flex items-center gap-2 px-2 py-1 text-xs"
                 [class.text-neutral-400]="!dark()"
                 [class.text-neutral-500]="dark()">
              <span class="inline-block h-2 w-2 shrink-0 rounded-full bg-violet-400"></span>
              <span class="font-mono italic">backend_completed</span>
              <span class="rounded px-1.5 py-0.5 text-[10px] font-medium" [class]="outcomeBadge(item.data.outcome)">
                {{ item.data.outcome ?? 'n/a' }}
              </span>
              @if (item.data.tokens; as tk) {
                <span class="min-w-0 truncate font-mono">· {{ tokensLabel(tk) }}</span>
              }
              <div class="group/info relative">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor"
                     class="h-3.5 w-3.5 cursor-default"
                     [class.text-neutral-400]="!dark()"
                     [class.text-neutral-500]="dark()">
                  <path fill-rule="evenodd" d="M2.25 12c0-5.385 4.365-9.75 9.75-9.75S21.75 6.615 21.75 12s-4.365 9.75-9.75 9.75S2.25 17.385 2.25 12zm8.706-1.442c1.146-.573 2.437.463 2.126 1.706l-.709 2.836.042-.02a.75.75 0 01.67 1.34l-.04.022c-1.147.573-2.438-.463-2.127-1.706l.71-2.836-.042.02a.75.75 0 11-.671-1.34l.041-.022zM12 9a.75.75 0 100-1.5.75.75 0 000 1.5z" clip-rule="evenodd" />
                </svg>
                <div bfTooltipFlip class="pointer-events-none absolute bottom-full right-0 z-50 mb-1.5 w-max max-w-xs rounded bg-neutral-800 px-2.5 py-1.5 text-[11px] text-white opacity-0 transition-opacity group-hover/info:opacity-100 whitespace-pre-wrap">{{ summaryExplain(item.data) }}</div>
              </div>
              <button type="button" (click)="toggleExpand(item)"
                      class="ml-auto flex items-center"
                      [attr.aria-label]="isExpanded(item) ? 'Colapsar detalle' : 'Expandir detalle'">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                     stroke-width="2" class="h-3.5 w-3.5 transition-transform" [class.rotate-90]="isExpanded(item)">
                  <path stroke-linecap="round" stroke-linejoin="round" d="M9 5l7 7-7 7" />
                </svg>
              </button>
              <span class="font-mono">{{ formatTime(item.occurredAt) }}</span>
            </div>
            @if (isExpanded(item)) {
              <div [class]="turnCardClasses()">
                @if (item.data.segments.length > 0) {
                  <div [class]="turnTrackClasses()">
                    @for (seg of item.data.segments; track $index) {
                      <div
                        class="h-full"
                        [class]="segBar(seg.type)"
                        [style.width.%]="segPct(seg, item.data)"
                        [title]="segLabel(seg)"
                      ></div>
                    }
                  </div>
                  <div class="mt-1.5 flex flex-wrap gap-x-3 gap-y-1 font-mono">
                    @for (seg of item.data.segments; track $index) {
                      <span class="inline-flex items-center gap-1">
                        <span class="inline-block h-2 w-2 rounded-full" [class]="segBar(seg.type)"></span>
                        {{ segLabel(seg) }}
                      </span>
                    }
                  </div>
                } @else {
                  <div class="whitespace-pre-wrap font-mono">{{ summaryExplain(item.data) }}</div>
                }
              </div>
            }
          } @else {
            <div class="flex items-center gap-2 px-2 py-1 text-xs"
                 [class.text-neutral-400]="!dark()"
                 [class.text-neutral-500]="dark()">
              <span class="inline-block h-2 w-2 rounded-full" [class]="stageDot(item.data.stage)"></span>
              <span class="font-mono italic" [class.text-rose-500]="isError(item.data.eventName)">
                {{ item.data.eventName }}
              </span>
              @if (item.data.latencyMs != null) {
                <span class="font-mono">· {{ item.data.latencyMs }}ms</span>
              }
              @if (item.data.summary || item.data.errorMessage) {
                <div class="group/info relative">
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor"
                       class="h-3.5 w-3.5 cursor-default"
                       [class.text-neutral-400]="!dark()"
                       [class.text-neutral-500]="dark()">
                    <path fill-rule="evenodd" d="M2.25 12c0-5.385 4.365-9.75 9.75-9.75S21.75 6.615 21.75 12s-4.365 9.75-9.75 9.75S2.25 17.385 2.25 12zm8.706-1.442c1.146-.573 2.437.463 2.126 1.706l-.709 2.836.042-.02a.75.75 0 01.67 1.34l-.04.022c-1.147.573-2.438-.463-2.127-1.706l.71-2.836-.042.02a.75.75 0 11-.671-1.34l.041-.022zM12 9a.75.75 0 100-1.5.75.75 0 000 1.5z" clip-rule="evenodd" />
                  </svg>
                  <div bfTooltipFlip class="pointer-events-none absolute bottom-full left-0 z-50 mb-1.5 w-max max-w-xs rounded bg-neutral-800 px-2.5 py-1.5 text-[11px] text-white opacity-0 transition-opacity group-hover/info:opacity-100 whitespace-pre-wrap break-all">
                    {{ eventExtraInfo(item.data) }}
                  </div>
                </div>
              }
              <span class="ml-auto font-mono">{{ formatTime(item.occurredAt) }}</span>
            </div>
          }
        }
      }
    </div>

    @if (selectedDisplayItem(); as displayItem) {
      <!-- Tool-call body modal: click backdrop or close button to dismiss. -->
      <div
        class="fixed inset-0 z-[60] flex items-center justify-center bg-black/60 backdrop-blur-sm p-4"
        (click)="selectedDisplayItem.set(null)"
      >
        <div
          class="flex max-h-[80vh] w-full max-w-2xl flex-col overflow-hidden rounded-2xl bg-white shadow-2xl"
          (click)="$event.stopPropagation()"
        >
          <header class="flex items-start justify-between gap-4 border-b border-neutral-200 px-5 py-4">
            <div class="min-w-0">
              <h3 class="truncate text-sm font-semibold text-neutral-900">
                {{ displayItem.label }}
              </h3>
            </div>
            <button
              type="button"
              class="flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-neutral-400 transition-colors hover:bg-neutral-100 hover:text-neutral-700"
              aria-label="Cerrar"
              (click)="selectedDisplayItem.set(null)"
            >
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="h-4 w-4">
                <path d="M6.2 4.8a1 1 0 0 1 1.4 0L12 9.2l4.4-4.4a1 1 0 1 1 1.4 1.4L13.4 10.6l4.4 4.4a1 1 0 1 1-1.4 1.4L12 12l-4.4 4.4a1 1 0 1 1-1.4-1.4l4.4-4.4-4.4-4.4a1 1 0 0 1 0-1.4Z" />
              </svg>
            </button>
          </header>
          <div class="flex-1 overflow-y-auto px-5 py-4">
            <p class="whitespace-pre-wrap text-sm leading-relaxed text-neutral-800">{{ displayItem.body }}</p>
          </div>
        </div>
      </div>
    }
  `,
})
export class ConversationFeedComponent {
  readonly items = input<ConversationItem[]>([]);
  readonly loading = input<boolean>(false);
  readonly showEvents = input<boolean>(false);
  readonly dark = input<boolean>(false);
  /** When true (public chat), message bubbles use --bf-* CSS variables from an ancestor. */
  readonly branded = input<boolean>(false);
  readonly autoScroll = input<boolean>(true);
  /** When false, content flows in a parent scroll container (e.g. edge-aligned chat scroll). */
  readonly scrollable = input<boolean>(true);
  readonly externalScrollRoot = input<HTMLElement | null>(null);
  readonly ariaLabel = input<string>('Conversación');

  readonly scrollContainer = viewChild<ElementRef<HTMLDivElement>>('scrollContainer');

  readonly hostClasses = computed(() =>
    this.scrollable()
      ? 'flex-1 min-h-0 flex flex-col overflow-hidden'
      : 'flex flex-col',
  );

  readonly selectedDisplayItem = signal<ToolDisplayItem | null>(null);

  // What each answer cost. The summaries it comes from only exist when the viewer may read
  // telemetry, so the public chat never shows it; showEvents is the second guard.
  private readonly answerTokensById = computed(() =>
    this.showEvents() ? tokensByAnswer(this.items()) : new Map<number, TurnTokens>(),
  );

  readonly visibleItems = computed(() => {
    const all = this.items();
    if (this.showEvents()) return all;
    return all.filter((i) => i.kind === 'message');
  });

  readonly containerClasses = computed(() => {
    const spacing = this.dark() ? 'space-y-2' : 'space-y-3';
    if (!this.scrollable()) return spacing;
    const padding = 'px-5 py-4';
    const scroll = this.dark() ? 'bf-feed-scroll overflow-y-scroll' : 'overflow-y-auto';
    return `flex-1 min-h-0 ${scroll} ${padding} ${spacing}`;
  });

  readonly turnCardClasses = computed(() =>
    this.dark()
      ? 'rounded-md border border-white/10 bg-white/5 px-2.5 py-2 text-xs text-neutral-400'
      : 'rounded-md border border-neutral-200 bg-neutral-50 px-2.5 py-2 text-xs text-neutral-500',
  );

  readonly turnTrackClasses = computed(() =>
    this.dark()
      ? 'mt-1.5 flex h-2 w-full overflow-hidden rounded-full bg-white/10'
      : 'mt-1.5 flex h-2 w-full overflow-hidden rounded-full bg-neutral-200',
  );

  readonly chunkPillClasses = computed(() => {
    const base =
      'group flex items-center gap-1 rounded-full border px-2 py-0.5 text-[11px] font-medium transition-colors duration-150 ease-out active:scale-[0.97]';
    return this.dark()
      ? `${base} border-white/10 bg-white/5 text-neutral-200 hover:bg-white/10`
      : `${base} border-neutral-200 bg-neutral-50 text-neutral-700 hover:bg-neutral-100`;
  });

  constructor() {
    effect(() => {
      // Re-run when items change or the external scroll root is attached.
      this.visibleItems();
      this.scrollable();
      this.externalScrollRoot();
      if (!this.autoScroll()) return;
      const el = this.scrollable()
        ? this.scrollContainer()?.nativeElement
        : this.externalScrollRoot();
      if (!el) return;
      // Defer until layout includes the new nodes (especially external scroll parents).
      requestAnimationFrame(() => {
        const nearBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 80;
        if (nearBottom) el.scrollTop = el.scrollHeight;
      });
    });
  }

  key = itemKey;

  linkifySegments = linkifySegments;

  isUser(item: ConversationItem & { kind: 'message' }): boolean {
    return item.data.role === 'User';
  }

  brandedBubbleBg(item: ConversationItem & { kind: 'message' }): string | null {
    if (!this.branded()) return null;
    return this.isUser(item) ? 'var(--bf-accent)' : 'var(--bf-bot-bubble)';
  }

  brandedBubbleFg(item: ConversationItem & { kind: 'message' }): string | null {
    if (!this.branded()) return null;
    return this.isUser(item) ? 'var(--bf-cta-fg)' : 'var(--bf-fg)';
  }

  isError(eventName: string): boolean {
    return ERROR_EVENTS.has(eventName);
  }

  eventExtraInfo(data: {
    summary?: string | null;
    errorMessage?: string | null;
    payload?: string | null;
  }): string {
    const parts: string[] = [];
    if (data.summary) parts.push(data.summary);
    const code = this.errorCodeFromPayload(data.payload);
    if (code) parts.push(`code: ${code}`);
    if (data.errorMessage) parts.push(data.errorMessage);
    return parts.join('\n');
  }

  private errorCodeFromPayload(payload: string | null | undefined): string | null {
    if (!payload) return null;
    try {
      const parsed = JSON.parse(payload) as { errorCode?: string };
      return parsed.errorCode ?? null;
    } catch {
      return null;
    }
  }


  stageDot(stage: string | null | undefined): string {
    return stage ? (STAGE_DOT_CLASS[stage] ?? 'bg-neutral-400') : 'bg-neutral-400';
  }

  segBar(type: TurnSegment['type']): string {
    return type === 'tool' ? 'bg-orange-400' : 'bg-violet-400';
  }

  // Pipeline stage colors mirror the stage dots so EOU/STT/LLM/TTS read
  // consistently across the feed.
  pipelineBar(key: string): string {
    return STAGE_DOT_CLASS[key === 'eou' ? 'lifecycle' : key] ?? 'bg-neutral-400';
  }

  pipelineTotal(stages: TurnPipelineStage[]): number {
    return stages.reduce((sum, s) => sum + s.ms, 0);
  }

  // Pipeline stages run sequentially, so the bar is scaled against their sum.
  pipelinePct(stage: TurnPipelineStage, stages: TurnPipelineStage[]): number {
    const total = this.pipelineTotal(stages);
    return total > 0 ? (stage.ms / total) * 100 : 0;
  }

  // Per-item expand state for the turn summary/metrics waterfalls. Collapsed by
  // default (compact row); the chevron toggles the inline full breakdown.
  private readonly expandedKeys = signal(new Set<string>());

  isExpanded(item: ConversationItem): boolean {
    return this.expandedKeys().has(itemKey(item));
  }

  toggleExpand(item: ConversationItem): void {
    const k = itemKey(item);
    this.expandedKeys.update((set) => {
      const next = new Set(set);
      if (next.has(k)) next.delete(k);
      else next.add(k);
      return next;
    });
  }

  stageDesc(key: string): string {
    return PIPELINE_STAGE_DESC[key] ?? '';
  }

  pipelineExplain(data: TurnMetricsData): string {
    const lines = data.stages.map(
      (s) => `${s.label.toUpperCase()} · ${s.ms}ms — ${PIPELINE_STAGE_DESC[s.label] ?? ''}`,
    );
    if (data.llmTtftMs != null) lines.push(`LLM primer token: ${data.llmTtftMs}ms`);
    if (data.ttsTtfbMs != null) lines.push(`TTS primer audio: ${data.ttsTtfbMs}ms`);
    return lines.join('\n');
  }

  summaryExplain(data: TurnSummaryData): string {
    const lines: string[] = [];
    if (data.totalMs != null) lines.push(`Duración total: ${data.totalMs}ms`);
    if (data.ttftMs != null) lines.push(`Primer token: ${data.ttftMs}ms`);
    if (data.outcome) lines.push(`Resultado: ${data.outcome}`);
    if (data.toolCount) lines.push(`Herramientas usadas: ${data.toolCount}`);
    if (data.tokens) {
      lines.push(`Tokens: ${this.tokensLabel(data.tokens)}`);
      if (data.tokens.unreportedCalls > 0) {
        lines.push(
          `Gemini no informó los tokens de ${data.tokens.unreportedCalls} de ${data.tokens.calls} llamadas`,
        );
      }
    }
    for (const seg of data.segments) {
      const what =
        seg.type === 'tool' ? `herramienta ${seg.name ?? ''}`.trim() : 'inferencia Gemini';
      const tokens = seg.tokens ? ` · ${this.tokensLabel(seg.tokens)}` : '';
      lines.push(`${seg.ms}ms — ${what}${tokens}`);
    }
    return lines.join('\n');
  }

  /**
   * Tokens of one Gemini call or of a whole turn, e.g.
   * `entrada 1200 (83 % caché) · salida 20`. The cache share is of the prompt
   * (`promptTokens` already includes the cached part). Thoughts only when present.
   */
  tokensLabel(t: TokenCounts): string {
    const pct = t.promptTokens > 0 ? Math.round((100 * t.cachedTokens) / t.promptTokens) : 0;
    const parts = [
      `entrada ${t.promptTokens} (${pct} % caché)`,
      `salida ${t.outputTokens}`,
    ];
    if (t.thoughtsTokens > 0) parts.push(`razonamiento ${t.thoughtsTokens}`);
    return parts.join(' · ');
  }

  segLabel(seg: TurnSegment): string {
    const name = seg.type === 'tool' ? (seg.name ?? 'tool') : 'gemini';
    const tokens = seg.tokens ? ` · ${this.tokensLabel(seg.tokens)}` : '';
    return `${name} ${seg.ms}ms${tokens}`;
  }

  // Width is scaled against the turn total so the bar's empty tail reflects
  // overhead not captured by any segment (network, glue). Falls back to the
  // segment sum when totalMs is missing.
  segPct(seg: TurnSegment, data: TurnSummaryData): number {
    const total =
      data.totalMs && data.totalMs > 0
        ? data.totalMs
        : data.segments.reduce((sum, s) => sum + s.ms, 0);
    return total > 0 ? (seg.ms / total) * 100 : 0;
  }

  outcomeBadge(outcome: string | null): string {
    switch (outcome) {
      case 'ok':
        return 'bg-emerald-500/15 text-emerald-600';
      case 'fallback':
        return 'bg-rose-500/15 text-rose-600';
      case 'partial':
      case 'grounded-only':
        return 'bg-amber-500/15 text-amber-600';
      default:
        return 'bg-neutral-500/15 text-neutral-500';
    }
  }

  readonly formatTokenCount = formatTokenCount;

  /** The tokens the turn behind this answer spent; null for anything else. */
  answerTokens(item: ConversationItem): TurnTokens | null {
    return item.kind === 'message' ? (this.answerTokensById().get(item.data.id) ?? null) : null;
  }

  answerTokensTitle(t: TurnTokens): string {
    return this.tokensLabel(t);
  }

  formatTime(iso: string): string {
    // Append Z if no timezone indicator so the value is always treated as UTC.
    const utc = /Z|[+-]\d{2}:?\d{2}$/.test(iso) ? iso : `${iso}Z`;
    return new Date(utc).toLocaleTimeString('es', {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
  }

}
