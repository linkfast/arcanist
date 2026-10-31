import { Component, OnDestroy, computed, effect, inject, signal, untracked } from '@angular/core';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { I18n, TranslatePipe } from '../core/i18n/i18n';
import type { TourText } from '../core/tour/tour';
import { TourService } from '../core/tour/tour.service';

interface Box {
  top: number;
  left: number;
  width: number;
  height: number;
}

/** Space around what a step points at. */
const PADDING = 6;
const CARD_WIDTH = 340;

/**
 * The tour being shown: what the step points at stays lit (the rest of the page dimmed) and a card beside it says
 * what it is, with Back, Next, «Skip tour» and «Turn off tours». A step whose element is not on the page shows in
 * the middle. Esc skips; ← and → move.
 */
@Component({
  selector: 'app-tour-overlay',
  imports: [HlmButtonImports, TranslatePipe],
  host: { '(document:keydown)': 'key($event)', '(window:resize)': 'measure()' },
  template: `
    @if (tour.active(); as active) {
      <div class="fixed inset-0 z-[90]" role="dialog" aria-modal="true" [attr.aria-label]="title()">
        @if (box(); as b) {
          <div
            class="pointer-events-none absolute rounded-lg ring-2 ring-primary transition-all duration-200"
            [style.top.px]="b.top - padding"
            [style.left.px]="b.left - padding"
            [style.width.px]="b.width + padding * 2"
            [style.height.px]="b.height + padding * 2"
            style="box-shadow: 0 0 0 9999px rgb(0 0 0 / 0.55)"
          ></div>
        } @else {
          <div class="absolute inset-0 bg-black/55"></div>
        }
        <div
          class="absolute grid gap-3 rounded-xl border bg-popover p-4 text-popover-foreground shadow-2xl"
          [style.top.px]="card().top"
          [style.left.px]="card().left"
          [style.width.px]="card().width"
        >
          <div class="flex items-start justify-between gap-3">
            <h2 class="text-base font-semibold">{{ title() }}</h2>
            <span class="shrink-0 text-xs text-muted-foreground">
              {{ 'tour.step' | t: { n: active.index + 1, total: active.tour.steps.length } }}
            </span>
          </div>
          <p class="text-sm leading-relaxed text-muted-foreground">{{ body() }}</p>
          <div class="flex flex-wrap items-center gap-2">
            <button
              hlmBtn
              variant="link"
              size="sm"
              class="h-auto px-0 text-xs"
              type="button"
              (click)="tour.finish()"
            >
              {{ 'tour.skip' | t }}
            </button>
            <button
              hlmBtn
              variant="link"
              size="sm"
              class="h-auto px-0 text-xs text-muted-foreground"
              type="button"
              (click)="tour.disableAll()"
            >
              {{ 'tour.disable' | t }}
            </button>
            <span class="flex-1"></span>
            @if (active.index > 0) {
              <button hlmBtn variant="outline" size="sm" type="button" (click)="tour.back()">
                {{ 'tour.back' | t }}
              </button>
            }
            <button hlmBtn size="sm" type="button" (click)="tour.next()">
              {{ (active.index + 1 < active.tour.steps.length ? 'tour.next' : 'tour.done') | t }}
            </button>
          </div>
        </div>
      </div>
    }
  `,
})
export class TourOverlay implements OnDestroy {
  protected readonly tour = inject(TourService);
  private readonly i18n = inject(I18n);

  protected readonly padding = PADDING;
  protected readonly box = signal<Box | null>(null);
  private readonly viewport = signal({ width: window.innerWidth, height: window.innerHeight });
  private timer: ReturnType<typeof setTimeout> | null = null;
  private onScroll = () => this.measure();

  private readonly step = computed(() => {
    const active = this.tour.active();
    return active ? active.tour.steps[active.index] : null;
  });

  protected readonly title = computed(() => this.text(this.step()?.title));
  protected readonly body = computed(() => this.text(this.step()?.body));

  /** The card: below what is lit (or above, if there is no room), centered without it; across the bottom on phones. */
  protected readonly card = computed(() => {
    const { width, height } = this.viewport();
    const cardWidth = Math.min(CARD_WIDTH, width - 24);
    const b = this.box();
    if (!b)
      return {
        top: Math.max(12, height / 2 - 120),
        left: (width - cardWidth) / 2,
        width: cardWidth,
      };
    const left = Math.min(Math.max(12, b.left), width - cardWidth - 12);
    const below = b.top + b.height + PADDING + 12;
    const top = below + 220 < height ? below : Math.max(12, b.top - PADDING - 12 - 220);
    return { top, left, width: cardWidth };
  });

  constructor() {
    // Each step: find what it points at (pages may still be loading it), bring it into view and light it.
    effect(() => {
      const step = this.step();
      untracked(() => this.find(step?.target ?? null, 0));
    });
    window.addEventListener('scroll', this.onScroll, true);
  }

  ngOnDestroy(): void {
    window.removeEventListener('scroll', this.onScroll, true);
    if (this.timer) clearTimeout(this.timer);
  }

  protected key(event: KeyboardEvent): void {
    if (!this.tour.active()) return;
    if (event.key === 'Escape') this.tour.finish();
    else if (event.key === 'ArrowRight') this.tour.next();
    else if (event.key === 'ArrowLeft') this.tour.back();
  }

  protected measure(): void {
    this.viewport.set({ width: window.innerWidth, height: window.innerHeight });
    const element = this.element(this.step()?.target ?? null);
    this.box.set(element ? toBox(element.getBoundingClientRect()) : null);
  }

  /** Waits up to a second and a half for the step's element (a page loading its data). */
  private find(target: string | null, tries: number): void {
    if (this.timer) clearTimeout(this.timer);
    const element = this.element(target);
    if (!element && target && tries < 15) {
      this.box.set(null);
      this.timer = setTimeout(() => this.find(target, tries + 1), 100);
      return;
    }
    element?.scrollIntoView({ block: 'center', behavior: 'smooth' });
    // Measured after the scroll settles.
    this.measure();
    this.timer = setTimeout(() => this.measure(), 350);
  }

  /** The first visible element the selector matches. */
  private element(target: string | null): HTMLElement | null {
    if (!target) return null;
    return (
      [...document.querySelectorAll<HTMLElement>(target)].find((e) => {
        const rect = e.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
      }) ?? null
    );
  }

  private text(text: TourText | undefined): string {
    return text ? (this.i18n.language() === 'es' ? text.es : text.en) : '';
  }
}

function toBox(rect: DOMRect): Box {
  return { top: rect.top, left: rect.left, width: rect.width, height: rect.height };
}
