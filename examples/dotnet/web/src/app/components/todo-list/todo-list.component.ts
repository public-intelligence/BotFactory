import { Component, input, output } from '@angular/core';
import { Todo } from '../../models/todo.model';
import { TodoItemComponent } from '../todo-item/todo-item.component';
import { LucideAngularModule } from 'lucide-angular';

@Component({
  selector: 'app-todo-list',
  standalone: true,
  imports: [TodoItemComponent, LucideAngularModule],
  template: `
    @if (todos().length === 0) {
      <div class="flex flex-col items-center justify-center py-12 text-gray-300">
        <lucide-icon name="clipboard-list" [size]="48" [strokeWidth]="1" />
        <p class="mt-3 text-sm text-gray-400">No todos yet. Add one above!</p>
      </div>
    } @else {
      <div class="divide-y divide-gray-100">
        @for (todo of todos(); track todo.id) {
          <app-todo-item
            [todo]="todo"
            (toggled)="toggled.emit($event)"
            (deleted)="deleted.emit($event)"
          />
        }
      </div>
    }
  `,
})
export class TodoListComponent {
  todos = input.required<Todo[]>();
  toggled = output<number>();
  deleted = output<number>();
}
