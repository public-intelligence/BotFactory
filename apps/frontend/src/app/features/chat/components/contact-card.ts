import {
  ChangeDetectionStrategy,
  Component,
  input,
  output,
} from '@angular/core';
import { telHref } from '../contact-extract';

export interface ContactCardData {
  urls: string[];
  phones: string[];
  emails: string[];
}

@Component({
  selector: 'bf-contact-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="bf-contact-card pointer-events-auto relative w-[min(100%,22rem)] overflow-hidden rounded-2xl border border-white/10 py-3 pl-4 pr-10 shadow-2xl backdrop-blur-xl"
      style="background: color-mix(in srgb, var(--bf-surface) 92%, transparent); color: var(--bf-fg)"
      role="status"
      aria-live="polite"
    >
      <button
        type="button"
        class="absolute right-2 top-2 flex h-8 w-8 items-center justify-center rounded-full transition-colors hover:bg-white/10"
        style="color: var(--bf-muted)"
        title="Cerrar"
        aria-label="Cerrar tarjeta de contacto"
        (click)="closed.emit()"
      >
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="h-4 w-4">
          <path
            d="M6.2 4.8a1 1 0 0 1 1.4 0L12 9.2l4.4-4.4a1 1 0 1 1 1.4 1.4L13.4 10.6l4.4 4.4a1 1 0 1 1-1.4 1.4L12 12l-4.4 4.4a1 1 0 1 1-1.4-1.4l4.4-4.4-4.4-4.4a1 1 0 0 1 0-1.4Z"
          />
        </svg>
      </button>

      @if (data().urls.length) {
        <p class="mb-1.5 text-[11px] font-semibold uppercase tracking-wide opacity-70">
          {{ data().urls.length === 1 ? 'Enlace' : 'Enlaces' }}
        </p>
        <ul class="mb-2 flex flex-col gap-1.5">
          @for (url of data().urls; track url) {
            <li>
              <button
                type="button"
                class="block w-full truncate rounded-lg px-2.5 py-1.5 text-left text-sm font-medium underline-offset-2 transition-colors hover:underline"
                style="background: color-mix(in srgb, var(--bf-accent) 18%, transparent); color: var(--bf-accent)"
                [title]="url"
                (click)="openUrl(url)"
              >
                {{ displayUrl(url) }}
              </button>
            </li>
          }
        </ul>
      }

      @if (data().emails.length) {
        <p
          class="mb-1.5 text-[11px] font-semibold uppercase tracking-wide opacity-70"
          [class.mt-2]="data().urls.length > 0"
        >
          {{ data().emails.length === 1 ? 'Correo' : 'Correos' }}
        </p>
        <ul class="mb-2 flex flex-col gap-1.5">
          @for (email of data().emails; track email) {
            <li>
              <button
                type="button"
                class="block w-full truncate rounded-lg px-2.5 py-1.5 text-left text-sm font-medium underline-offset-2 transition-colors hover:underline"
                style="background: color-mix(in srgb, var(--bf-accent) 18%, transparent); color: var(--bf-accent)"
                [title]="email"
                (click)="openEmail(email)"
              >
                {{ email }}
              </button>
            </li>
          }
        </ul>
      }

      @if (data().phones.length) {
        <p
          class="mb-1.5 text-[11px] font-semibold uppercase tracking-wide opacity-70"
          [class.mt-2]="data().urls.length > 0 || data().emails.length > 0"
        >
          {{ data().phones.length === 1 ? 'Teléfono' : 'Teléfonos' }}
        </p>
        <ul class="flex flex-col gap-1.5">
          @for (phone of data().phones; track phone) {
            <li>
              <button
                type="button"
                class="block w-full truncate rounded-lg px-2.5 py-1.5 text-left text-sm font-medium tabular-nums underline-offset-2 transition-colors hover:underline"
                style="background: color-mix(in srgb, var(--bf-accent) 18%, transparent); color: var(--bf-accent)"
                [title]="phone"
                (click)="openPhone(phone)"
              >
                {{ phone }}
              </button>
            </li>
          }
        </ul>
      }
    </div>
  `,
  styles: `
    :host {
      display: block;
      animation: bf-contact-in 280ms ease-out both;
    }

    @keyframes bf-contact-in {
      from {
        opacity: 0;
        transform: translateY(12px);
      }
      to {
        opacity: 1;
        transform: translateY(0);
      }
    }
  `,
})
export class ContactCard {
  readonly data = input.required<ContactCardData>();
  readonly opened = output<string>();
  readonly closed = output<void>();

  displayUrl(url: string): string {
    return url.replace(/^https?:\/\//i, '');
  }

  openUrl(url: string): void {
    const href = /^https?:\/\//i.test(url) ? url : `https://${url}`;
    window.open(href, '_blank', 'noopener');
    this.opened.emit(href);
  }

  openEmail(email: string): void {
    window.location.href = `mailto:${email}`;
    this.opened.emit(`mailto:${email}`);
  }

  openPhone(phone: string): void {
    const href = telHref(phone);
    window.location.href = href;
    this.opened.emit(href);
  }
}
