import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowUp, lucideFolder, lucideHardDrive } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ApiError, ServerDirectoryListing } from '../core/api/rest';
import { I18n, TranslatePipe } from '../core/i18n/i18n';

export type DirectoryLoader = (path: string | null) => Promise<ServerDirectoryListing>;

/** Browses folders on the server so locations can be picked instead of typed. Place it inside a dialog. */
@Component({
  selector: 'app-folder-picker',
  imports: [
    FormsModule,
    NgIcon,
    HlmButtonImports,
    HlmInputImports,
    HlmCheckboxImports,
    HlmLabelImports,
    HlmSpinnerImports,
    TranslatePipe,
  ],
  providers: [provideIcons({ lucideArrowUp, lucideFolder, lucideHardDrive })],
  host: { class: 'block w-full min-w-0' },
  template: `
    <div class="grid gap-3">
      <form class="flex gap-2" (ngSubmit)="open(typedPath)">
        <input
          hlmInput
          class="flex-1 font-mono text-xs"
          name="path"
          [(ngModel)]="typedPath"
          [placeholder]="'folderPicker.typePath' | t"
          [attr.aria-label]="'folderPicker.typePath' | t"
        />
        <button hlmBtn variant="outline" type="submit">{{ 'folderPicker.go' | t }}</button>
      </form>

      <div class="flex items-center justify-between gap-2">
        <button
          hlmBtn
          variant="ghost"
          size="sm"
          type="button"
          class="gap-1"
          [disabled]="listing()?.path === null || busy()"
          (click)="open(listing()?.parent ?? null)"
        >
          <ng-icon name="lucideArrowUp" />
          {{ 'folderPicker.up' | t }}
        </button>
        <label hlmLabel class="flex items-center gap-2 text-xs">
          <hlm-checkbox [checked]="showHidden()" (checkedChange)="showHidden.set($event)" />
          {{ 'folderPicker.showHidden' | t }}
        </label>
      </div>

      <div class="h-72 overflow-y-auto rounded-md border">
        @if (busy()) {
          <div class="flex h-full items-center justify-center"><hlm-spinner /></div>
        } @else if (error()) {
          <p class="p-4 text-sm text-destructive">{{ error() }}</p>
        } @else {
          @for (directory of directories(); track directory.path) {
            <button
              type="button"
              class="flex w-full items-center gap-2 px-3 py-2 text-left text-sm hover:bg-accent"
              (click)="open(directory.path)"
            >
              <ng-icon
                [name]="listing()?.path === null ? 'lucideHardDrive' : 'lucideFolder'"
                class="shrink-0 text-muted-foreground"
              />
              <span class="truncate">{{ directory.name }}</span>
            </button>
          } @empty {
            <p class="p-4 text-sm text-muted-foreground">{{ 'folderPicker.empty' | t }}</p>
          }
        }
      </div>

      <p class="truncate font-mono text-xs text-muted-foreground" [title]="listing()?.path ?? ''">
        {{ listing()?.path ?? ('folderPicker.roots' | t) }}
      </p>

      <div class="flex justify-end gap-2">
        <button hlmBtn variant="outline" type="button" (click)="cancelled.emit()">
          {{ 'common.cancel' | t }}
        </button>
        <button
          hlmBtn
          type="button"
          [disabled]="!listing()?.path"
          (click)="picked.emit(listing()!.path!)"
        >
          {{ 'folderPicker.select' | t }}
        </button>
      </div>
    </div>
  `,
})
export class FolderPicker implements OnInit {
  private readonly i18n = inject(I18n);

  readonly load = input.required<DirectoryLoader>();
  readonly start = input<string | null>(null);
  readonly picked = output<string>();
  readonly cancelled = output<void>();

  protected readonly listing = signal<ServerDirectoryListing | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly showHidden = signal(false);
  protected typedPath = '';

  protected readonly directories = computed(() =>
    (this.listing()?.directories ?? []).filter((d) => this.showHidden() || !d.isHidden),
  );

  ngOnInit(): void {
    void this.open(this.start());
  }

  protected async open(path: string | null): Promise<void> {
    if (path !== null && !path.trim() && this.listing() !== null) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      const listing = await this.load()(path?.trim() || null);
      this.listing.set(listing);
      this.typedPath = listing.path ?? '';
    } catch (error) {
      this.error.set(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }
}
