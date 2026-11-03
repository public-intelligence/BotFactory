import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import {
  LucideIconProvider,
  LUCIDE_ICONS,
  Plus,
  Circle,
  CircleCheck,
  Trash2,
  ClipboardList,
  ListTodo,
  Search,
  SearchX,
  Sparkles,
} from 'lucide-angular';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(),
    {
      provide: LUCIDE_ICONS,
      multi: true,
      useValue: new LucideIconProvider({
        Plus,
        Circle,
        CircleCheck,
        Trash2,
        ClipboardList,
        ListTodo,
        Search,
        SearchX,
        Sparkles,
      }),
    },
  ],
};
