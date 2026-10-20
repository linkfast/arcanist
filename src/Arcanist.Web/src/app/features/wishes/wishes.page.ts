import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type { WishFieldsFragment as Wish } from '../../graphql/generated/graphql';
import {
  AddWishMutation,
  MarkWishesSeenMutation,
  RemoveWishMutation,
  WishListQuery,
  WishObtainedSubscription,
} from '../../graphql/operations';
import { DateTimePipe, RelativeTimePipe } from '../../shared/format';

/**
 * The user's wish list: songs they want, recognised as soon as one enters a library they can play (by MusicBrainz,
 * AcoustID, or title and artist), with the history of those that arrived. Every user has their own.
 */
@Component({
  selector: 'app-wishes-page',
  imports: [
    FormsModule,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmFieldImports,
    HlmInputImports,
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

      <section hlmCard>
        <div hlmCardHeader>
          <h2 hlmCardTitle>{{ 'wishes.add' | t }}</h2>
        </div>
        <form hlmCardContent class="grid gap-3" (ngSubmit)="add()">
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
            <summary class="cursor-pointer text-muted-foreground">{{ 'wishes.ids' | t }}</summary>
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
      </section>

      <section hlmCard>
        <div hlmCardHeader>
          <h2 hlmCardTitle>{{ 'wishes.wanted' | t }} ({{ wanted().length }})</h2>
          <p hlmCardDescription>{{ 'wishes.wantedHelp' | t }}</p>
        </div>
        <ul hlmCardContent class="grid grid-cols-[minmax(0,1fr)] gap-2">
          @for (wish of wanted(); track wish.id) {
            <li class="flex items-center justify-between gap-3 rounded-md border p-3 text-sm">
              <div class="grid min-w-0 gap-0.5">
                <p class="truncate">
                  <span class="font-medium">{{ wish.title }}</span>
                  <span class="text-muted-foreground"> · {{ wish.artist }}</span>
                  @if (wish.album) {
                    <span class="text-muted-foreground"> · {{ wish.album }}</span>
                  }
                </p>
                <p class="text-xs text-muted-foreground" [title]="wish.createdAt | dateTime">
                  {{ 'wishes.since' | t: { when: (wish.createdAt | relativeTime) } }}
                  @if (wish.note) {
                    · {{ wish.note }}
                  }
                </p>
              </div>
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
            <li class="flex items-center justify-between gap-3 rounded-md border p-3 text-sm">
              <div class="grid min-w-0 gap-0.5">
                <p class="flex flex-wrap items-center gap-2">
                  <span class="truncate font-medium">{{ wish.title }}</span>
                  <span class="text-muted-foreground">{{ wish.artist }}</span>
                  @if (!wish.seen) {
                    <span hlmBadge>{{ 'wishes.new' | t }}</span>
                  }
                </p>
                <p class="text-xs text-muted-foreground" [title]="wish.obtainedAt | dateTime">
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
              </div>
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
            </li>
          } @empty {
            <li class="text-sm text-muted-foreground">{{ 'wishes.noneObtained' | t }}</li>
          }
        </ul>
      </section>
    </div>
  `,
})
export class WishesPage implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);
  private readonly destroyRef = inject(DestroyRef);

  protected title = '';
  protected artist = '';
  protected album = '';
  protected note = '';
  protected mbid = '';
  protected acoustId = '';

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

  private async load(markSeen = true): Promise<void> {
    try {
      const result = await this.gql.query(WishListQuery);
      this.wishes.set(result.wishList.items);
      if (markSeen && result.wishList.unseen > 0) await this.gql.mutate(MarkWishesSeenMutation);
    } catch (error) {
      this.error.set(this.i18n.error(ApiError.from(error).code));
    }
  }

  protected async add(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const { addWish } = await this.gql.mutate(AddWishMutation, {
        input: {
          title: this.title.trim(),
          artist: this.artist.trim(),
          album: this.album.trim() || null,
          note: this.note.trim() || null,
          musicBrainzRecordingId: this.mbid.trim() || null,
          acoustId: this.acoustId.trim() || null,
        },
      });
      this.wishes.update((list) => [addWish, ...list]);
      toast.success(
        this.i18n.t(addWish.status === 'OBTAINED' ? 'wishes.alreadyThere' : 'wishes.added'),
      );
      this.title = this.artist = this.album = this.note = this.mbid = this.acoustId = '';
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
