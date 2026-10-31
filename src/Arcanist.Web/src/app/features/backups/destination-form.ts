import { Component, OnInit, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmRadioGroupImports } from '@spartan-ng/helm/radio-group';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  BackupDestinationFieldsFragment as Destination,
  BackupDestinationKind,
} from '../../graphql/generated/graphql';
import {
  BackupProvidersQuery,
  CreateBackupDestinationMutation,
  CreateServerDirectoryMutation,
  ServerDirectoriesQuery,
} from '../../graphql/operations';
import { DirectoryCreator, DirectoryLoader, FolderPicker } from '../../shared/folder-picker';
import {
  S3ConnectionDraft,
  S3ConnectionFields,
  emptyS3Connection,
  s3ConnectionInput,
  s3ConnectionReady,
} from '../../shared/s3-connection';

/** A new place for backups: a folder of this server, an S3 bucket or another Arcanist server. */
@Component({
  selector: 'app-destination-form',
  imports: [
    FormsModule,
    RouterLink,
    HlmAlertImports,
    HlmButtonImports,
    HlmDialogImports,
    HlmFieldImports,
    HlmInputImports,
    HlmRadioGroupImports,
    HlmSpinnerImports,
    TranslatePipe,
    FolderPicker,
    S3ConnectionFields,
  ],
  host: { class: 'block w-full min-w-0' },
  template: `
    @if (picking()) {
      <div class="grid gap-4">
        <hlm-dialog-header>
          <h3 hlmDialogTitle>{{ 'folderPicker.title' | t }}</h3>
        </hlm-dialog-header>
        <app-folder-picker
          [load]="loadDirectories"
          [create]="createDirectory"
          [start]="path || null"
          (picked)="path = $event; picking.set(false)"
          (cancelled)="picking.set(false)"
        />
      </div>
    } @else {
      <form class="grid grid-cols-[minmax(0,1fr)] gap-5" (ngSubmit)="create()">
        <hlm-dialog-header>
          <h3 hlmDialogTitle>{{ 'backups.destinations.add' | t }}</h3>
          <p hlmDialogDescription>{{ 'backups.destinations.addHelp' | t }}</p>
        </hlm-dialog-header>

        <hlm-radio-group class="grid gap-2" name="kind" [(ngModel)]="kind">
          @for (option of kinds(); track option) {
            <label class="flex items-start gap-3 rounded-lg border p-3 text-sm">
              <hlm-radio [value]="option" class="mt-0.5"
                ><hlm-radio-indicator indicator
              /></hlm-radio>
              <span class="grid gap-0.5">
                <span class="font-medium">{{ 'backups.destinations.kinds.' + option | t }}</span>
                <span class="text-xs text-muted-foreground">{{
                  'backups.destinations.kindHelp.' + option | t
                }}</span>
              </span>
            </label>
          }
          @for (provider of providers(); track provider.id) {
            <label class="flex items-start gap-3 rounded-lg border p-3 text-sm">
              <hlm-radio [value]="'provider:' + provider.id" class="mt-0.5"
                ><hlm-radio-indicator indicator
              /></hlm-radio>
              <span class="grid gap-0.5">
                <span class="font-medium">{{ provider.name }}</span>
                <span class="text-xs text-muted-foreground">{{
                  'backups.destinations.kindHelp.EXTENSION' | t
                }}</span>
              </span>
            </label>
          }
        </hlm-radio-group>

        <hlm-field>
          <label hlmFieldLabel for="destination-name">{{ 'backups.destinations.name' | t }}</label>
          <input
            hlmInput
            id="destination-name"
            name="name"
            [placeholder]="
              (kind === 'PEER'
                ? 'backups.destinations.peerNamePlaceholder'
                : 'backups.destinations.namePlaceholder'
              ) | t
            "
            [(ngModel)]="name"
          />
          @if (kind === 'PEER') {
            <p hlmFieldDescription>{{ 'backups.destinations.peerNameHelp' | t }}</p>
          }
          @if (fieldError('name'); as message) {
            <hlm-field-error>{{ message }}</hlm-field-error>
          }
        </hlm-field>

        @switch (kind) {
          @case ('FOLDER') {
            <hlm-field>
              <label hlmFieldLabel for="destination-path">{{
                'backups.destinations.folder' | t
              }}</label>
              <div class="flex gap-2">
                <input
                  hlmInput
                  id="destination-path"
                  name="path"
                  class="min-w-0 flex-1 font-mono text-xs"
                  [(ngModel)]="path"
                />
                <button hlmBtn variant="outline" type="button" (click)="picking.set(true)">
                  {{ 'backups.destinations.choose' | t }}
                </button>
              </div>
              <p hlmFieldDescription>{{ 'backups.destinations.folderHelp' | t }}</p>
              @if (fieldError('path'); as message) {
                <hlm-field-error>{{ message }}</hlm-field-error>
              }
            </hlm-field>
          }
          @case ('S3') {
            @if (!s3Available()) {
              <p
                class="rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-800 dark:text-amber-300"
              >
                {{ 'backups.destinations.s3NeedsPlan' | t }}
              </p>
            }
            <app-s3-connection fieldId="destination-s3" [(value)]="s3" />
            @for (message of s3Errors(); track $index) {
              <p class="text-sm text-destructive">{{ message }}</p>
            }
          }
          @case ('PEER') {
            <hlm-field>
              <label hlmFieldLabel for="destination-invitation">{{
                'backups.destinations.invitation' | t
              }}</label>
              <input
                hlmInput
                id="destination-invitation"
                name="invitation"
                autocomplete="off"
                class="font-mono text-xs"
                placeholder="arcanist://p2p?kind=backup…"
                [(ngModel)]="invitation"
              />
              <p hlmFieldDescription>{{ 'backups.destinations.invitationHelp' | t }}</p>
              @if (fieldError('invitation'); as message) {
                <hlm-field-error>{{ message }}</hlm-field-error>
              }
            </hlm-field>
          }
        }

        @if (error(); as code) {
          <div hlmAlert variant="destructive">
            <p hlmAlertDescription>
              {{ i18n.error(code) }}
              @if (code === 'p2p_disabled') {
                <a class="underline" routerLink="/network">{{ 'backups.goToNetwork' | t }}</a>
              }
            </p>
          </div>
        }

        <hlm-dialog-footer>
          <button hlmBtn variant="ghost" type="button" (click)="cancelled.emit()">
            {{ 'common.cancel' | t }}
          </button>
          <button hlmBtn type="submit" [disabled]="busy() || !ready()">
            @if (busy()) {
              <hlm-spinner class="size-4" />
            }
            {{ 'backups.destinations.create' | t }}
          </button>
        </hlm-dialog-footer>
      </form>
    }
  `,
})
export class DestinationForm implements OnInit {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);

  /** Buckets are offered (the Ultimate Cloud extension is on). */
  readonly s3Offered = input(true);
  /** The plan allows storing on other clouds. */
  readonly s3Available = input(false);
  readonly created = output<Destination>();
  readonly cancelled = output<void>();

  /** A kind of destination, or provider:<id> for one an extension provides. */
  protected kind: BackupDestinationKind | `provider:${string}` = 'FOLDER';
  /** The kinds of destination extensions provide (Ultimate Cloud, with Ultimate Cloud Plus). */
  protected readonly providers = signal<{ id: string; name: string }[]>([]);
  protected name = '';
  protected path = '';
  protected invitation = '';
  protected s3: S3ConnectionDraft = emptyS3Connection();

  protected readonly picking = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly fieldErrors = signal<Record<string, string[]>>({});

  protected readonly loadDirectories: DirectoryLoader = async (path) =>
    (await this.gql.query(ServerDirectoriesQuery, { path })).serverDirectories;
  protected readonly createDirectory: DirectoryCreator = async (parent, name) =>
    (await this.gql.mutate(CreateServerDirectoryMutation, { input: { parent, name } }))
      .createServerDirectory;

  async ngOnInit(): Promise<void> {
    try {
      this.providers.set(
        (await this.gql.query(BackupProvidersQuery, { language: this.i18n.language() }))
          .backupProviders,
      );
    } catch {
      // Without them, the server's own kinds are still there.
    }
  }

  protected kinds(): BackupDestinationKind[] {
    return this.s3Offered() ? ['FOLDER', 'S3', 'PEER'] : ['FOLDER', 'PEER'];
  }

  protected ready(): boolean {
    switch (this.kind) {
      case 'FOLDER':
        return !!this.name.trim() && !!this.path.trim();
      case 'S3':
        return !!this.name.trim() && s3ConnectionReady(this.s3);
      case 'PEER':
        return !!this.invitation.trim();
      default:
        return !!this.name.trim();
    }
  }

  protected fieldError(field: string): string | null {
    const code = this.fieldErrors()[field]?.[0];
    return code ? this.i18n.error(code) : null;
  }

  /** Errors of the bucket's fields (`s3.bucket`…). */
  protected s3Errors(): string[] {
    return Object.entries(this.fieldErrors())
      .filter(([field]) => field.startsWith('s3'))
      .map(([field, codes]) => {
        const label = field.split('.')[1];
        const message = this.i18n.error(codes[0]);
        return label ? `${this.i18n.t('s3.' + label)}: ${message}` : message;
      });
  }

  protected async create(): Promise<void> {
    if (!this.ready()) return;
    this.busy.set(true);
    this.error.set(null);
    this.fieldErrors.set({});
    try {
      const result = await this.gql.mutate(CreateBackupDestinationMutation, {
        input: {
          name: this.name.trim(),
          kind: this.kind.startsWith('provider:')
            ? 'EXTENSION'
            : (this.kind as BackupDestinationKind),
          provider: this.kind.startsWith('provider:') ? this.kind.slice('provider:'.length) : null,
          path: this.kind === 'FOLDER' ? this.path.trim() : null,
          s3: this.kind === 'S3' ? s3ConnectionInput(this.s3) : null,
          invitation: this.kind === 'PEER' ? this.invitation.trim() : null,
        },
      });
      toast.success(this.i18n.t('backups.destinations.created'));
      this.created.emit(result.createBackupDestination);
    } catch (error) {
      const apiError = ApiError.from(error);
      this.fieldErrors.set(apiError.fields);
      if (Object.keys(apiError.fields).length === 0) this.error.set(apiError.code);
    } finally {
      this.busy.set(false);
    }
  }
}
