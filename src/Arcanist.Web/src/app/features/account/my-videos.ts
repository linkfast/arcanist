import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  MyVideoSettingsQuery as Result,
  VideoAudioMode,
} from '../../graphql/generated/graphql';
import { MyVideoSettingsQuery, SetMyVideoSettingsMutation } from '../../graphql/operations';

/** Whose audio the user hears with music videos by default; players can still switch while playing. */
@Component({
  selector: 'app-my-videos',
  imports: [FormsModule, HlmCardImports, HlmNativeSelectImports, TranslatePipe],
  template: `
    @if (state(); as s) {
      <section hlmCard>
        <div hlmCardHeader>
          <h2 hlmCardTitle>{{ 'account.videos.title' | t }}</h2>
          <p hlmCardDescription>{{ 'account.videos.description' | t }}</p>
        </div>
        <div hlmCardContent>
          <label class="grid gap-1 text-sm">
            <span>{{ 'account.videos.audio' | t }}</span>
            <select
              hlmNativeSelect
              name="audio"
              [ngModel]="s.audio ?? ''"
              [disabled]="busy()"
              (ngModelChange)="set($event)"
            >
              <option value="">
                {{ 'account.videos.server' | t: { mode: ('videos.audio.' + s.serverDefault | t) } }}
              </option>
              <option value="TRACK">{{ 'videos.audio.TRACK' | t }}</option>
              <option value="VIDEO">{{ 'videos.audio.VIDEO' | t }}</option>
            </select>
          </label>
        </div>
      </section>
    }
  `,
})
export class MyVideos implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  protected readonly state = signal<Result['myVideoSettings'] | null>(null);
  protected readonly busy = signal(false);

  async ngOnInit(): Promise<void> {
    try {
      this.state.set((await this.gql.query(MyVideoSettingsQuery)).myVideoSettings);
    } catch {
      // Not shown without it (an older server).
    }
  }

  protected async set(value: string): Promise<void> {
    this.busy.set(true);
    try {
      const audio = value === '' ? null : (value as VideoAudioMode);
      this.state.set(
        (await this.gql.mutate(SetMyVideoSettingsMutation, { audio })).setMyVideoSettings,
      );
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }
}
