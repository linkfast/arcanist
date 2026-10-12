import { Component, DestroyRef, OnInit, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  LibrariesQuery as LibrariesResult,
  OrganizePreviewQuery as PreviewResult,
  OrganizeRunFieldsFragment as OrganizeRun,
} from '../../graphql/generated/graphql';
import {
  OrganizeLibraryMutation,
  OrganizePreviewQuery,
  OrganizeRunsQuery,
  UndoOrganizeRunMutation,
  UpdateLibraryOrganizationMutation,
} from '../../graphql/operations';
import { CountPipe, RelativeTimePipe } from '../../shared/format';

type Library = LibrariesResult['libraries'][number];
type Preview = PreviewResult['organizePreview'];

/** Placeholders a template can use, shown as a reference. */
const PLACEHOLDERS = [
  'albumartist',
  'artist',
  'album',
  'year',
  'disc',
  'track',
  'title',
  'genre',
].map((name) => `{${name}}`);

/** How often runs in progress are refreshed. */
const REFRESH_MS = 2000;

/**
 * How a library's files are organized: the path template (with a preview of what it would move), automatic
 * organizing of new files, organizing now, and undoing past runs. Shown inside a dialog of the libraries page.
 */
@Component({
  selector: 'app-organize-dialog',
  imports: [
    FormsModule,
    HlmButtonImports,
    HlmBadgeImports,
    HlmDialogImports,
    HlmFieldImports,
    HlmInputImports,
    HlmSpinnerImports,
    HlmSwitchImports,
    TranslatePipe,
    CountPipe,
    RelativeTimePipe,
  ],
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>{{ 'organize.title' | t: { name: library().name } }}</h3>
      <p hlmDialogDescription>{{ 'organize.subtitle' | t }}</p>
    </hlm-dialog-header>

    <form class="grid gap-4" (ngSubmit)="save()">
      <hlm-field>
        <label hlmFieldLabel for="organize-template">{{ 'organize.template' | t }}</label>
        <input
          hlmInput
          id="organize-template"
          name="template"
          class="font-mono text-xs"
          [(ngModel)]="template"
          (ngModelChange)="schedulePreview()"
        />
        <p hlmFieldDescription>
          {{ 'organize.templateHelp' | t }}
          @for (placeholder of placeholders; track placeholder) {
            <code class="mx-0.5 rounded bg-muted px-1 font-mono text-[11px]">{{
              placeholder
            }}</code>
          }
        </p>
        <button
          hlmBtn
          variant="link"
          size="sm"
          type="button"
          class="h-auto self-start p-0"
          (click)="useDefault()"
        >
          {{ 'organize.useDefault' | t }}
        </button>
      </hlm-field>

      <label class="flex items-start gap-2 text-sm">
        <hlm-switch class="mt-0.5" [(checked)]="autoOrganize" />
        <span>
          {{ 'organize.auto' | t }}
          <span class="block text-xs text-muted-foreground">{{ 'organize.autoHelp' | t }}</span>
        </span>
      </label>

      @if (error()) {
        <p class="text-sm text-destructive">{{ error() }}</p>
      }

      <div class="flex justify-end">
        <button hlmBtn size="sm" type="submit" [disabled]="busy()">
          {{ 'common.save' | t }}
        </button>
      </div>
    </form>

    <section class="grid gap-2 rounded-lg border p-3 text-sm">
      <h4 class="font-medium">{{ 'organize.preview' | t }}</h4>
      @if (previewing()) {
        <div class="flex justify-center py-4"><hlm-spinner /></div>
      } @else if (preview(); as plan) {
        <p class="text-muted-foreground">
          {{
            'organize.previewSummary'
              | t
                : {
                    move: (plan.filesToMove | count),
                    inPlace: (plan.filesInPlace | count),
                    others: (plan.otherFilesToMove | count),
                  }
          }}
        </p>
        @if (plan.unavailableSourceIds.length) {
          <p class="text-destructive">{{ 'organize.unavailable' | t }}</p>
        }
        @if (plan.moves.length) {
          <ul class="grid max-h-56 gap-1 overflow-y-auto font-mono text-[11px]">
            @for (move of plan.moves; track move.from) {
              <li class="grid min-w-0 rounded bg-muted px-2 py-1">
                <span class="truncate text-muted-foreground" [title]="move.from">{{
                  move.from
                }}</span>
                <span class="truncate" [title]="move.to">→ {{ move.to }}</span>
              </li>
            }
          </ul>
        }
      }
      <div class="flex flex-wrap items-center justify-between gap-2">
        <p class="text-xs text-muted-foreground">{{ 'organize.nowHelp' | t }}</p>
        <button
          hlmBtn
          size="sm"
          type="button"
          [disabled]="busy() || running() || !preview()?.filesToMove || templateChanged()"
          (click)="organize()"
        >
          {{ 'organize.now' | t }}
        </button>
      </div>
      @if (templateChanged()) {
        <p class="text-xs text-muted-foreground">{{ 'organize.saveFirst' | t }}</p>
      }
    </section>

    @if (runs().length) {
      <section class="grid gap-2 text-sm">
        <h4 class="font-medium">{{ 'organize.history' | t }}</h4>
        <ul class="grid gap-1">
          @for (run of runs(); track run.id) {
            <li class="flex items-center justify-between gap-2 rounded-md bg-muted px-3 py-2">
              <span class="grid min-w-0">
                <span class="flex flex-wrap items-center gap-2">
                  @if (run.status === 'RUNNING' || run.status === 'UNDOING') {
                    <hlm-spinner class="size-3" />
                  }
                  {{ 'organize.status.' + run.status | t }}
                  <span hlmBadge variant="outline">{{ 'organize.kind.' + run.kind | t }}</span>
                </span>
                <span class="text-xs text-muted-foreground">
                  {{ run.startedAt | relativeTime }} ·
                  {{
                    'organize.moved' | t: { moved: (run.moved | count), total: (run.total | count) }
                  }}
                  @if (run.failed) {
                    · {{ 'organize.failed' | t: { count: run.failed } }}
                  }
                  @if (run.errorCode) {
                    · {{ 'errors.' + run.errorCode | t }}
                  }
                </span>
              </span>
              @if ((run.status === 'COMPLETED' || run.status === 'FAILED') && run.moved > 0) {
                <button
                  hlmBtn
                  variant="outline"
                  size="sm"
                  type="button"
                  [disabled]="busy() || running()"
                  (click)="undo(run)"
                >
                  {{ 'organize.undo' | t }}
                </button>
              }
            </li>
          }
        </ul>
      </section>
    }

    <hlm-dialog-footer>
      <button hlmBtn variant="outline" type="button" (click)="closed.emit()">
        {{ 'common.close' | t }}
      </button>
    </hlm-dialog-footer>
  `,
  host: { class: 'grid gap-4' },
})
export class OrganizeDialog implements OnInit {
  readonly library = input.required<Library>();
  readonly closed = output<void>();

  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  protected readonly placeholders = PLACEHOLDERS;
  protected template = '';
  protected autoOrganize = false;
  private savedTemplate = '';

  protected readonly preview = signal<Preview | null>(null);
  protected readonly previewing = signal(false);
  protected readonly runs = signal<OrganizeRun[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly running = signal(false);

  private previewTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    const timer = setInterval(() => {
      if (this.running()) void this.loadRuns();
    }, REFRESH_MS);
    inject(DestroyRef).onDestroy(() => {
      clearInterval(timer);
      clearTimeout(this.previewTimer);
    });
  }

  async ngOnInit(): Promise<void> {
    const organization = this.library().organization;
    this.template = organization?.template ?? '';
    this.savedTemplate = this.template;
    this.autoOrganize = organization?.autoOrganize ?? false;
    await Promise.all([this.loadPreview(), this.loadRuns()]);
  }

  protected templateChanged(): boolean {
    return this.template.trim() !== this.savedTemplate;
  }

  protected useDefault(): void {
    this.template = '';
    void this.loadPreview();
  }

  protected schedulePreview(): void {
    clearTimeout(this.previewTimer);
    this.previewTimer = setTimeout(() => void this.loadPreview(), 400);
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const result = await this.gql.mutate(UpdateLibraryOrganizationMutation, {
        libraryId: this.library().id,
        input: { template: this.template.trim() || null, autoOrganize: this.autoOrganize },
      });
      const organization = result.updateLibraryOrganization.organization;
      this.template = organization?.template ?? '';
      this.savedTemplate = this.template;
      toast.success(this.i18n.t('organize.saved'));
      await this.loadPreview();
    } catch (error) {
      this.error.set(this.describe(ApiError.from(error)));
    } finally {
      this.busy.set(false);
    }
  }

  protected async organize(): Promise<void> {
    await this.act(() =>
      this.gql.mutate(OrganizeLibraryMutation, { libraryId: this.library().id }),
    );
  }

  protected async undo(run: OrganizeRun): Promise<void> {
    await this.act(() => this.gql.mutate(UndoOrganizeRunMutation, { id: run.id }));
  }

  private async act(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
      await this.loadRuns();
    } catch (error) {
      toast.error(this.describe(ApiError.from(error)));
    } finally {
      this.busy.set(false);
    }
  }

  private async loadPreview(): Promise<void> {
    this.previewing.set(true);
    this.error.set(null);
    try {
      const result = await this.gql.query(OrganizePreviewQuery, {
        libraryId: this.library().id,
        template: this.template.trim() || null,
      });
      this.preview.set(result.organizePreview);
    } catch (error) {
      this.preview.set(null);
      this.error.set(this.describe(ApiError.from(error)));
    } finally {
      this.previewing.set(false);
    }
  }

  private async loadRuns(): Promise<void> {
    const wasRunning = this.running();
    const result = await this.gql.query(OrganizeRunsQuery, { libraryId: this.library().id });
    this.runs.set(result.organizeRuns);
    this.running.set(
      result.organizeRuns.some((r) => r.status === 'RUNNING' || r.status === 'UNDOING'),
    );
    // Files moved: what is left to move changed.
    if (wasRunning && !this.running()) await this.loadPreview();
  }

  private describe(error: ApiError): string {
    const fieldCode = Object.values(error.fields)[0]?.[0];
    return this.i18n.error(fieldCode ?? error.code);
  }
}
