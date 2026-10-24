import { Component, OnInit, inject, signal } from '@angular/core';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type { MyDlnaStarTracksQuery as Result } from '../../graphql/generated/graphql';
import { MyDlnaStarTracksQuery, SetMyDlnaStarTracksMutation } from '../../graphql/operations';

/** Whether the user's StarTracks are shown to the devices of the local network (DLNA); only while DLNA is on. */
@Component({
  selector: 'app-my-dlna',
  imports: [HlmCardImports, HlmSwitchImports, TranslatePipe],
  template: `
    @if (state(); as s) {
      @if (s.available) {
        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'account.dlna.title' | t }}</h2>
            <p hlmCardDescription>{{ 'account.dlna.description' | t: { name: s.name } }}</p>
          </div>
          <div hlmCardContent>
            <label class="flex items-center justify-between gap-3 text-sm">
              <span>{{ 'account.dlna.show' | t }}</span>
              <hlm-switch
                [checked]="s.shown"
                [disabled]="busy()"
                (checkedChange)="set($event)"
                [aria-label]="'account.dlna.show' | t"
              />
            </label>
          </div>
        </section>
      }
    }
  `,
})
export class MyDlna implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  protected readonly state = signal<Result['myDlnaStarTracks'] | null>(null);
  protected readonly busy = signal(false);

  async ngOnInit(): Promise<void> {
    try {
      this.state.set((await this.gql.query(MyDlnaStarTracksQuery)).myDlnaStarTracks);
    } catch {
      // Not shown without it.
    }
  }

  protected async set(shown: boolean): Promise<void> {
    this.busy.set(true);
    try {
      this.state.set(
        (await this.gql.mutate(SetMyDlnaStarTracksMutation, { shown })).setMyDlnaStarTracks,
      );
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }
}
