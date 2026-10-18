import { Component, OnDestroy, OnInit, computed, inject, input } from '@angular/core';
import { DomSanitizer, type SafeResourceUrl } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { EnabledExtensions } from '../../core/extensions/enabled-extensions';
import { ExtensionHost } from '../../core/extensions/extension-host';
import { TranslatePipe } from '../../core/i18n/i18n';

/**
 * The settings page of an installed extension, inside the panel: the page it declares (manifest `adminPage`) in a
 * frame that gets the session and the panel's look with the host protocol.
 */
@Component({
  selector: 'app-extension-page',
  imports: [RouterLink, HlmButtonImports, TranslatePipe],
  host: { class: 'flex h-full min-h-[calc(100vh-4rem)] flex-col gap-4' },
  template: `
    @if (extension(); as current) {
      <header class="flex items-center gap-3">
        @if (current.iconUrl) {
          <img [src]="current.iconUrl" alt="" class="size-9 rounded-lg" />
        }
        <div class="min-w-0">
          <h1 class="truncate text-2xl font-semibold">{{ current.name }}</h1>
          <p class="text-sm text-muted-foreground">
            {{ current.version }}
            @if (current.author) {
              · {{ current.author }}
            }
          </p>
        </div>
      </header>
      <iframe
        #frame
        [src]="url()"
        [title]="current.name"
        class="min-h-[480px] w-full flex-1 rounded-lg border bg-card"
        (load)="adopt(frame)"
      ></iframe>
    } @else if (loaded()) {
      <p class="text-sm text-muted-foreground">{{ 'extensions.page.unavailable' | t }}</p>
      <a hlmBtn variant="outline" size="sm" class="self-start" routerLink="/extensions">{{
        'nav.extensions' | t
      }}</a>
    }
  `,
})
export class ExtensionPage implements OnInit, OnDestroy {
  readonly id = input.required<string>();
  private readonly extensions = inject(EnabledExtensions);
  private readonly host = inject(ExtensionHost);
  private frame: HTMLIFrameElement | null = null;

  protected readonly extension = computed(
    () => this.extensions.pages().find((e) => e.id === this.id()) ?? null,
  );
  protected readonly loaded = computed(() => this.extensions.all().length > 0);
  private readonly sanitizer = inject(DomSanitizer);
  /** Only the extension's own pages (the server checks them at install) are framed. */
  protected readonly url = computed<SafeResourceUrl | null>(() => {
    const url = this.extension()?.adminUrl;
    return url?.startsWith(`/ext/${this.id()}/`)
      ? this.sanitizer.bypassSecurityTrustResourceUrl(url)
      : null;
  });

  async ngOnInit(): Promise<void> {
    if (!this.loaded()) await this.extensions.refresh();
  }

  ngOnDestroy(): void {
    this.host.release(this.frame?.contentWindow ?? null);
  }

  /** The frame's window may talk to the panel (also after it navigates or reloads). */
  protected adopt(frame: HTMLIFrameElement): void {
    this.frame = frame;
    this.host.adopt(frame.contentWindow);
  }
}
