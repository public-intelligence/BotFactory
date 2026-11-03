import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { TodoService } from './services/todo.service';
import { Todo } from './models/todo.model';
import { TodoInputComponent } from './components/todo-input/todo-input.component';
import { TodoListComponent } from './components/todo-list/todo-list.component';
import { TodoFooterComponent, Filter } from './components/todo-footer/todo-footer.component';
import { LucideAngularModule } from 'lucide-angular';
import { CommandSearchComponent } from './components/command-search/command-search.component';

@Component({
  selector: 'app-root',
  imports: [
    TodoInputComponent,
    TodoListComponent,
    TodoFooterComponent,
    LucideAngularModule,
    CommandSearchComponent,
  ],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App implements OnInit {
  private todoService = inject(TodoService);

  todos = signal<Todo[]>([]);
  filter = signal<Filter>('all');

  filteredTodos = computed(() => {
    const todos = this.todos();
    switch (this.filter()) {
      case 'active':
        return todos.filter((t) => !t.isCompleted);
      case 'completed':
        return todos.filter((t) => t.isCompleted);
      default:
        return todos;
    }
  });

  activeCount = computed(
    () => this.todos().filter((t) => !t.isCompleted).length
  );

  ngOnInit() {
    this.loadTodos();
  }

  addTodo(title: string) {
    this.todoService.create(title).subscribe(() => this.loadTodos());
  }

  toggleTodo(id: number) {
    this.todoService.toggle(id).subscribe(() => this.loadTodos());
  }

  deleteTodo(id: number) {
    this.todoService.delete(id).subscribe(() => this.loadTodos());
  }

  private loadTodos() {
    this.todoService.getAll().subscribe((todos) => this.todos.set(todos));
  }
}
