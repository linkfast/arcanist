import { Component, input } from '@angular/core';

export interface LegalSection {
  id: string;
  title: string;
  paragraphs: string[];
}

/** The terms and conditions and the disclaimer (`GET /rest/v1/legal/terms`). */
export interface LegalTerms {
  version: string;
  mode: 'SELF_HOSTED' | 'LINKFAST_SERVICE';
  language: string;
  title: string;
  sections: LegalSection[];
  disclaimerTitle: string;
  disclaimer: LegalSection[];
  accept: string;
  issuesUrl: string;
}

/** Splits a paragraph around a link, so the issues address can be clicked. */
function pieces(text: string, url: string): { text: string; link: boolean }[] {
  const index = text.indexOf(url);
  if (!url || index < 0) return [{ text, link: false }];
  return [
    { text: text.slice(0, index), link: false },
    { text: url, link: true },
    { text: text.slice(index + url.length), link: false },
  ];
}

/** The terms and the disclaimer, as text in a scrolling box (never HTML from the server). */
@Component({
  selector: 'app-legal-terms',
  host: { class: 'block' },
  template: `
    @let t = terms();
    <div class="grid max-h-80 gap-4 overflow-y-auto rounded-lg border bg-muted/30 p-4 text-sm">
      <section class="grid gap-3">
        <h2 class="font-semibold">{{ t.title }}</h2>
        @for (section of t.sections; track section.id) {
          <div class="grid gap-1">
            <h3 class="font-medium">{{ section.title }}</h3>
            @for (paragraph of section.paragraphs; track $index) {
              <p class="text-muted-foreground">
                @for (piece of split(paragraph); track $index) {
                  @if (piece.link) {
                    <a
                      class="break-all underline"
                      [href]="piece.text"
                      target="_blank"
                      rel="noopener noreferrer"
                      >{{ piece.text }}</a
                    >
                  } @else {
                    {{ piece.text }}
                  }
                }
              </p>
            }
          </div>
        }
      </section>
      <section class="grid gap-3 border-t pt-4">
        <h2 class="font-semibold">{{ t.disclaimerTitle }}</h2>
        @for (section of t.disclaimer; track section.id) {
          <div class="grid gap-1">
            <h3 class="font-medium">{{ section.title }}</h3>
            @for (paragraph of section.paragraphs; track $index) {
              <p class="text-muted-foreground">{{ paragraph }}</p>
            }
          </div>
        }
      </section>
    </div>
  `,
})
export class LegalTermsView {
  readonly terms = input.required<LegalTerms>();

  protected split(paragraph: string): { text: string; link: boolean }[] {
    return pieces(paragraph, this.terms().issuesUrl);
  }
}
