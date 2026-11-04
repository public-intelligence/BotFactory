import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { formatAssistantMessage } from '../message-format';

/**
 * An assistant answer laid out for reading: paragraphs, bullet and numbered lists
 * (marker in its own column, hanging indent), bold and tappable links. Blocks come
 * from formatAssistantMessage; everything renders through Angular's escaping.
 */
@Component({
  selector: 'bf-assistant-message',
  standalone: true,
  imports: [NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-template #inline let-segments>
      @for (seg of segments; track $index) {
        @if (seg.type === 'text') {
          <span [class.font-semibold]="seg.bold">{{ seg.value }}</span>
        } @else {
          <a
            [href]="seg.href"
            [attr.target]="seg.type === 'url' ? '_blank' : null"
            [attr.rel]="seg.type === 'url' ? 'noopener noreferrer' : null"
            class="underline underline-offset-2"
            [class.font-semibold]="seg.bold"
            [class.text-blue-700]="!dark() && !branded()"
            [class.text-sky-300]="dark() && !branded()"
            [style.color]="branded() ? 'var(--bf-accent)' : null"
            >{{ seg.value }}</a
          >
        }
      }
    </ng-template>

    <div class="flex flex-col gap-2 break-words leading-relaxed">
      @for (block of blocks(); track $index) {
        @switch (block.kind) {
          @case ('heading') {
            <p class="font-semibold">
              <ng-container *ngTemplateOutlet="inline; context: { $implicit: block.segments }" />
            </p>
          }
          @case ('list') {
            @if (block.ordered) {
              <ol class="flex flex-col gap-1">
                @for (item of block.items; track $index) {
                  <li class="flex gap-2">
                    <span class="shrink-0 tabular-nums" aria-hidden="true">{{ item.marker }}</span>
                    <span class="min-w-0 flex-1">
                      <ng-container *ngTemplateOutlet="inline; context: { $implicit: item.segments }" />
                    </span>
                  </li>
                }
              </ol>
            } @else {
              <ul class="flex flex-col gap-1">
                @for (item of block.items; track $index) {
                  <li class="flex gap-2">
                    <span class="shrink-0" aria-hidden="true">•</span>
                    <span class="min-w-0 flex-1">
                      <ng-container *ngTemplateOutlet="inline; context: { $implicit: item.segments }" />
                    </span>
                  </li>
                }
              </ul>
            }
          }
          @default {
            <p [class.font-semibold]="block.kind === 'paragraph' && block.lead">
              <ng-container *ngTemplateOutlet="inline; context: { $implicit: block.segments }" />
            </p>
          }
        }
      }
    </div>
  `,
})
export class AssistantMessageComponent {
  readonly content = input<string>('');
  readonly dark = input<boolean>(false);
  readonly branded = input<boolean>(false);

  protected readonly blocks = computed(() => formatAssistantMessage(this.content()));
}
