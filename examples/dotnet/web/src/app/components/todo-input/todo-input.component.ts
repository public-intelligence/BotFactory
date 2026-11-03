import { Component, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';

@Component({
  selector: 'app-todo-input',
  standalone: true,
  imports: [FormsModule, LucideAngularModule],
  template: `
    <div class="flex gap-2 p-4">
      <input
        type="text"
        [(ngModel)]="title"
        (keydown.enter)="submit()"
        placeholder="What needs to be done?"
        class="flex-1 px-4 py-2.5 border border-gray-200 rounded-xl
               focus:ring-2 focus:ring-indigo-500 focus:border-transparent
               focus:outline-none text-gray-700 placeholder-gray-400
               transition-shadow"
      />
      <button
        (click)="submit()"
        class="px-4 py-2.5 bg-indigo-500 hover:bg-indigo-600 text-white
               rounded-xl transition-colors shadow-sm hover:shadow-md
               active:scale-95 transform"
      >
        <lucide-icon name="plus" [size]="20" />
      </button>
    </div>
  `,
})
export class TodoInputComponent {
  title = '';
  added = output<string>();

  submit() {
    const trimmed = this.title.trim();
    if (trimmed) {
      this.added.emit(trimmed);
      this.title = '';
    }
  }
}
