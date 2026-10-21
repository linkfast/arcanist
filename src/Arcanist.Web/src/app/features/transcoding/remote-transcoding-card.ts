import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmRadioGroupImports } from '@spartan-ng/helm/radio-group';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  RemoteLibrarySettingsFieldsFragment as Settings,
  RemoteTranscoding,
} from '../../graphql/generated/graphql';
import {
  RemoteLibrarySettingsQuery,
  SetRemoteLibrarySettingsMutation,
} from '../../graphql/operations';

const Modes: RemoteTranscoding[] = ['AUTO', 'HERE', 'OWNER'];

/** Where music other servers share is converted: here, at its owner's, or where it is cheapest. */
@Component({
  selector: 'app-remote-transcoding-card',
  imports: [FormsModule, HlmButtonImports, HlmCardImports, HlmRadioGroupImports, TranslatePipe],
  template: `
    <section hlmCard>
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'remoteLibraries.transcoding' | t }}</h2>
        <p hlmCardDescription>{{ 'remoteLibraries.transcodingHelp' | t }}</p>
      </div>
      @if (settings()) {
        <div hlmCardContent class="grid gap-4 text-sm">
          <hlm-radio-group class="grid gap-2" name="remote-transcoding" [(ngModel)]="mode">
            @for (option of modes; track option) {
              <label class="flex items-start gap-2">
                <hlm-radio [value]="option" class="mt-0.5"
                  ><hlm-radio-indicator indicator
                /></hlm-radio>
                {{ 'remoteLibraries.modes.' + option | t }}
              </label>
            }
          </hlm-radio-group>
          <div>
            <button hlmBtn type="button" [disabled]="busy()" (click)="save()">
              {{ 'common.save' | t }}
            </button>
          </div>
        </div>
      }
    </section>
  `,
})
export class RemoteTranscodingCard implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  protected readonly modes = Modes;
  protected readonly settings = signal<Settings | null>(null);
  protected readonly busy = signal(false);
  protected mode: RemoteTranscoding = 'AUTO';

  async ngOnInit(): Promise<void> {
    this.show((await this.gql.query(RemoteLibrarySettingsQuery)).remoteLibrarySettings);
  }

  /** Only where it is converted: the cache of shared music stays as the P2P network's page has it. */
  protected async save(): Promise<void> {
    const current = this.settings()!;
    this.busy.set(true);
    try {
      const result = await this.gql.mutate(SetRemoteLibrarySettingsMutation, {
        input: {
          cacheEnabled: current.cacheEnabled,
          cacheMaxBytes: current.cacheMaxBytes,
          cacheMaxAgeDays: current.cacheMaxAgeDays,
          transcoding: this.mode,
        },
      });
      this.show(result.setRemoteLibrarySettings);
      toast.success(this.i18n.t('remoteLibraries.saved'));
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }

  private show(settings: Settings): void {
    this.settings.set(settings);
    this.mode = settings.transcoding;
  }
}
