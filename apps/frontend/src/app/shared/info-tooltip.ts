import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-info-tooltip',
  standalone: true,
  template: `
    <span class="group relative ml-1.5 inline-flex items-center align-middle">
      <svg
        xmlns="http://www.w3.org/2000/svg"
        viewBox="0 0 20 20"
        fill="currentColor"
        class="h-4 w-4 text-neutral-400 hover:text-neutral-600 cursor-help"
        [attr.aria-label]="text"
        [attr.title]="text"
        tabindex="0"
      >
        <path
          fill-rule="evenodd"
          d="M10 18a8 8 0 1 0 0-16 8 8 0 0 0 0 16Zm.75-11a.75.75 0 1 1-1.5 0 .75.75 0 0 1 1.5 0ZM9 9a.75.75 0 0 0 0 1.5h.25v3h-.25a.75.75 0 0 0 0 1.5h2a.75.75 0 0 0 0-1.5h-.25V9.75A.75.75 0 0 0 10 9H9Z"
          clip-rule="evenodd"
        />
      </svg>
      <span
        role="tooltip"
        class="pointer-events-none absolute left-1/2 top-full z-20 mt-2 w-64 -translate-x-1/2 rounded-md bg-neutral-900 px-3 py-2 text-xs font-normal leading-relaxed text-white opacity-0 shadow-lg transition-opacity group-hover:opacity-100 group-focus-within:opacity-100"
      >
        {{ text }}
      </span>
    </span>
  `,
})
export class InfoTooltip {
  @Input({ required: true }) text!: string;
}
