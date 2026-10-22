import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowUp, lucideFolder, lucideFolderPlus, lucideHardDrive } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ApiError, ServerDirectory, ServerDirectoryListing } from '../core/api/rest';
import { I18n, TranslatePipe } from '../core/i18n/i18n';
import { HostHelp } from './host-help';

export type DirectoryLoader = (path: string | null) => Promise<ServerDirectoryListing>;

/** Creates a folder named `name` inside `parent`. */
export type DirectoryCreator = (parent: string, name: string) => Promise<ServerDirectory>;

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
    HostHelp,
    TranslatePipe,
  ],
  providers: [provideIcons({ lucideArrowUp, lucideFolder, lucideFolderPlus, lucideHardDrive })],
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
        @if (create()) {
          <button
            hlmBtn
            variant="ghost"
            size="sm"
            type="button"
            class="gap-1"
            [disabled]="!listing()?.path || busy() || naming()"
            (click)="startNaming()"
          >
            <ng-icon name="lucideFolderPlus" />
            {{ 'folderPicker.newFolder' | t }}
          </button>
        }
        <label hlmLabel class="ml-auto flex items-center gap-2 text-xs">
          <hlm-checkbox [checked]="showHidden()" (checkedChange)="showHidden.set($event)" />
          {{ 'folderPicker.showHidden' | t }}
        </label>
      </div>

      @if (naming()) {
        <form class="grid gap-1" (ngSubmit)="createFolder()">
          <div class="flex gap-2">
            <input
              hlmInput
              class="flex-1"
              name="newFolder"
              [(ngModel)]="newName"
              [placeholder]="'folderPicker.newFolderName' | t"
              [attr.aria-label]="'folderPicker.newFolderName' | t"
              autofocus
            />
            <button hlmBtn type="submit" [disabled]="!newName.trim() || busy()">
              {{ 'folderPicker.create' | t }}
            </button>
            <button hlmBtn variant="ghost" type="button" (click)="naming.set(false)">
              {{ 'common.cancel' | t }}
            </button>
          </div>
          @if (createError()) {
            <p class="text-xs text-destructive">{{ createError() }}</p>
          }
        </form>
      }

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

      <details class="rounded-md border px-3 py-2 text-sm">
        <summary class="cursor-pointer text-muted-foreground">
          {{ 'host.folders.title' | t }}
        </summary>
        <app-host-help class="mt-2" topic="folders" />
      </details>

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
  /** Offers to create folders when given. */
  readonly create = input<DirectoryCreator | null>(null);
  readonly start = input<string | null>(null);
  readonly picked = output<string>();
  readonly cancelled = output<void>();

  protected readonly listing = signal<ServerDirectoryListing | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly showHidden = signal(false);
  protected typedPath = '';
  protected readonly naming = signal(false);
  protected readonly createError = signal<string | null>(null);
  protected newName = '';

  protected readonly directories = computed(() =>
    (this.listing()?.directories ?? []).filter((d) => this.showHidden() || !d.isHidden),
  );

  ngOnInit(): void {
    void this.open(this.start());
  }

  protected startNaming(): void {
    this.newName = '';
    this.createError.set(null);
    this.naming.set(true);
  }

  /** Creates the folder in the one shown, then opens it, ready to be used. */
  protected async createFolder(): Promise<void> {
    const parent = this.listing()?.path;
    const create = this.create();
    if (!parent || !create || !this.newName.trim()) return;

    this.busy.set(true);
    this.createError.set(null);
    try {
      const folder = await create(parent, this.newName.trim());
      this.naming.set(false);
      this.busy.set(false);
      await this.open(folder.path);
    } catch (error) {
      const apiError = ApiError.from(error);
      this.createError.set(
        this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code),
      );
      this.busy.set(false);
    }
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
