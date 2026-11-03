import { Injectable, inject } from '@angular/core';
import { Api } from '../../api/api';
import type { BotResponse } from '../../api/models/bot-response';

@Injectable({ providedIn: 'root' })
export class BotBrandingService {
  private readonly api = inject(Api);

  resolveLogoUrl(bot: BotResponse, slug: string): string | null {
    if (!bot.hasCustomLogo) return null;
    const base = `${this.api.rootUrl}/api/bots/by-slug/${encodeURIComponent(slug)}/logo/download`;
    // logoVersion changes on every upload, so a URL carrying it is safe for the
    // API to mark immutable. Without one the API falls back to revalidation —
    // still correct, just a 304 per load.
    return bot.logoVersion ? `${base}?v=${encodeURIComponent(bot.logoVersion)}` : base;
  }
}
