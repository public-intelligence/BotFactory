import { Directive, ElementRef, inject, OnDestroy, OnInit } from '@angular/core';

/**
 * Auto-flips a hover tooltip above/below its trigger so it isn't clipped by the
 * scrollable feed (which has `overflow-y-auto` and a fixed header). Place on the
 * absolutely-positioned popover element; its trigger is the parent. On hover we
 * measure the room above the trigger within the nearest scroll container and
 * toggle the vertical placement classes accordingly. Default markup should keep
 * the "above" classes (`bottom-full mb-1.5`); this only flips when needed.
 */
@Directive({
  selector: '[bfTooltipFlip]',
  standalone: true,
})
export class TooltipFlipDirective implements OnInit, OnDestroy {
  private readonly el = inject<ElementRef<HTMLElement>>(ElementRef);
  private trigger: HTMLElement | null = null;
  private readonly place = (): void => {
    const popover = this.el.nativeElement;
    const trigger = this.trigger;
    if (!trigger) return;

    const tip = popover.getBoundingClientRect();
    const trg = trigger.getBoundingClientRect();
    const box = this.scrollParent(trigger)?.getBoundingClientRect();
    const topBound = box?.top ?? 0;

    // Vertical: not enough room above → drop the tooltip below the trigger.
    const flipBelow = trg.top - topBound < tip.height + 8;
    popover.classList.toggle('bottom-full', !flipBelow);
    popover.classList.toggle('mb-1.5', !flipBelow);
    popover.classList.toggle('top-full', flipBelow);
    popover.classList.toggle('mt-1.5', flipBelow);

    // Horizontal: anchor toward the side with more room so a wide tooltip stays
    // inside the container. Trigger in the right half → grow left (right-0);
    // otherwise grow right (left-0).
    const containerMid = box ? (box.left + box.right) / 2 : window.innerWidth / 2;
    const triggerMid = (trg.left + trg.right) / 2;
    const growLeft = triggerMid > containerMid;
    popover.classList.toggle('right-0', growLeft);
    popover.classList.toggle('left-0', !growLeft);
  };

  ngOnInit(): void {
    this.trigger = this.el.nativeElement.parentElement;
    this.trigger?.addEventListener('mouseenter', this.place);
  }

  ngOnDestroy(): void {
    this.trigger?.removeEventListener('mouseenter', this.place);
  }

  private scrollParent(node: HTMLElement): HTMLElement | null {
    for (let n = node.parentElement; n; n = n.parentElement) {
      const oy = getComputedStyle(n).overflowY;
      if (oy === 'auto' || oy === 'scroll') return n;
    }
    return null;
  }
}
