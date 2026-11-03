import { Injectable, signal } from '@angular/core';
import type { BotResponse } from '../../api/models/bot-response';

@Injectable()
export class BotEditStore {
  readonly bot = signal<BotResponse | null>(null);
  readonly loading = signal(true);
  readonly publishing = signal(false);
  readonly error = signal<string | null>(null);
}
