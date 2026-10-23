import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, effect, inject, input, output, signal } from '@angular/core';
import { BrnDialogContent } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { firstValueFrom } from 'rxjs';
import { ApiError } from '../../core/api/rest';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';

/** What an extension still needs before it works (`AdminPage.setup` of its settings page). */
export interface ExtensionSetup {
  title: string;
  description: string;
  items: string[];
  action: string;
  actionLabel: string;
}

/** Reads what an extension still needs; null when nothing (or it has no settings page). */
export async function extensionSetup(
  http: HttpClient,
  id: string,
  language: string,
): Promise<ExtensionSetup | null> {
  try {
    const page = await firstValueFrom(
      http.get<{ setup?: ExtensionSetup | null }>(
        `/rest/v1/extensions/${encodeURIComponent(id)}/admin`,
        {
          params: new HttpParams().set('language', language),
        },
      ),
    );
    return page.setup ?? null;
  } catch {
    return null;
  }
}

/** Runs it; the result's message (or error) is shown. Returns whether it went well. */
export async function runExtensionSetup(
  http: HttpClient,
  i18n: I18n,
  id: string,
  setup: ExtensionSetup,
): Promise<boolean> {
  try {
    const result = await firstValueFrom(
      http.post<{ ok: boolean; message?: string | null }>(
        `/rest/v1/extensions/${encodeURIComponent(id)}/admin/actions/${encodeURIComponent(setup.action)}`,
        { values: {} },
        { params: new HttpParams().set('language', i18n.language()) },
      ),
    );
    if (result.ok) toast.success(result.message || i18n.t('extensions.setup.done'));
    else toast.error(result.message || i18n.t('extensions.setup.failed'));
    return result.ok;
  } catch (error) {
    toast.error(i18n.error(ApiError.from(error).code));
    return false;
  }
}

/**
 * Right after an extension is installed and starts: what it still needs (tools to download…), installed there with
 * the administrator's consent instead of in its settings page. Shows nothing when the extension needs nothing.
 */
@Component({
  selector: 'app-extension-setup',
  imports: [BrnDialogContent, HlmButtonImports, HlmDialogImports, HlmSpinnerImports, TranslatePipe],
  template: `
    <hlm-dialog [state]="setup() ? 'open' : 'closed'" (closed)="close()">
      <hlm-dialog-content
        *brnDialogContent="let ctx"
        class="w-[min(32rem,calc(100vw-2rem))] sm:max-w-lg"
      >
        @if (setup(); as needed) {
          <hlm-dialog-header>
            <h3 hlmDialogTitle>{{ needed.title }}</h3>
            <p hlmDialogDescription>{{ needed.description }}</p>
          </hlm-dialog-header>
          @if (needed.items.length > 0) {
            <ul class="list-disc space-y-1 pl-5 text-sm">
              @for (item of needed.items; track $index) {
                <li>{{ item }}</li>
              }
            </ul>
          }
          <p class="text-xs text-muted-foreground">{{ 'extensions.setup.consent' | t }}</p>
          <hlm-dialog-footer>
            <button hlmBtn variant="outline" type="button" [disabled]="running()" (click)="close()">
              {{ 'extensions.setup.later' | t }}
            </button>
            <button hlmBtn type="button" [disabled]="running()" (click)="run(needed)">
              @if (running()) {
                <hlm-spinner class="size-4" />
              }
              {{ needed.actionLabel }}
            </button>
          </hlm-dialog-footer>
        }
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class ExtensionSetupDialog {
  private readonly http = inject(HttpClient);
  private readonly i18n = inject(I18n);

  /** The extensions to ask about, one after another. */
  readonly ids = input<string[]>([]);
  readonly finished = output<void>();

  protected readonly setup = signal<ExtensionSetup | null>(null);
  protected readonly running = signal(false);
  private queue: string[] = [];
  private current: string | null = null;

  constructor() {
    effect(() => {
      const ids = this.ids();
      if (ids.length === 0) return;
      this.queue = [...ids];
      void this.next();
    });
  }

  private async next(): Promise<void> {
    this.setup.set(null);
    while (this.queue.length > 0) {
      const id = this.queue.shift()!;
      const needed = await extensionSetup(this.http, id, this.i18n.language());
      if (needed) {
        this.current = id;
        this.setup.set(needed);
        return;
      }
    }
    this.current = null;
    this.finished.emit();
  }

  protected async run(needed: ExtensionSetup): Promise<void> {
    if (!this.current) return;
    this.running.set(true);
    await runExtensionSetup(this.http, this.i18n, this.current, needed);
    this.running.set(false);
    await this.next();
  }

  protected close(): void {
    if (this.running() || !this.setup()) return;
    void this.next();
  }
}
