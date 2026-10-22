import { Component, input } from '@angular/core';

/**
 * Arcanist's mark: the logo and the ARCANIST wordmark, drawn as Ultimate Desktop draws ULTIMATE (straight segments,
 * outlined: no font; see packaging/branding/arcanist-wordmark.py), in the brand's violet, with an edition label under
 * it as Ultimate's plans are shown.
 */
@Component({
  selector: 'app-brand',
  template: `
    <span class="flex items-center gap-2.5">
      <img
        src="arcanist.png"
        alt=""
        [class]="compact() ? 'size-7 rounded-md' : 'size-9 rounded-lg'"
      />
      <span class="grid gap-1.5">
        <svg
          viewBox="7 -70 665.6 70"
          role="img"
          aria-label="Arcanist"
          [class]="(compact() ? 'h-3' : 'h-3.5') + ' w-auto text-[#7C3AED] dark:text-[#8E51FF]'"
        >
          <path
            fill="currentColor"
            fill-rule="evenodd"
            d="M7 0L32.40 -70L43.40 -70L68.70 0L56.90 0L50.90 -16.60L24.80 -16.60L18.80 0Z M27.60 -26.30L48.20 -26.30L37.90 -55.90L37.70 -55.90Z M104.70 0L104.70 -70L145 -70L156.70 -58.30L156.70 -42L148.40 -33.70L156.70 0L144.60 0L136.20 -30.30L116.30 -30.30L116.30 0Z M116.30 -60.10L139.40 -60.10L145.10 -54.40L145.10 -45.90L139.40 -40.20L116.30 -40.20Z M248.70 0L208.40 0L196.70 -11.70L196.70 -58.30L208.40 -70L248.70 -70L248.70 -60.10L214 -60.10L208.30 -54.40L208.30 -15.60L214 -9.90L248.70 -9.90Z M282.70 0L308.10 -70L319.10 -70L344.40 0L332.60 0L326.60 -16.60L300.50 -16.60L294.50 0Z M303.30 -26.30L323.90 -26.30L313.60 -55.90L313.40 -55.90Z M382.40 0L382.40 -70L393.50 -70L426.10 -19.50L426.30 -19.50L426.30 -70L437.40 -70L437.40 0L426.30 0L393.70 -50.50L393.50 -50.50L393.50 0Z M481.40 0L481.40 -70L493 -70L493 0Z M585 -70L544.70 -70L533 -58.30L533 -41.80L544.70 -30.30L567.70 -30.30L573.40 -24.60L573.40 -15.60L567.70 -9.90L533 -9.90L533 0L573.30 0L585 -11.70L585 -28.40L573.40 -40.10L550.30 -40.10L544.60 -45.80L544.60 -54.40L550.30 -60.10L585 -60.10Z M640 0L640 -60.10L619 -60.10L619 -70L672.60 -70L672.60 -60.10L651.60 -60.10L651.60 0Z"
          />
        </svg>
        @if (edition()) {
          <span
            class="text-[9px] leading-none font-semibold tracking-[0.4em] text-muted-foreground uppercase"
            >{{ edition() }}</span
          >
        }
      </span>
    </span>
  `,
})
export class Brand {
  /** Under the wordmark, in Ultimate's edition style (ADMIN, SETUP…). */
  readonly edition = input<string | null>(null);
  /** Smaller, for a top bar. */
  readonly compact = input(false);
}
