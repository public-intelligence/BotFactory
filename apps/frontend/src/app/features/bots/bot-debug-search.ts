import { Component, computed, inject, signal } from '@angular/core';
import { Api } from '../../api/api';
import { debugSearch } from '../../api/functions';
import { SearchResultItem } from '../../api/models/search-result-item';
import { BotEditStore } from './bot-edit.store';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'bf-bot-debug-search',
  template: `
    <div class="space-y-6">
      <!-- Bot params -->
      <div class="flex items-center gap-4 text-xs text-neutral-500">
        <span class="rounded bg-neutral-100 px-2 py-1 font-mono">
          Top K: {{ bot()?.topK }}
        </span>
        <span class="rounded bg-neutral-100 px-2 py-1 font-mono">
          Umbral: {{ bot()?.similarityThreshold }}
        </span>
        <label class="ml-auto flex cursor-pointer items-center gap-1.5">
          <input
            type="checkbox"
            [checked]="ignoreThreshold()"
            (change)="ignoreThreshold.set(!ignoreThreshold())"
            class="h-3.5 w-3.5 rounded border-neutral-300 accent-blue-600"
          />
          <span class="font-mono">Ignore threshold</span>
        </label>
      </div>

      <!-- Search input -->
      <div class="flex gap-3">
        <input
          #queryInput
          type="text"
          placeholder="Search your knowledge base..."
          (keydown.enter)="onSearch(queryInput.value)"
          class="flex-1 rounded-lg border border-neutral-200 bg-white px-4 py-2.5 text-sm text-neutral-900 placeholder:text-neutral-400 focus:border-blue-400 focus:outline-none"
        />
        <button
          (click)="onSearch(queryInput.value)"
          [disabled]="searching()"
          class="rounded-lg bg-blue-600 px-5 py-2.5 text-sm font-medium text-white transition hover:bg-blue-700 disabled:opacity-50"
        >
          {{ searching() ? 'Searching...' : 'Search' }}
        </button>
      </div>

      <!-- Results -->
      @if (lastQuery()) {
        <p class="text-sm text-neutral-500">
          {{ results().length }} results for
          <span class="font-medium text-neutral-700">"{{ lastQuery() }}"</span>
        </p>
      }

      @for (result of results(); track result.chunkId; let i = $index) {
        <div class="rounded-lg border border-neutral-200 bg-white p-4">
          <div class="mb-2 flex items-center gap-3 text-xs text-neutral-400">
            <span class="rounded bg-neutral-100 px-2 py-0.5 font-mono">
              #{{ i + 1 }}
            </span>
            <span>Doc {{ result.documentId }}</span>
            <span>Chunk {{ result.chunkIndex }}</span>
            <span>{{ result.sourceLocation }}</span>
            <span
              class="ml-auto rounded px-2 py-0.5 font-mono font-medium"
              [class]="
                +result.distance < 0.3
                  ? 'bg-green-100 text-green-700'
                  : +result.distance < 0.6
                    ? 'bg-yellow-100 text-yellow-700'
                    : 'bg-red-100 text-red-700'
              "
            >
              {{ (+result.distance).toFixed(4) }}
            </span>
          </div>
          <p
            class="whitespace-pre-wrap text-sm leading-relaxed text-neutral-700"
          >
            {{ result.text }}
          </p>
        </div>
      }

      @if (lastQuery() && results().length === 0 && !searching()) {
        <p class="py-8 text-center text-sm text-neutral-400">
          No results found.
        </p>
      }
    </div>
  `,
})
export class BotDebugSearch {
  private readonly api = inject(Api);
  private readonly store = inject(BotEditStore);

  readonly bot = this.store.bot;
  readonly searching = signal(false);
  readonly results = signal<SearchResultItem[]>([]);
  readonly lastQuery = signal('');
  readonly ignoreThreshold = signal(false);

  async onSearch(query: string) {
    query = query.trim();
    const bot = this.bot();
    if (!query || !bot || this.searching()) return;

    this.searching.set(true);
    this.lastQuery.set(query);

    try {
      const response = await this.api.invoke(debugSearch, {
        body: this.ignoreThreshold()
          ? { query, topK: bot.topK, botId: bot.id, maxDistance: 2 }
          : { query, topK: bot.topK, botId: bot.id },
      });
      this.results.set(response.results);
    } catch {
      toast.error('Search failed. Is the embeddings API key configured?');
      this.results.set([]);
    } finally {
      this.searching.set(false);
    }
  }
}
