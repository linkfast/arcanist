import { HttpClient, HttpEventType } from '@angular/common/http';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideRefreshCw, lucideUpload } from '@ng-icons/lucide';
import { BrnDialogContent } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmProgressImports } from '@spartan-ng/helm/progress';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ApiError } from '../../core/api/rest';
import { AuthService } from '../../core/auth/auth.service';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  MyUploadStatus,
  UploadLogQuery as UploadLogResult,
  UploadOrigin,
} from '../../graphql/generated/graphql';
import {
  SetUploadSettingsMutation,
  UploadLibrariesQuery,
  UploadLogQuery,
  UploadTargetsQuery,
} from '../../graphql/operations';
import { BytesPipe, RelativeTimePipe } from '../../shared/format';

type Entry = UploadLogResult['uploads']['items'][number];

/** A file the administrator is sending from the panel. */
interface Sending {
  file: File;
  percent: number;
  state: 'waiting' | 'sending' | 'imported' | 'duplicate' | 'failed';
  error?: string;
}

const Origins: UploadOrigin[] = ['PANEL', 'DESKTOP', 'WEB', 'MOBILE', 'EXTENSION', 'API'];
const Statuses: MyUploadStatus[] = ['PROCESSING', 'ADDED', 'DUPLICATE', 'NOT_ADDED', 'FAILED'];
/** How often the list is read again: often while something waits for its scan. */
const ActiveMs = 3000;
const IdleMs = 30000;

/**
 * Every upload into the libraries, whoever sent it and however: the admin panel, Ultimate Desktop, web players,
 * phones and extensions all go through the same API, which records them. And sending music from here, through it
 * too (client=PANEL).
 */
@Component({
  selector: 'app-uploads-card',
  imports: [
    FormsModule,
    NgIcon,
    BrnDialogContent,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmDialogImports,
    HlmNativeSelectImports,
    HlmProgressImports,
    HlmSpinnerImports,
    TranslatePipe,
    BytesPipe,
    RelativeTimePipe,
  ],
  providers: [provideIcons({ lucideRefreshCw, lucideUpload })],
  template: `
    <section hlmCard>
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'import.uploads.title' | t }}</h2>
        <p hlmCardDescription>{{ 'import.uploads.description' | t }}</p>
        <div hlmCardAction class="flex gap-1">
          <button
            hlmBtn
            variant="ghost"
            size="icon"
            type="button"
            [attr.aria-label]="'import.uploads.refresh' | t"
            (click)="load()"
          >
            <ng-icon name="lucideRefreshCw" />
          </button>
          @if (auth.can('UPLOAD')) {
            <button hlmBtn size="sm" type="button" class="gap-1" (click)="openSend()">
              <ng-icon name="lucideUpload" />
              {{ 'import.uploads.send' | t }}
            </button>
          }
        </div>
      </div>
      <div hlmCardContent class="grid gap-3">
        @if (auth.can('MANAGE_LIBRARIES') && writable().length > 0) {
          <label class="flex flex-wrap items-center gap-2 text-sm">
            <span>{{ 'import.uploads.defaultLibrary' | t }}</span>
            <select
              hlmNativeSelect
              class="h-8 text-xs"
              [ngModel]="defaultLibrary() ?? ''"
              (ngModelChange)="setDefaultLibrary($event || null)"
            >
              <option value="">{{ 'import.uploads.noDefault' | t }}</option>
              @for (option of writable(); track option.id) {
                <option [value]="option.id">{{ option.name }}</option>
              }
            </select>
            <span class="w-full text-xs text-muted-foreground">{{
              'import.uploads.defaultHelp' | t
            }}</span>
          </label>
        }
        <div class="flex flex-wrap gap-2">
          <select
            hlmNativeSelect
            class="h-8 text-xs"
            [attr.aria-label]="'import.uploads.origin' | t"
            [ngModel]="origin() ?? ''"
            (ngModelChange)="origin.set($event || null); load()"
          >
            <option value="">{{ 'import.uploads.anyOrigin' | t }}</option>
            @for (option of origins; track option) {
              <option [value]="option">{{ 'import.uploads.origins.' + option | t }}</option>
            }
          </select>
          <select
            hlmNativeSelect
            class="h-8 text-xs"
            [attr.aria-label]="'import.uploads.status' | t"
            [ngModel]="status() ?? ''"
            (ngModelChange)="status.set($event || null); load()"
          >
            <option value="">{{ 'import.uploads.anyStatus' | t }}</option>
            @for (option of statuses; track option) {
              <option [value]="option">{{ 'import.uploads.statuses.' + option | t }}</option>
            }
          </select>
          @if (libraries().length > 1) {
            <select
              hlmNativeSelect
              class="h-8 text-xs"
              [attr.aria-label]="'import.uploads.library' | t"
              [ngModel]="library() ?? ''"
              (ngModelChange)="library.set($event || null); load()"
            >
              <option value="">{{ 'import.uploads.anyLibrary' | t }}</option>
              @for (option of libraries(); track option.id) {
                <option [value]="option.id">{{ option.name }}</option>
              }
            </select>
          }
          @if (page(); as current) {
            <span class="self-center text-xs text-muted-foreground">
              {{ 'import.uploads.count' | t: { count: current.totalCount } }}
              @if (current.active) {
                · {{ 'import.uploads.active' | t: { count: current.active } }}
              }
            </span>
          }
        </div>

        @if (items(); as list) {
          <ul class="grid divide-y rounded-md border">
            @for (entry of list; track entry.id) {
              <li
                class="flex flex-wrap items-start justify-between gap-x-4 gap-y-1 px-3 py-2 text-sm"
              >
                <div class="min-w-0">
                  <p class="truncate font-medium" [title]="entry.path ?? entry.name">
                    {{ entry.track?.title ?? entry.name }}
                    @if (entry.track?.artists?.length) {
                      <span class="font-normal text-muted-foreground">
                        · {{ entry.track!.artists[0].name }}</span
                      >
                    }
                  </p>
                  <p class="truncate text-xs text-muted-foreground">
                    {{ entry.userName }}
                    @if (entry.libraryName) {
                      → {{ entry.libraryName }}
                    }
                    · {{ entry.createdAt | relativeTime }}
                    @if (entry.size) {
                      · {{ entry.size | bytes }}
                    }
                    @if (entry.track && entry.name !== entry.track.title) {
                      · {{ entry.name }}
                    }
                  </p>
                  @if (entry.errorCode) {
                    <p class="text-xs text-destructive">{{ i18n.error(entry.errorCode) }}</p>
                  }
                </div>
                <div class="flex shrink-0 flex-wrap items-center gap-1">
                  <span hlmBadge variant="outline">
                    {{
                      entry.origin === 'EXTENSION' && entry.source
                        ? entry.source
                        : ('import.uploads.origins.' + entry.origin | t)
                    }}
                  </span>
                  <span hlmBadge [variant]="statusVariant(entry.status)">
                    {{ 'import.uploads.statuses.' + entry.status | t }}
                  </span>
                </div>
              </li>
            } @empty {
              <li class="px-3 py-6 text-center text-sm text-muted-foreground">
                {{ 'import.uploads.none' | t }}
              </li>
            }
          </ul>
          @if (page()?.nextCursor) {
            <button
              hlmBtn
              variant="outline"
              size="sm"
              type="button"
              class="justify-self-center"
              [disabled]="loading()"
              (click)="more()"
            >
              {{ 'import.uploads.more' | t }}
            </button>
          }
        } @else {
          <div class="flex justify-center py-6"><hlm-spinner /></div>
        }
      </div>
    </section>

    <hlm-dialog [state]="sendOpen() ? 'open' : 'closed'" (closed)="closeSend()">
      <hlm-dialog-content *brnDialogContent="let ctx" class="sm:max-w-lg">
        <hlm-dialog-header>
          <h3 hlmDialogTitle>{{ 'import.uploads.send' | t }}</h3>
          <p hlmDialogDescription>{{ 'import.uploads.sendHelp' | t }}</p>
        </hlm-dialog-header>
        <div class="grid gap-3 text-sm">
          <label class="grid gap-1">
            {{ 'import.uploads.library' | t }}
            <select
              hlmNativeSelect
              [ngModel]="target()"
              (ngModelChange)="target.set($event)"
              [disabled]="sending()"
            >
              @for (option of writable(); track option.id) {
                <option [value]="option.id">{{ option.name }}</option>
              }
            </select>
          </label>
          @if (writable().length === 0) {
            <p class="text-xs text-muted-foreground">{{ 'import.uploads.noWritable' | t }}</p>
          }
          <label
            class="grid cursor-pointer place-items-center gap-1 rounded-lg border border-dashed p-6 text-center"
            [class.bg-muted]="dragging()"
            (dragover)="$event.preventDefault(); dragging.set(true)"
            (dragleave)="dragging.set(false)"
            (drop)="dropped($event)"
          >
            <ng-icon name="lucideUpload" size="20" />
            <span>{{ 'import.uploads.choose' | t }}</span>
            <span class="text-xs text-muted-foreground">{{ 'import.uploads.formats' | t }}</span>
            <input
              type="file"
              multiple
              accept="audio/*,.flac,.mp3,.m4a,.ogg,.opus,.wav,.aiff,.ape,.wv,.dsf"
              class="sr-only"
              [disabled]="sending()"
              (change)="chosen($any($event.target).files); $any($event.target).value = ''"
            />
          </label>
          @if (queue().length > 0) {
            <ul class="grid max-h-64 gap-1 overflow-y-auto">
              @for (item of queue(); track $index) {
                <li class="grid gap-1 rounded-md border px-3 py-1.5">
                  <div class="flex items-center justify-between gap-2">
                    <span class="truncate text-xs">{{ item.file.name }}</span>
                    <span class="shrink-0 text-xs text-muted-foreground">{{
                      item.state === 'failed' && item.error
                        ? i18n.error(item.error)
                        : ('import.uploads.sending.' + item.state | t)
                    }}</span>
                  </div>
                  @if (item.state === 'sending') {
                    <hlm-progress [value]="item.percent"><hlm-progress-indicator /></hlm-progress>
                  }
                </li>
              }
            </ul>
          }
        </div>
        <hlm-dialog-footer>
          <button hlmBtn variant="outline" type="button" (click)="closeSend()">
            {{ (sending() ? 'import.uploads.background' : 'common.close') | t }}
          </button>
          <button
            hlmBtn
            type="button"
            [disabled]="sending() || !target() || waiting() === 0"
            (click)="send()"
          >
            {{ 'import.uploads.start' | t: { count: waiting() } }}
          </button>
        </hlm-dialog-footer>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class UploadsCard implements OnInit, OnDestroy {
  protected readonly auth = inject(AuthService);
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);
  private readonly http = inject(HttpClient);

  protected readonly origins = Origins;
  protected readonly statuses = Statuses;
  protected readonly page = signal<UploadLogResult['uploads'] | null>(null);
  protected readonly items = signal<Entry[] | null>(null);
  protected readonly loading = signal(false);
  protected readonly origin = signal<UploadOrigin | null>(null);
  protected readonly status = signal<MyUploadStatus | null>(null);
  protected readonly library = signal<string | null>(null);
  protected readonly libraries = signal<{ id: string; name: string; canUpload: boolean }[]>([]);
  protected readonly writable = computed(() => this.libraries().filter((l) => l.canUpload));

  protected readonly sendOpen = signal(false);
  protected readonly target = signal<string | null>(null);
  protected readonly queue = signal<Sending[]>([]);
  protected readonly sending = signal(false);
  protected readonly dragging = signal(false);
  protected readonly waiting = computed(
    () => this.queue().filter((i) => i.state === 'waiting').length,
  );

  private timer: ReturnType<typeof setTimeout> | undefined;
  private destroyed = false;

  /** Where uploads that name no library go (and wishes with no target of their own). */
  protected readonly defaultLibrary = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    const result = await this.gql.query(UploadLibrariesQuery);
    this.libraries.set(result.libraries.filter((l) => !l.virtual));
    try {
      this.defaultLibrary.set(
        (await this.gql.query(UploadTargetsQuery)).uploadSettings.defaultLibraryId ?? null,
      );
    } catch {
      // Shown as none.
    }
    await this.load();
  }

  protected async setDefaultLibrary(id: string | null): Promise<void> {
    try {
      const { setUploadSettings } = await this.gql.mutate(SetUploadSettingsMutation, {
        input: { defaultLibraryId: id },
      });
      this.defaultLibrary.set(setUploadSettings.defaultLibraryId ?? null);
      toast.success(this.i18n.t('import.uploads.defaultSaved'));
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    }
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    clearTimeout(this.timer);
  }

  /** Reads the first page again (with the filters), and again later: sooner while uploads wait for their scan. */
  async load(): Promise<void> {
    clearTimeout(this.timer);
    this.loading.set(true);
    try {
      const result = (await this.gql.query(UploadLogQuery, { input: this.filters() })).uploads;
      this.page.set(result);
      this.items.set(result.items);
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.loading.set(false);
    }

    if (!this.destroyed)
      this.timer = setTimeout(
        () => void this.load(),
        this.page()?.active || this.sending() ? ActiveMs : IdleMs,
      );
  }

  protected async more(): Promise<void> {
    const cursor = this.page()?.nextCursor;
    if (!cursor) return;
    this.loading.set(true);
    try {
      const result = (
        await this.gql.query(UploadLogQuery, { input: { ...this.filters(), after: cursor } })
      ).uploads;
      this.page.set({ ...result, items: result.items });
      this.items.update((items) => [...(items ?? []), ...result.items]);
    } finally {
      this.loading.set(false);
    }
  }

  private filters() {
    return {
      origin: this.origin(),
      status: this.status(),
      libraryId: this.library(),
      first: 50,
    };
  }

  protected statusVariant(
    status: MyUploadStatus,
  ): 'default' | 'secondary' | 'destructive' | 'outline' {
    return status === 'ADDED'
      ? 'default'
      : status === 'FAILED' || status === 'NOT_ADDED'
        ? 'destructive'
        : 'secondary';
  }

  protected openSend(): void {
    if (!this.target()) this.target.set(this.writable()[0]?.id ?? null);
    this.sendOpen.set(true);
  }

  /** Closing while sending leaves it going: the list shows how it ends. */
  protected closeSend(): void {
    this.sendOpen.set(false);
    if (!this.sending()) this.queue.set([]);
  }

  protected chosen(files: FileList | null): void {
    if (!files) return;
    this.queue.update((queue) => [
      ...queue.filter((i) => i.state !== 'imported' && i.state !== 'duplicate'),
      ...Array.from(files).map((file) => ({ file, percent: 0, state: 'waiting' as const })),
    ]);
  }

  protected dropped(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    if (!this.sending()) this.chosen(event.dataTransfer?.files ?? null);
  }

  /** Sends the waiting files one at a time, through the same API every client uses. */
  protected async send(): Promise<void> {
    const library = this.target();
    if (!library) return;
    this.sending.set(true);
    try {
      for (let index = 0; index < this.queue().length; index++) {
        const item = this.queue()[index];
        if (item.state !== 'waiting') continue;
        this.update(index, { state: 'sending', percent: 0 });
        try {
          const status = await this.upload(library, item.file, (percent) =>
            this.update(index, { percent }),
          );
          this.update(index, {
            state: status === 'DUPLICATE' ? 'duplicate' : 'imported',
            percent: 100,
          });
        } catch (error) {
          const apiError = ApiError.from(error);
          this.update(index, {
            state: 'failed',
            error: Object.values(apiError.fields)[0]?.[0] ?? apiError.code,
          });
        }
      }
    } finally {
      this.sending.set(false);
      await this.load();
    }
  }

  private update(index: number, change: Partial<Sending>): void {
    this.queue.update((queue) =>
      queue.map((item, i) => (i === index ? { ...item, ...change } : item)),
    );
  }

  private upload(
    library: string,
    file: File,
    progress: (percent: number) => void,
  ): Promise<string> {
    const url =
      `/rest/v1/libraries/${encodeURIComponent(library)}/uploads` +
      `?fileName=${encodeURIComponent(file.name)}&client=PANEL`;
    return new Promise((resolve, reject) => {
      this.http
        .post<{ status: string }>(url, file, {
          headers: { 'Content-Type': 'application/octet-stream' },
          reportProgress: true,
          observe: 'events',
        })
        .subscribe({
          next: (event) => {
            if (event.type === HttpEventType.UploadProgress && event.total)
              progress(Math.round((event.loaded / event.total) * 100));
            else if (event.type === HttpEventType.Response)
              resolve(event.body?.status ?? 'IMPORTED');
          },
          error: reject,
        });
    });
  }
}
