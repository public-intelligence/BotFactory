import {
  Component,
  HostListener,
  inject,
  signal,
  ElementRef,
  viewChild,
  effect,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { TodoService } from '../../services/todo.service';
import { SearchResult } from '../../models/todo.model';
import { Subject, debounceTime, switchMap, of } from 'rxjs';
import { DatePipe } from '@angular/common';

@Component({
  selector: 'app-command-search',
  standalone: true,
  imports: [FormsModule, LucideAngularModule, DatePipe],
  template: `
    @if (isOpen()) {
      <div
        class="fixed inset-0 z-50 flex items-start justify-center pt-[20vh]"
        (click)="close()"
      >
        <!-- Backdrop -->
        <div class="absolute inset-0 bg-black/50 backdrop-blur-sm"></div>

        <!-- Dialog -->
        <div
          class="relative w-full max-w-lg bg-white rounded-2xl shadow-2xl overflow-hidden"
          (click)="$event.stopPropagation()"
        >
          <!-- Search input -->
          <div class="flex items-center gap-3 px-4 py-3 border-b border-gray-100">
            <lucide-icon name="search" [size]="18" class="text-gray-400" />
            <input
              #searchInput
              type="text"
              [(ngModel)]="query"
              (ngModelChange)="onQueryChange($event)"
              placeholder="Search todos..."
              class="flex-1 text-sm text-gray-700 placeholder-gray-400
                     focus:outline-none bg-transparent"
            />
            <kbd
              class="hidden sm:inline-flex items-center px-1.5 py-0.5 text-[10px]
                     font-medium text-gray-400 bg-gray-100 rounded"
            >
              ESC
            </kbd>
          </div>

          <!-- Results -->
          <div class="max-h-72 overflow-y-auto">
            @if (results().length > 0) {
              @for (result of results(); track result.id) {
                <div
                  class="flex items-center gap-3 px-4 py-3 hover:bg-gray-50
                         transition-colors cursor-default"
                >
                  <lucide-icon
                    [name]="result.isCompleted ? 'circle-check' : 'circle'"
                    [size]="18"
                    [strokeWidth]="1.5"
                    [class]="result.isCompleted ? 'text-indigo-500' : 'text-gray-300'"
                  />
                  <div class="flex-1 min-w-0">
                    <p
                      class="text-sm truncate"
                      [class]="result.isCompleted
                        ? 'line-through text-gray-400'
                        : 'text-gray-700'"
                    >
                      {{ result.title }}
                    </p>
                    <p class="text-xs text-gray-400 mt-0.5">
                      {{ result.createdAt | date : 'MMM d, y' }}
                    </p>
                  </div>
                  <span class="text-xs text-gray-300 tabular-nums">
                    {{ ((1 - result.distance) * 100).toFixed(0) }}%
                  </span>
                </div>
              }
            } @else if (query && !isLoading()) {
              <div class="flex flex-col items-center justify-center py-8 text-gray-400">
                <lucide-icon name="search-x" [size]="32" [strokeWidth]="1" />
                <p class="mt-2 text-sm">No results found</p>
              </div>
            } @else if (!query) {
              <div class="flex flex-col items-center justify-center py-8 text-gray-400">
                <lucide-icon name="sparkles" [size]="32" [strokeWidth]="1" />
                <p class="mt-2 text-sm">Semantic search powered by embeddings</p>
              </div>
            }
          </div>
        </div>
      </div>
    }
  `,
})
export class CommandSearchComponent {
  private todoService = inject(TodoService);
  private searchInput = viewChild<ElementRef<HTMLInputElement>>('searchInput');

  isOpen = signal(false);
  query = '';
  results = signal<SearchResult[]>([]);
  isLoading = signal(false);

  private search$ = new Subject<string>();

  constructor() {
    this.search$
      .pipe(
        debounceTime(300),
        switchMap((q) => {
          if (!q.trim()) {
            return of([]);
          }
          this.isLoading.set(true);
          return this.todoService.search(q);
        })
      )
      .subscribe((results) => {
        this.results.set(results);
        this.isLoading.set(false);
      });

    effect(() => {
      if (this.isOpen()) {
        setTimeout(() => this.searchInput()?.nativeElement.focus(), 0);
      }
    });
  }

  @HostListener('document:keydown', ['$event'])
  onKeydown(event: KeyboardEvent) {
    if ((event.metaKey || event.ctrlKey) && event.key === 'k') {
      event.preventDefault();
      this.toggle();
    }
    if (event.key === 'Escape' && this.isOpen()) {
      this.close();
    }
  }

  toggle() {
    this.isOpen.update((v) => !v);
    if (!this.isOpen()) {
      this.reset();
    }
  }

  close() {
    this.isOpen.set(false);
    this.reset();
  }

  onQueryChange(value: string) {
    this.search$.next(value);
  }

  private reset() {
    this.query = '';
    this.results.set([]);
  }
}
