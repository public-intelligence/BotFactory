import { Component, input, output } from '@angular/core';
import { Todo } from '../../models/todo.model';
import { LucideAngularModule } from 'lucide-angular';

@Component({
  selector: 'app-todo-item',
  standalone: true,
  imports: [LucideAngularModule],
  template: `
    <div
      class="group flex items-center gap-3 px-4 py-3 border-b border-gray-100
             hover:bg-gray-50/80 transition-colors"
    >
      <button
        (click)="toggled.emit(todo().id)"
        class="flex-shrink-0 transition-colors"
        [class]="todo().isCompleted ? 'text-indigo-500' : 'text-gray-300 hover:text-gray-400'"
      >
        <lucide-icon
          [name]="todo().isCompleted ? 'circle-check' : 'circle'"
          [size]="22"
          [strokeWidth]="1.5"
        />
      </button>

      <span
        class="flex-1 text-sm transition-all"
        [class]="todo().isCompleted
          ? 'line-through text-gray-400'
          : 'text-gray-700'"
      >
        {{ todo().title }}
      </span>

      <button
        (click)="deleted.emit(todo().id)"
        class="flex-shrink-0 opacity-0 group-hover:opacity-100 text-gray-300
               hover:text-red-500 transition-all"
      >
        <lucide-icon name="trash-2" [size]="16" [strokeWidth]="1.5" />
      </button>
    </div>
  `,
})
export class TodoItemComponent {
  todo = input.required<Todo>();
  toggled = output<number>();
  deleted = output<number>();
}
