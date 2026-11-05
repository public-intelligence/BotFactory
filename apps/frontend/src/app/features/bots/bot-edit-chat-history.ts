import {
  Component,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import type { OnInit, TemplateRef } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import {
  PaginatedTableComponent,
  StatusPillComponent,
  DropdownSearchComponent,
} from '@public-intelligence/ui-core';
import type { PaginatedColumnDef } from '@public-intelligence/ui-core';

import { BotEditStore } from './bot-edit.store';
import { Api } from '../../api/api';
import { getConversations } from '../../api/functions';
import type { GetConversations$Params } from '../../api/fn/conversations/get-conversations';
import type { ConversationResponse } from '../../api/models/conversation-response';
import { ConversationDetailDrawer } from './conversation-detail-drawer';
import { AuthService } from '../../core/services/auth.service';
import { formatTokenCount } from '../conversations/conversation-item';
import {
  formatDateInput,
  matchesDatePreset,
  presetDaysButtonClass,
} from '../../shared/filter-ui';
import { toast } from 'ngx-sonner';

import {
  defaultChatHistoryDateRange,
  mapConversationStatusLabel,
  toUtcEndOfDay,
  toUtcStartOfDay,
  CHAT_HISTORY_STATUS_MAP,
  canSeeTelemetry,
} from './bot-edit-chat-history.helpers';

const STATUS_OPTIONS = [
  { value: '', label: 'Todos los estados' },
  { value: 'Active', label: CHAT_HISTORY_STATUS_MAP['Active'] },
  { value: 'Paused', label: CHAT_HISTORY_STATUS_MAP['Paused'] },
  { value: 'Closed', label: CHAT_HISTORY_STATUS_MAP['Closed'] },
];

type ConversationTableRow = ConversationResponse & {
  _statusLabel: string;
  _date: string;
  /** Tokens spent, formatted; «—» when the conversation has none recorded. */
  _tokens: string;
};

@Component({
  selector: 'bf-bot-edit-chat-history',
  imports: [
    PaginatedTableComponent,
    StatusPillComponent,
    DropdownSearchComponent,
    ConversationDetailDrawer,
  ],
  template: `
    <div class="space-y-6">
      <section>
        <h3 class="mb-2 text-base font-semibold text-neutral-900">
          Historial de Chat
        </h3>
        <p class="mb-4 text-sm text-neutral-500">
          Conversaciones registradas para este bot. Haz clic en "Ver" para revisar los mensajes.
        </p>

        <div class="mb-4 rounded-lg border border-neutral-200 bg-white p-4">
          <div class="flex w-full flex-wrap items-end gap-3">
              <div class="flex flex-col gap-1">
                <label class="text-xs font-medium text-neutral-500">Desde</label>
                <input
                  type="date"
                  [value]="dateFrom()"
                  (change)="onDateFromChange($event)"
                  class="rounded-lg border border-neutral-200 px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none"
                />
              </div>
              <div class="flex flex-col gap-1">
                <label class="text-xs font-medium text-neutral-500">Hasta</label>
                <input
                  type="date"
                  [value]="dateTo()"
                  (change)="onDateToChange($event)"
                  class="rounded-lg border border-neutral-200 px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none"
                />
              </div>
              <div class="flex gap-2 pb-0.5">
                <button
                  type="button"
                  (click)="applyPresetDays(7)"
                  [class]="presetDaysButtonClass(isPreset7())"
                  [attr.aria-pressed]="isPreset7()"
                >
                  7 días
                </button>
                <button
                  type="button"
                  (click)="applyPresetDays(30)"
                  [class]="presetDaysButtonClass(isPreset30())"
                  [attr.aria-pressed]="isPreset30()"
                >
                  30 días
                </button>
              </div>
              @for (trackId of [status()]; track trackId) {
                <app-dropdown-search
                  class="min-w-[180px]"
                  [options]="statusOptions"
                  [value]="status() || null"
                  placeholder="Todos los estados"
                  (valueChange)="onStatusChange($event)"
                ></app-dropdown-search>
              }
          </div>
        </div>

        @if (loading()) {
          <p class="text-sm text-neutral-400">Cargando conversaciones...</p>
        } @else if (isEmpty()) {
          <p class="text-sm text-neutral-600">No hay conversaciones</p>
          @if (hasActiveFilters()) {
            <p class="mt-1 text-xs text-neutral-500">
              Prueba ampliar el rango de fechas o quitar filtros.
            </p>
          }
        } @else {
          <app-paginated-table
            [data]="tableData()"
            [columns]="columns"
            (pageChange)="onPageChange($event)"
          ></app-paginated-table>
        }
      </section>
    </div>

    <ng-template #statusTpl let-item>
      <app-status-pill
        [status]="item._statusLabel"
        type="access"
      ></app-status-pill>
    </ng-template>

    <ng-template #dateTpl let-item>
      {{ formatDate(item.createdAt) }}
    </ng-template>

    <ng-template #actionsTpl let-item>
      <button
        type="button"
        (click)="onViewConversation(item)"
        class="text-sm font-medium text-blue-600 transition hover:underline"
      >
        Ver
      </button>
    </ng-template>

    @if (store.bot(); as bot) {
      <bf-conversation-detail-drawer
        [open]="drawerOpen()"
        [botId]="bot.id"
        [conversationId]="selectedConversationId()"
        (closed)="closeDrawer()"
      />
    }
  `,
})
export class BotEditChatHistory implements OnInit {
  protected readonly store = inject(BotEditStore);
  private readonly api = inject(Api);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);

  // The opened conversation is reflected in the URL (`?conversationId=`) so it
  // can be deep-linked, survives refresh, and responds to the back button.
  private readonly queryParams = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });

  readonly statusTpl = viewChild<TemplateRef<unknown>>('statusTpl');
  readonly dateTpl = viewChild<TemplateRef<unknown>>('dateTpl');
  readonly actionsTpl = viewChild<TemplateRef<unknown>>('actionsTpl');

  readonly statusOptions = STATUS_OPTIONS;

  columns: PaginatedColumnDef[] = [];

  readonly loading = signal(true);

  readonly selectedConversationId = computed(() => {
    const raw = this.queryParams().get('conversationId');
    const id = raw ? Number(raw) : NaN;
    return Number.isFinite(id) ? id : null;
  });
  readonly drawerOpen = computed(() => this.selectedConversationId() !== null);

  readonly dateFrom = signal(defaultChatHistoryDateRange().from);
  readonly dateTo = signal(defaultChatHistoryDateRange().to);
  readonly status = signal('');
  readonly page = signal(1);

  readonly tableData = signal({
    items: [] as ConversationTableRow[],
    page: 1,
    pageSize: 20,
    totalItems: 0,
    totalPages: 0,
  });

  readonly isEmpty = computed(
    () => !this.loading() && this.tableData().items.length === 0,
  );

  readonly isPreset7 = computed(() =>
    matchesDatePreset(this.dateFrom(), this.dateTo(), 7),
  );

  readonly isPreset30 = computed(() =>
    matchesDatePreset(this.dateFrom(), this.dateTo(), 30),
  );

  readonly presetDaysButtonClass = presetDaysButtonClass;

  private lastBotId: number | null = null;

  constructor() {
    effect(() => {
      const bot = this.store.bot();
      if (!bot) return;

      if (this.lastBotId !== bot.id) {
        this.lastBotId = bot.id;
        this.resetFilters();
        this.page.set(1);
      }

      void this.loadConversations(bot.id);
    });
  }

  ngOnInit() {
    // Tokens come from telemetry: the column only exists for whoever may read it.
    const showTokens = canSeeTelemetry(this.auth.user()?.permissions ?? []);
    this.columns = [
      { header: 'ID', field: 'id', width: '70px' },
      { header: 'Estado', template: this.statusTpl(), width: '110px' },
      { header: 'Mensajes', field: 'messageCount', width: '100px', align: 'center' },
      ...(showTokens ? [{ header: 'Tokens', field: '_tokens', width: '110px', align: 'center' as const }] : []),
      { header: 'Fecha', template: this.dateTpl(), width: '160px' },
      { header: 'Acciones', template: this.actionsTpl(), width: '90px', align: 'center' },
    ];
  }

  hasActiveFilters(): boolean {
    return (
      this.status() !== '' ||
      this.dateFrom() !== defaultChatHistoryDateRange().from ||
      this.dateTo() !== defaultChatHistoryDateRange().to
    );
  }

  onDateFromChange(event: Event) {
    const value = (event.target as HTMLInputElement).value;
    if (value) {
      this.dateFrom.set(value);
      this.resetAndLoad();
    }
  }

  onDateToChange(event: Event) {
    const value = (event.target as HTMLInputElement).value;
    if (value) {
      this.dateTo.set(value);
      this.resetAndLoad();
    }
  }

  applyPresetDays(days: number) {
    const to = new Date();
    const from = new Date();
    from.setDate(from.getDate() - days);
    this.dateFrom.set(formatDateInput(from));
    this.dateTo.set(formatDateInput(to));
    this.resetAndLoad();
  }

  onStatusChange(value: string | null) {
    this.status.set(value ?? '');
    this.resetAndLoad();
  }

  onPageChange(page: number) {
    this.page.set(page);
    const bot = this.store.bot();
    if (bot) void this.loadConversations(bot.id);
  }

  onViewConversation(item: ConversationResponse) {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { conversationId: item.id },
      queryParamsHandling: 'merge',
    });
  }

  closeDrawer() {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { conversationId: null },
      queryParamsHandling: 'merge',
    });
  }

  formatDate(iso: string): string {
    return new Date(iso).toLocaleDateString('es', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  private resetFilters() {
    const range = defaultChatHistoryDateRange();
    this.dateFrom.set(range.from);
    this.dateTo.set(range.to);
    this.status.set('');
  }

  private resetAndLoad() {
    this.page.set(1);
    const bot = this.store.bot();
    if (bot) void this.loadConversations(bot.id);
  }

  private async loadConversations(botId: number) {
    this.loading.set(true);
    try {
      const params: GetConversations$Params = {
        botId,
        page: this.page(),
        pageSize: 20,
        from: toUtcStartOfDay(this.dateFrom()),
        to: toUtcEndOfDay(this.dateTo()),
      };

      const statusFilter = this.status();
      if (statusFilter) {
        params.status = statusFilter;
      }

      const result = await this.api.invoke(getConversations, params);
      this.tableData.set({
        items: result.items.map((c) => ({
          ...c,
          _statusLabel: mapConversationStatusLabel(c.status),
          _date: this.formatDate(c.createdAt),
          _tokens: c.tokens ? formatTokenCount(c.tokens.totalTokens) : '—',
        })),
        page: Number(result.page),
        pageSize: Number(result.pageSize),
        totalItems: Number(result.totalItems),
        totalPages: Number(result.totalPages),
      });
    } catch {
      toast.error('Error al cargar conversaciones.');
    } finally {
      this.loading.set(false);
    }
  }
}
