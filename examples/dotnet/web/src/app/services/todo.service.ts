import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Todo, SearchResult } from '../models/todo.model';

@Injectable({ providedIn: 'root' })
export class TodoService {
  private http = inject(HttpClient);
  private apiUrl = 'http://localhost:5179/api/todos';

  getAll() {
    return this.http.get<Todo[]>(this.apiUrl);
  }

  create(title: string) {
    return this.http.post<Todo>(this.apiUrl, { title, isCompleted: false });
  }

  toggle(id: number) {
    return this.http.patch<Todo>(`${this.apiUrl}/${id}/toggle`, {});
  }

  delete(id: number) {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }

  search(query: string) {
    return this.http.get<SearchResult[]>(`${this.apiUrl}/search`, {
      params: { q: query },
    });
  }
}
