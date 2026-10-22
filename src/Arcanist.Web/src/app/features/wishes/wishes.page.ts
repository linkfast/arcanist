import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule, NgForm } from '@angular/forms';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTabsImports } from '@spartan-ng/helm/tabs';
import { ApiError } from '../../core/api/rest';
import { AuthService } from '../../core/auth/auth.service';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  ResolveWishLinkMutation,
  WishFieldsFragment as Wish,
  WishListScope,
} from '../../graphql/generated/graphql';
import {
  AddWishMutation,
  MarkWishesSeenMutation,
  RemoveWishMutation,
  ResolveWishLinkMutation as ResolveWishLink,
  WishListQuery,
  WishObtainedSubscription,
} from '../../graphql/operations';
import { DateTimePipe, RelativeTimePipe } from '../../shared/format';
import { musicBrainzRecordingUrl, wishServiceName } from './wish-services';

type Preview = ResolveWishLinkMutation['resolveWishLink'];

/**
 * The user's wish list: songs they want, recognised as soon as one enters a library they can play (by MusicBrainz,
 * AcoustID, or title and artist), with the history of those that arrived. A song can start from a streaming service's
 * link (it fills itself in, with its cover) or be typed in. Those with VIEW_ALL_WISHES also see everyone's wishes.
 */
@Component({
  selector: 'app-wishes-page',
  imports: [
    FormsModule,
    NgTemplateOutlet,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmFieldImports,
    HlmInputImports,
    HlmTabsImports,
    TranslatePipe,
    DateTimePipe,
    RelativeTimePipe,
  ],
  template: `
    <div class="mx-auto grid w-full max-w-4xl grid-cols-[minmax(0,1fr)] gap-6">
      <header class="grid gap-1">
        <h1 class="text-2xl font-semibold tracking-tight">{{ 'wishes.title' | t }}</h1>
        <p class="text-sm text-muted-foreground">{{ 'wishes.description' | t }}</p>
      </header>

      @if (canSeeAll) {
        <hlm-tabs [tab]="scope()" (tabActivated)="setScope($any($event))">
          <hlm-tabs-list>
            <button hlmTabsTrigger="MINE">{{ 'wishes.tabs.mine' | t }}</button>
            <button hlmTabsTrigger="ALL">{{ 'wishes.tabs.all' | t }}</button>
          </hlm-tabs-list>
        </hlm-tabs>
      }

      @if (scope() === 'MINE') {
        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'wishes.add' | t }}</h2>
          </div>
          <div hlmCardContent class="grid gap-4">
            <form class="grid gap-2" (ngSubmit)="read()">
              <label hlmFieldLabel for="wish-link">{{ 'wishes.link' | t }}</label>
              <div class="flex flex-wrap gap-2">
                <input
                  hlmInput
                  id="wish-link"
                  name="link"
                  type="url"
                  class="min-w-0 flex-1"
                  [placeholder]="'wishes.linkPlaceholder' | t"
                  [(ngModel)]="link"
                />
                <button
                  hlmBtn
                  variant="secondary"
                  type="submit"
                  [disabled]="reading() || !link.trim()"
                >
                  {{ (reading() ? 'wishes.reading' : 'wishes.read') | t }}
                </button>
              </div>
              <p class="text-xs text-muted-foreground">{{ 'wishes.linkHelp' | t }}</p>
            </form>

            @if (preview(); as found) {
              <div class="flex items-center gap-3 rounded-md border bg-muted/40 p-3 text-sm">
                @if (found.artworkId) {
                  <img
                    class="size-16 shrink-0 rounded object-cover"
                    [src]="'/rest/v1/artwork/' + found.artworkId + '?size=128'"
                    [alt]="'wishes.cover' | t"
                  />
                }
                <div class="grid min-w-0 flex-1 gap-0.5">
                  <p class="truncate font-medium">{{ found.title }}</p>
                  <p class="truncate text-muted-foreground">
                    {{ found.artist }}
                    @if (found.album) {
                      · {{ found.album }}
                    }
                  </p>
                  <p class="text-xs text-muted-foreground">
                    {{ 'wishes.readFrom' | t: { service: serviceName(found.service) } }}
                    <a
                      class="underline"
                      [href]="found.url"
                      target="_blank"
                      rel="noopener noreferrer"
                      >{{ 'wishes.openIn' | t: { service: serviceName(found.service) } }}</a
                    >
                  </p>
                </div>
                <button hlmBtn variant="ghost" size="sm" type="button" (click)="clearLink()">
                  {{ 'wishes.clearLink' | t }}
                </button>
              </div>
            } @else {
              <p class="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                {{ 'wishes.manual' | t }}
              </p>
            }

            <form class="grid gap-3" #wishForm="ngForm" (ngSubmit)="add(wishForm)">
              <div class="grid gap-3 sm:grid-cols-2">
                <hlm-field>
                  <label hlmFieldLabel for="wish-title">{{ 'wishes.songTitle' | t }}</label>
                  <input hlmInput id="wish-title" name="title" required [(ngModel)]="title" />
                </hlm-field>
                <hlm-field>
                  <label hlmFieldLabel for="wish-artist">{{ 'wishes.artist' | t }}</label>
                  <input hlmInput id="wish-artist" name="artist" required [(ngModel)]="artist" />
                </hlm-field>
                <hlm-field>
                  <label hlmFieldLabel for="wish-album">{{ 'wishes.album' | t }}</label>
                  <input
                    hlmInput
                    id="wish-album"
                    name="album"
                    [placeholder]="'wishes.optional' | t"
                    [(ngModel)]="album"
                  />
                </hlm-field>
                <hlm-field>
                  <label hlmFieldLabel for="wish-note">{{ 'wishes.note' | t }}</label>
                  <input
                    hlmInput
                    id="wish-note"
                    name="note"
                    [placeholder]="'wishes.optional' | t"
                    [(ngModel)]="note"
                  />
                </hlm-field>
              </div>
              <details class="text-sm">
                <summary class="cursor-pointer text-muted-foreground">
                  {{ 'wishes.ids' | t }}
                </summary>
                <div class="mt-3 grid gap-3 sm:grid-cols-2">
                  <hlm-field>
                    <label hlmFieldLabel for="wish-mbid">{{ 'wishes.mbid' | t }}</label>
                    <input hlmInput id="wish-mbid" name="mbid" [(ngModel)]="mbid" />
                  </hlm-field>
                  <hlm-field>
                    <label hlmFieldLabel for="wish-acoustid">AcoustID</label>
                    <input hlmInput id="wish-acoustid" name="acoustId" [(ngModel)]="acoustId" />
                  </hlm-field>
                </div>
              </details>
              @if (error()) {
                <p class="text-sm text-destructive">{{ error() }}</p>
              }
              <div>
                <button hlmBtn type="submit" [disabled]="busy() || !title.trim() || !artist.trim()">
                  {{ 'wishes.addButton' | t }}
                </button>
              </div>
            </form>
          </div>
        </section>
      } @else {
        <p class="text-sm text-muted-foreground">{{ 'wishes.allHelp' | t }}</p>
      }

      <section hlmCard>
        <div hlmCardHeader>
          <h2 hlmCardTitle>{{ 'wishes.wanted' | t }} ({{ wanted().length }})</h2>
          <p hlmCardDescription>{{ 'wishes.wantedHelp' | t }}</p>
        </div>
        <ul hlmCardContent class="grid grid-cols-[minmax(0,1fr)] gap-2">
          @for (wish of wanted(); track wish.id) {
            <li class="flex items-center gap-3 rounded-md border p-3 text-sm">
              <ng-container *ngTemplateOutlet="cover; context: { $implicit: wish }" />
              <div class="grid min-w-0 flex-1 gap-0.5">
                <p class="truncate">
                  <span class="font-medium">{{ wish.title }}</span>
                  <span class="text-muted-foreground"> · {{ wish.artist }}</span>
                  @if (wish.album) {
                    <span class="text-muted-foreground"> · {{ wish.album }}</span>
                  }
                </p>
                <p class="text-xs text-muted-foreground" [title]="wish.createdAt | dateTime">
                  @if (wish.owner; as owner) {
                    {{ 'wishes.by_user' | t: { name: owner.displayName || owner.userName } }} ·
                  }
                  {{ 'wishes.since' | t: { when: (wish.createdAt | relativeTime) } }}
                  @if (wish.note) {
                    · {{ wish.note }}
                  }
                </p>
                <ng-container *ngTemplateOutlet="links; context: { $implicit: wish }" />
              </div>
              @if (scope() === 'MINE') {
                <button
                  hlmBtn
                  variant="ghost"
                  size="sm"
                  type="button"
                  [disabled]="busy()"
                  (click)="remove(wish)"
                >
                  {{ 'wishes.remove' | t }}
                </button>
              }
            </li>
          } @empty {
            <li class="text-sm text-muted-foreground">{{ 'wishes.noneWanted' | t }}</li>
          }
        </ul>
      </section>

      <section hlmCard>
        <div hlmCardHeader>
          <h2 hlmCardTitle>{{ 'wishes.obtained' | t }}</h2>
          <p hlmCardDescription>{{ 'wishes.obtainedHelp' | t }}</p>
        </div>
        <ul hlmCardContent class="grid grid-cols-[minmax(0,1fr)] gap-2">
          @for (wish of obtained(); track wish.id) {
            <li class="flex items-center gap-3 rounded-md border p-3 text-sm">
              <ng-container *ngTemplateOutlet="cover; context: { $implicit: wish }" />
              <div class="grid min-w-0 flex-1 gap-0.5">
                <p class="flex flex-wrap items-center gap-2">
                  <span class="truncate font-medium">{{ wish.title }}</span>
                  <span class="text-muted-foreground">{{ wish.artist }}</span>
                  @if (!wish.seen && scope() === 'MINE') {
                    <span hlmBadge>{{ 'wishes.new' | t }}</span>
                  }
                </p>
                <p class="text-xs text-muted-foreground" [title]="wish.obtainedAt | dateTime">
                  @if (wish.owner; as owner) {
                    {{ 'wishes.by_user' | t: { name: owner.displayName || owner.userName } }} ·
                  }
                  @if (wish.foundWhenAdded) {
                    {{ 'wishes.wasThere' | t }}
                  } @else {
                    {{ 'wishes.arrived' | t: { when: (wish.obtainedAt | relativeTime) } }}
                  }
                  @if (wish.track; as track) {
                    · {{ track.album.title }}
                  } @else {
                    · {{ 'wishes.gone' | t }}
                  }
                  @if (wish.matchedBy) {
                    · {{ 'wishes.by.' + wish.matchedBy | t }}
                  }
                </p>
                <ng-container *ngTemplateOutlet="links; context: { $implicit: wish }" />
              </div>
              @if (scope() === 'MINE') {
                <button
                  hlmBtn
                  variant="ghost"
                  size="sm"
                  type="button"
                  [disabled]="busy()"
                  (click)="remove(wish)"
                >
                  {{ 'wishes.remove' | t }}
                </button>
              }
            </li>
          } @empty {
            <li class="text-sm text-muted-foreground">{{ 'wishes.noneObtained' | t }}</li>
          }
        </ul>
      </section>
    </div>

    <ng-template #cover let-wish>
      @if (wish.artworkId) {
        <img
          class="size-12 shrink-0 rounded object-cover"
          loading="lazy"
          [src]="'/rest/v1/artwork/' + wish.artworkId + '?size=128'"
          [alt]="'wishes.cover' | t"
        />
      } @else {
        <div class="size-12 shrink-0 rounded bg-muted" aria-hidden="true"></div>
      }
    </ng-template>

    <ng-template #links let-wish>
      @if (wish.sourceUrl || wish.musicBrainzRecordingId) {
        <p class="flex flex-wrap gap-x-3 text-xs">
          @if (wish.sourceUrl) {
            <a
              class="underline"
              [href]="wish.sourceUrl"
              target="_blank"
              rel="noopener noreferrer"
              >{{ 'wishes.openIn' | t: { service: serviceName(wish.sourceService) } }}</a
            >
          }
          @if (wish.musicBrainzRecordingId && wish.sourceService !== 'MUSIC_BRAINZ') {
            <a
              class="underline"
              [href]="musicBrainzUrl(wish.musicBrainzRecordingId)"
              target="_blank"
              rel="noopener noreferrer"
              >MusicBrainz</a
            >
          }
        </p>
      }
    </ng-template>
  `,
})
export class WishesPage implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly canSeeAll = inject(AuthService).can('VIEW_ALL_WISHES');
  protected readonly scope = signal<WishListScope>('MINE');

  protected link = '';
  protected title = '';
  protected artist = '';
  protected album = '';
  protected note = '';
  protected mbid = '';
  protected acoustId = '';
  private durationMs: number | null = null;

  protected readonly preview = signal<Preview | null>(null);
  protected readonly reading = signal(false);
  protected readonly wishes = signal<Wish[]>([]);
  protected readonly wanted = computed(() => this.wishes().filter((w) => w.status === 'WANTED'));
  protected readonly obtained = computed(() =>
    this.wishes().filter((w) => w.status === 'OBTAINED'),
  );
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    await this.load();
    // What arrives while the page is open shows at once (and counts as seen when the page is opened again).
    this.gql
      .subscribe(WishObtainedSubscription)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(({ wishObtained }) => {
        toast.success(this.i18n.t('wishes.arrivedToast', { title: wishObtained.title }));
        void this.load(false);
      });
  }

  protected serviceName(service: Preview['service'] | null | undefined): string {
    return wishServiceName(service) ?? this.i18n.t('wishes.otherPage');
  }

  protected musicBrainzUrl(id: string): string {
    return musicBrainzRecordingUrl(id);
  }

  protected async setScope(scope: WishListScope): Promise<void> {
    if (scope === this.scope()) return;
    this.scope.set(scope);
    this.wishes.set([]);
    await this.load(false);
  }

  private async load(markSeen = true): Promise<void> {
    try {
      const scope = this.scope();
      const result = await this.gql.query(WishListQuery, { scope });
      if (scope !== this.scope()) return;
      this.wishes.set(result.wishList.items);
      if (markSeen && scope === 'MINE' && result.wishList.unseen > 0)
        await this.gql.mutate(MarkWishesSeenMutation);
    } catch (error) {
      this.error.set(this.i18n.error(ApiError.from(error).code));
    }
  }

  /** Reads the link and fills the form with what it says. */
  protected async read(): Promise<void> {
    this.reading.set(true);
    this.error.set(null);
    try {
      const { resolveWishLink: found } = await this.gql.mutate(ResolveWishLink, {
        input: { url: this.link.trim() },
      });
      this.preview.set(found);
      this.title = found.title;
      this.artist = found.artist;
      this.album = found.album ?? '';
      this.mbid = found.musicBrainzRecordingId ?? '';
      this.durationMs = found.durationMs ?? null;
    } catch (error) {
      this.error.set(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.reading.set(false);
    }
  }

  protected clearLink(): void {
    this.preview.set(null);
    this.link = '';
    this.durationMs = null;
  }

  protected async add(form: NgForm): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    const found = this.preview();
    try {
      const { addWish } = await this.gql.mutate(AddWishMutation, {
        input: {
          title: this.title.trim(),
          artist: this.artist.trim(),
          album: this.album.trim() || null,
          note: this.note.trim() || null,
          musicBrainzRecordingId: this.mbid.trim() || null,
          acoustId: this.acoustId.trim() || null,
          durationMs: found ? this.durationMs : null,
          sourceUrl: found?.url ?? null,
          artworkId: found?.artworkId ?? null,
        },
      });
      this.wishes.update((list) => [addWish, ...list]);
      toast.success(
        this.i18n.t(addWish.status === 'OBTAINED' ? 'wishes.alreadyThere' : 'wishes.added'),
      );
      // Empty and untouched again: no fields marked as missing.
      form.resetForm({ title: '', artist: '', album: '', note: '', mbid: '', acoustId: '' });
      this.clearLink();
    } catch (error) {
      this.error.set(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(wish: Wish): Promise<void> {
    this.busy.set(true);
    try {
      await this.gql.mutate(RemoveWishMutation, { id: wish.id });
      this.wishes.update((list) => list.filter((w) => w.id !== wish.id));
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }
}
