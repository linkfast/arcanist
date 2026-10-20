import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input } from '@angular/core';

type Part = { text: string; href?: string; strong?: boolean };
type Block = { kind: 'heading' | 'item' | 'paragraph'; parts: Part[] };

/**
 * A release's notes as GitHub writes them (headings, bullets, links, bold), drawn as text: nothing in them becomes
 * markup, so a release cannot put anything else on the page.
 */
@Component({
  selector: 'app-release-notes',
  template: `
    <div class="grid gap-1.5 text-sm">
      @for (block of blocks(); track $index) {
        @switch (block.kind) {
          @case ('heading') {
            <p class="mt-2 font-semibold first:mt-0">
              <ng-container
                *ngTemplateOutlet="parts; context: { $implicit: block.parts }"
              ></ng-container>
            </p>
          }
          @case ('item') {
            <p class="relative pl-4 break-words text-muted-foreground">
              <span class="absolute left-1">•</span>
              <ng-container
                *ngTemplateOutlet="parts; context: { $implicit: block.parts }"
              ></ng-container>
            </p>
          }
          @default {
            <p class="break-words text-muted-foreground">
              <ng-container
                *ngTemplateOutlet="parts; context: { $implicit: block.parts }"
              ></ng-container>
            </p>
          }
        }
      }
    </div>

    <ng-template #parts let-parts>
      @for (part of parts; track $index) {
        @if (part.href) {
          <a class="underline" [href]="part.href" target="_blank" rel="noopener noreferrer">{{
            part.text
          }}</a>
        } @else if (part.strong) {
          <strong class="text-foreground">{{ part.text }}</strong>
        } @else {
          <!-- An element of its own, so no space of this template ends up in the text. -->
          <span>{{ part.text }}</span>
        }
      }
    </ng-template>
  `,
  imports: [NgTemplateOutlet],
})
export class ReleaseNotes {
  readonly markdown = input('');

  protected readonly blocks = computed(() => parse(this.markdown()));
}

/** Headings, bullets and paragraphs; within them, [links](https://…), bare URLs and **bold**. */
export function parse(markdown: string): Block[] {
  const blocks: Block[] = [];
  for (const raw of markdown.replace(/\r/g, '').split('\n')) {
    const line = raw.trim();
    if (!line || /^<!--.*-->$/.test(line)) continue;
    const heading = /^#{1,6}\s+(.*)$/.exec(line);
    const item = /^[*-]\s+(.*)$/.exec(line);
    if (heading) blocks.push({ kind: 'heading', parts: inline(heading[1]) });
    else if (item) blocks.push({ kind: 'item', parts: inline(item[1]) });
    else blocks.push({ kind: 'paragraph', parts: inline(line) });
  }
  return blocks;
}

function inline(text: string): Part[] {
  const parts: Part[] = [];
  const pattern = /\[([^\]]+)\]\((https?:\/\/[^)\s]+)\)|(https?:\/\/[^\s)]+)|\*\*([^*]+)\*\*/g;
  let last = 0;
  for (let match = pattern.exec(text); match; match = pattern.exec(text)) {
    if (match.index > last) parts.push({ text: text.slice(last, match.index) });
    if (match[1]) parts.push({ text: match[1], href: match[2] });
    else if (match[3]) parts.push({ text: shorten(match[3]), href: match[3] });
    else parts.push({ text: match[4], strong: true });
    last = match.index + match[0].length;
  }
  if (last < text.length) parts.push({ text: text.slice(last) });
  return parts;
}

/** GitHub's links to pull requests and comparisons, as GitHub shows them (#12, v0.1.0...v0.2.0). */
function shorten(url: string): string {
  const pull = /\/pull\/(\d+)$/.exec(url);
  if (pull) return `#${pull[1]}`;
  const compare = /\/compare\/(.+)$/.exec(url);
  return compare ? compare[1] : url;
}
