export interface Todo {
  id: number;
  title: string;
  isCompleted: boolean;
  createdAt: string;
}

export interface SearchResult {
  id: number;
  title: string;
  isCompleted: boolean;
  createdAt: string;
  distance: number;
}
