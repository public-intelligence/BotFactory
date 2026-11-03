import { Component, input, output, model } from '@angular/core';

export type Filter = 'all' | 'active' | 'completed';

@Component({
  selector: 'app-todo-footer',
  standalone: true,
  template: `
    <div class="flex items-center justify-between px-4 py-3 text-sm text-gray-500">
      <span>
        {{ activeCount() }} item{{ activeCount() === 1 ? '' : 's' }} left
      </span>

      <div class="flex gap-1">
        @for (f of filters; track f) {
          <button
            (click)="filterChange.emit(f)"
            class="px-3 py-1 rounded-lg capitalize transition-colors"
            [class]="currentFilter() === f
              ? 'bg-indigo-100 text-indigo-700 font-medium'
              : 'hover:bg-gray-100 text-gray-500'"
          >
            {{ f }}
          </button>
        }
      </div>
    </div>
  `,
})
export class TodoFooterComponent {
  activeCount = input.required<number>();
  currentFilter = input.required<Filter>();
  filterChange = output<Filter>();

  filters: Filter[] = ['all', 'active', 'completed'];
}
