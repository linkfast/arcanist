import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEllipsis, lucideStar, lucideTrash2, lucideX } from '@ng-icons/lucide';
import { BrnAlertDialogContent } from '@spartan-ng/brain/alert-dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ApiError } from '../../core/api/rest';
import { EnabledExtensions } from '../../core/extensions/enabled-extensions';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type { PlayHistoryQuery as Result } from '../../graphql/generated/graphql';
import {
  AddStarTracksMutation,
  ClearPlayHistoryMutation,
  DeletePlaysMutation,
  DeleteTrackCopiesMutation,
  PlayHistoryQuery,
  SetTrackStarredMutation,
} from '../../graphql/operations';

type Play = Result['playHistory']['items'][number];

/** Something that cannot be undone, waiting for the user to say yes. */
type Pending =
  | { kind: 'selected'; count: number }
  | { kind: 'song'; play: Play }
  | { kind: 'library'; play: Play }
  | { kind: 'clear' };

/** The user's plays by day: they search them, star the songs and take from it what they do not want there. */
@Component({
  selector: 'app-play-history',
  imports: [
    BrnAlertDialogContent,
    CdkMenu,
    CdkMenuItem,
    CdkMenuTrigger,
    FormsModule,
    HlmAlertDialogImports,
    HlmButtonImports,
    HlmCheckboxImports,
    HlmInputImports,
    HlmSpinnerImports,
    NgIcon,
    TranslatePipe,
  ],
  providers: [provideIcons({ lucideEllipsis, lucideStar, lucideTrash2, lucideX })],
  template: `
    <div class="mb-4 flex flex-wrap items-center justify-between gap-2">
      <input
        hlmInput
        type="search"
        class="w-full sm:max-w-xs"
        data-tour="listening-search"
        [attr.aria-label]="'listening.search' | t"
        [placeholder]="'listening.search' | t"
        [ngModel]="search()"
        (ngModelChange)="searchFor($event)"
      />
      <div class="flex flex-wrap items-center gap-2" data-tour="listening-actions">
        @if (total() !== null) {
          <span class="text-sm text-muted-foreground">
            {{
              (total() === 1 ? 'listening.countOne' : 'listening.count') | t: { count: total()! }
            }}
          </span>
        }
        <button
          hlmBtn
          variant="outline"
          size="sm"
          type="button"
          [disabled]="!plays().length || !!search()"
          (click)="pending.set({ kind: 'clear' })"
        >
          {{ 'listening.clear' | t }}
        </button>
      </div>
    </div>

    @if (selected().size) {
      <div
        class="sticky top-0 z-10 mb-3 flex flex-wrap items-center gap-2 rounded-lg border bg-card p-2"
      >
        <span class="px-1 text-sm">
          {{
            (selected().size === 1 ? 'listening.selectedOne' : 'listening.selected')
              | t: { count: selected().size }
          }}
        </span>
        <button hlmBtn size="sm" type="button" [disabled]="busy()" (click)="starSelected()">
          <ng-icon name="lucideStar" /> {{ 'listening.starSelected' | t }}
        </button>
        <button
          hlmBtn
          size="sm"
          variant="outline"
          type="button"
          [disabled]="busy()"
          (click)="pending.set({ kind: 'selected', count: selected().size })"
        >
          {{ 'listening.removeSelected' | t }}
        </button>
        <button hlmBtn size="sm" variant="ghost" type="button" (click)="selected.set(emptySet)">
          {{ 'common.cancel' | t }}
        </button>
      </div>
    }

    @if (error()) {
      <p class="text-sm text-destructive">{{ error() }}</p>
    }

    <div class="grid gap-6">
      @for (day of days(); track day.key) {
        <section class="grid gap-1">
          <h2 class="text-sm font-medium text-muted-foreground">{{ day.label }}</h2>
          <ul class="divide-y rounded-lg border">
            @for (play of day.plays; track play.id) {
              <li class="flex items-center gap-3 p-2">
                <hlm-checkbox
                  [attr.aria-label]="'listening.select' | t: { title: play.track.title }"
                  [checked]="selected().has(play.id)"
                  (checkedChange)="toggle(play.id)"
                />
                @if (play.track.artworkId) {
                  <img
                    class="size-10 shrink-0 rounded object-cover"
                    alt=""
                    loading="lazy"
                    [src]="'/rest/v1/artwork/' + play.track.artworkId + '?size=64'"
                  />
                } @else {
                  <div class="size-10 shrink-0 rounded bg-muted"></div>
                }
                <div class="min-w-0 flex-1">
                  <p class="truncate text-sm font-medium">{{ play.track.title }}</p>
                  <p class="truncate text-xs text-muted-foreground">
                    {{ artists(play) }} · {{ play.track.album.title }}
                  </p>
                </div>
                <div class="hidden shrink-0 text-right text-xs text-muted-foreground sm:block">
                  <p>{{ time(play.playedAt) }}</p>
                  @if (play.client) {
                    <p class="max-w-32 truncate" [title]="play.client">{{ play.client }}</p>
                  }
                </div>
                <button
                  hlmBtn
                  variant="ghost"
                  size="icon"
                  type="button"
                  [class.text-amber-500]="play.track.starred"
                  [class.dark:text-amber-400]="play.track.starred"
                  [attr.aria-pressed]="play.track.starred"
                  [attr.aria-label]="
                    (play.track.starred ? 'listening.unstar' : 'listening.star')
                      | t: { title: play.track.title }
                  "
                  [title]="
                    (play.track.starred ? 'listening.isStarTrack' : 'listening.toStarTrack') | t
                  "
                  (click)="star(play)"
                >
                  <ng-icon
                    name="lucideStar"
                    [style.fill]="play.track.starred ? 'currentColor' : 'none'"
                  />
                </button>
                <button
                  hlmBtn
                  variant="ghost"
                  size="icon"
                  type="button"
                  [attr.aria-label]="'listening.actionsFor' | t: { title: play.track.title }"
                  [cdkMenuTriggerFor]="actions"
                >
                  <ng-icon name="lucideEllipsis" />
                </button>
                <ng-template #actions>
                  <div
                    cdkMenu
                    class="z-50 grid min-w-60 rounded-md border bg-popover p-1 text-sm text-popover-foreground shadow-md"
                  >
                    <button
                      cdkMenuItem
                      [class]="menuItem"
                      type="button"
                      (cdkMenuItemTriggered)="removePlay(play)"
                    >
                      <ng-icon name="lucideX" /> {{ 'listening.removePlay' | t }}
                    </button>
                    <button
                      cdkMenuItem
                      [class]="menuItem"
                      type="button"
                      (cdkMenuItemTriggered)="pending.set({ kind: 'song', play })"
                    >
                      <ng-icon name="lucideX" /> {{ 'listening.removeSong' | t }}
                    </button>
                    @if (play.track.canDelete) {
                      <button
                        cdkMenuItem
                        [class]="menuItem + ' text-destructive'"
                        type="button"
                        (cdkMenuItemTriggered)="pending.set({ kind: 'library', play })"
                      >
                        <ng-icon name="lucideTrash2" /> {{ 'listening.deleteFromLibrary' | t }}
                      </button>
                    }
                  </div>
                </ng-template>
              </li>
            }
          </ul>
        </section>
      } @empty {
        @if (!loading() && !error()) {
          <p class="py-8 text-center text-sm text-muted-foreground">
            {{ (search() ? 'listening.noMatches' : 'listening.empty') | t }}
          </p>
        }
      }
    </div>

    @if (loading()) {
      <div class="flex justify-center py-8"><hlm-spinner /></div>
    } @else if (cursor()) {
      <div class="mt-4 flex justify-center">
        <button hlmBtn variant="outline" type="button" (click)="more()">
          {{ 'listening.more' | t }}
        </button>
      </div>
    }

    <hlm-alert-dialog [state]="pending() ? 'open' : 'closed'" (closed)="pending.set(null)">
      <hlm-alert-dialog-content *brnAlertDialogContent="let ctx">
        <hlm-alert-dialog-header>
          <h3 hlmAlertDialogTitle>{{ confirmTitle() }}</h3>
          <p hlmAlertDialogDescription>{{ confirmText() }}</p>
          @if (pending()?.kind !== 'library' && extensions.has('ultimate-cloud')) {
            <p hlmAlertDialogDescription>{{ 'listening.confirm.cloudNote' | t }}</p>
          }
        </hlm-alert-dialog-header>
        <hlm-alert-dialog-footer>
          <button hlmAlertDialogCancel type="button" (click)="pending.set(null)">
            {{ 'common.cancel' | t }}
          </button>
          <button hlmAlertDialogAction type="button" (click)="confirm()">
            {{ confirmAction() }}
          </button>
        </hlm-alert-dialog-footer>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class PlayHistory implements OnInit, OnDestroy {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);
  protected readonly extensions = inject(EnabledExtensions);

  protected readonly menuItem =
    'flex items-center gap-2 rounded-sm px-2 py-1.5 text-left outline-none hover:bg-accent focus-visible:bg-accent [&_ng-icon]:shrink-0';
  protected readonly emptySet: ReadonlySet<string> = new Set<string>();

  protected readonly search = signal('');
  protected readonly plays = signal<Play[]>([]);
  protected readonly total = signal<number | null>(null);
  protected readonly cursor = signal<string | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly selected = signal<ReadonlySet<string>>(this.emptySet);
  protected readonly pending = signal<Pending | null>(null);

  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  /** Answers to a search the user has already changed are dropped. */
  private generation = 0;

  protected readonly days = computed(() => {
    const language = this.i18n.language();
    const days: { key: string; label: string; plays: Play[] }[] = [];
    for (const play of this.plays()) {
      const date = new Date(play.playedAt);
      const key = `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`;
      if (days.at(-1)?.key !== key)
        days.push({ key, label: this.dayLabel(date, language), plays: [] });
      days.at(-1)!.plays.push(play);
    }
    return days;
  });

  protected readonly confirmTitle = computed(() => this.confirmKey('title'));
  protected readonly confirmText = computed(() => this.confirmKey('text'));
  protected readonly confirmAction = computed(() => this.confirmKey('action'));

  ngOnInit(): Promise<void> {
    return this.load();
  }

  ngOnDestroy(): void {
    clearTimeout(this.searchTimer);
  }

  protected searchFor(text: string): void {
    this.search.set(text);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => void this.load(), 300);
  }

  protected more(): Promise<void> {
    return this.load(this.cursor());
  }

  protected toggle(id: string): void {
    const next = new Set(this.selected());
    if (!next.delete(id)) next.add(id);
    this.selected.set(next);
  }

  protected artists(play: Play): string {
    return play.track.artists
      .filter((a) => a.role === 'PRIMARY')
      .map((a) => a.name)
      .join(', ');
  }

  protected time(at: string): string {
    return new Intl.DateTimeFormat(this.i18n.language(), {
      hour: 'numeric',
      minute: '2-digit',
    }).format(new Date(at));
  }

  /** Every play of the song shows it. Back as it was if the server says no. */
  protected async star(play: Play): Promise<void> {
    const starred = !play.track.starred;
    this.setStarred([play.track.id], starred);
    try {
      await this.gql.mutate(SetTrackStarredMutation, { trackId: play.track.id, starred });
    } catch (error) {
      this.setStarred([play.track.id], !starred);
      this.fail(error);
    }
  }

  protected starSelected(): Promise<void> {
    const ids = [
      ...new Set(
        this.plays()
          .filter((p) => this.selected().has(p.id))
          .map((p) => p.track.id),
      ),
    ];
    return this.run(async () => {
      await this.gql.mutate(AddStarTracksMutation, {
        input: { items: ids.map((trackId) => ({ trackId })) },
      });
      this.setStarred(ids, true);
      this.selected.set(this.emptySet);
      toast.success(this.i18n.t('listening.starred', { count: ids.length }));
    });
  }

  /** One play: a small thing, and the list says at once that it left. */
  protected removePlay(play: Play): Promise<void> {
    return this.run(async () => {
      await this.gql.mutate(DeletePlaysMutation, { input: { playIds: [play.id] } });
      this.drop(new Set([play.id]));
    });
  }

  protected confirm(): Promise<void> {
    const pending = this.pending();
    this.pending.set(null);
    if (!pending) return Promise.resolve();
    return this.run(async () => {
      switch (pending.kind) {
        case 'selected': {
          const ids = new Set(this.selected());
          await this.gql.mutate(DeletePlaysMutation, { input: { playIds: [...ids] } });
          this.drop(ids);
          return;
        }
        case 'song':
          await this.gql.mutate(DeletePlaysMutation, {
            input: { trackIds: [pending.play.track.id] },
          });
          break;
        case 'library':
          await this.gql.mutate(DeleteTrackCopiesMutation, {
            id: pending.play.track.id,
            libraryId: null,
          });
          break;
        case 'clear':
          await this.gql.mutate(ClearPlayHistoryMutation);
          break;
      }
      // More than the pages already here may have gone.
      await this.load();
    });
  }

  private confirmKind(pending: Pending): string {
    return pending.kind === 'selected' && pending.count === 1 ? 'selectedOne' : pending.kind;
  }

  private confirmKey(part: 'title' | 'text' | 'action'): string {
    const pending = this.pending();
    if (!pending) return '';
    const key = `listening.confirm.${this.confirmKind(pending)}.${part}`;
    if (pending.kind === 'selected') return this.i18n.t(key, { count: pending.count });
    if (pending.kind === 'clear') return this.i18n.t(key);
    return this.i18n.t(key, { title: pending.play.track.title });
  }

  private drop(ids: ReadonlySet<string>): void {
    this.plays.update((plays) => plays.filter((p) => !ids.has(p.id)));
    this.total.update((total) => (total === null ? null : Math.max(0, total - ids.size)));
    const left = new Set(this.selected());
    for (const id of ids) left.delete(id);
    this.selected.set(left);
  }

  private setStarred(trackIds: readonly string[], starred: boolean): void {
    this.plays.update((plays) =>
      plays.map((p) =>
        trackIds.includes(p.track.id) ? { ...p, track: { ...p.track, starred } } : p,
      ),
    );
  }

  private dayLabel(date: Date, language: string): string {
    const today = new Date();
    const yesterday = new Date(today.getFullYear(), today.getMonth(), today.getDate() - 1);
    if (date.toDateString() === today.toDateString()) return this.i18n.t('listening.today');
    if (date.toDateString() === yesterday.toDateString()) return this.i18n.t('listening.yesterday');
    return new Intl.DateTimeFormat(language, {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
      year: date.getFullYear() === today.getFullYear() ? undefined : 'numeric',
    }).format(date);
  }

  /** The first page again, or the next one after a cursor. */
  private async load(after: string | null = null): Promise<void> {
    const generation = ++this.generation;
    this.loading.set(true);
    this.error.set(null);
    if (!after) this.selected.set(this.emptySet);
    try {
      const { playHistory } = await this.gql.query(PlayHistoryQuery, {
        after,
        first: 100,
        search: this.search().trim() || null,
      });
      if (generation !== this.generation) return;
      this.plays.update((plays) => (after ? [...plays, ...playHistory.items] : playHistory.items));
      this.cursor.set(playHistory.nextCursor);
      this.total.set(playHistory.totalCount);
    } catch (error) {
      if (generation === this.generation)
        this.error.set(this.i18n.error(ApiError.from(error).code));
    } finally {
      if (generation === this.generation) this.loading.set(false);
    }
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
    } catch (error) {
      this.fail(error);
    } finally {
      this.busy.set(false);
    }
  }

  private fail(error: unknown): void {
    toast.error(this.i18n.error(ApiError.from(error).code));
  }
}
