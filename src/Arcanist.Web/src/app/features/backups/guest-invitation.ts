import { Component, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import {
  CreateBackupInvitationMutation,
  CreateServerDirectoryMutation,
  ServerDirectoriesQuery,
} from '../../graphql/operations';
import { DirectoryCreator, DirectoryLoader, FolderPicker } from '../../shared/folder-picker';
import { Invitation } from '../../shared/invitation';

/** Invites another Arcanist server to keep its backups in a folder of this one, through the P2P network. */
@Component({
  selector: 'app-guest-invitation',
  imports: [
    FormsModule,
    RouterLink,
    HlmAlertImports,
    HlmButtonImports,
    HlmDialogImports,
    HlmFieldImports,
    HlmInputImports,
    HlmSpinnerImports,
    TranslatePipe,
    FolderPicker,
    Invitation,
  ],
  host: { class: 'block w-full min-w-0' },
  template: `
    @if (invitation(); as created) {
      <div class="grid gap-4">
        <hlm-dialog-header>
          <h3 hlmDialogTitle>{{ 'backups.guests.invitationTitle' | t }}</h3>
          <p hlmDialogDescription>{{ 'backups.guests.invitationHelp' | t }}</p>
        </hlm-dialog-header>
        <app-invitation [link]="created.link" [expiresAt]="created.expiresAt" [showQr]="false" />
        <hlm-dialog-footer>
          <button hlmBtn type="button" (click)="closed.emit()">{{ 'common.done' | t }}</button>
        </hlm-dialog-footer>
      </div>
    } @else if (picking()) {
      <div class="grid gap-4">
        <hlm-dialog-header>
          <h3 hlmDialogTitle>{{ 'folderPicker.title' | t }}</h3>
        </hlm-dialog-header>
        <app-folder-picker
          [load]="loadDirectories"
          [create]="createDirectory"
          [start]="folder || null"
          (picked)="folder = $event; picking.set(false)"
          (cancelled)="picking.set(false)"
        />
      </div>
    } @else {
      <form class="grid grid-cols-[minmax(0,1fr)] gap-4" (ngSubmit)="create()">
        <hlm-dialog-header>
          <h3 hlmDialogTitle>{{ 'backups.guests.invite' | t }}</h3>
          <p hlmDialogDescription>{{ 'backups.guests.inviteHelp' | t }}</p>
        </hlm-dialog-header>

        <hlm-field>
          <label hlmFieldLabel for="guest-name">{{ 'backups.guests.name' | t }}</label>
          <input
            hlmInput
            id="guest-name"
            name="name"
            [placeholder]="'backups.guests.namePlaceholder' | t"
            [(ngModel)]="name"
          />
          @if (fieldError('name'); as message) {
            <hlm-field-error>{{ message }}</hlm-field-error>
          }
        </hlm-field>

        <hlm-field>
          <label hlmFieldLabel for="guest-folder">{{ 'backups.guests.folder' | t }}</label>
          <div class="flex gap-2">
            <input
              hlmInput
              id="guest-folder"
              name="folder"
              class="min-w-0 flex-1 font-mono text-xs"
              [(ngModel)]="folder"
            />
            <button hlmBtn variant="outline" type="button" (click)="picking.set(true)">
              {{ 'backups.destinations.choose' | t }}
            </button>
          </div>
          <p hlmFieldDescription>{{ 'backups.guests.folderHelp' | t }}</p>
          @if (fieldError('folder'); as message) {
            <hlm-field-error>{{ message }}</hlm-field-error>
          }
        </hlm-field>

        <hlm-field>
          <label hlmFieldLabel for="guest-quota">{{ 'backups.guests.quota' | t }}</label>
          <div class="flex items-center gap-2 text-sm">
            <input
              hlmInput
              id="guest-quota"
              name="quota"
              type="number"
              min="0"
              step="any"
              class="w-32"
              [(ngModel)]="quotaGb"
            />
            GB
          </div>
          <p hlmFieldDescription>{{ 'backups.guests.quotaHelp' | t }}</p>
          @if (fieldError('quotaGb'); as message) {
            <hlm-field-error>{{ message }}</hlm-field-error>
          }
        </hlm-field>

        <p class="text-xs text-muted-foreground">
          {{ 'backups.guests.needsP2p' | t }}
          <a class="underline" routerLink="/network">{{ 'backups.goToNetwork' | t }}</a>
        </p>

        @if (error(); as code) {
          <div hlmAlert variant="destructive">
            <p hlmAlertDescription>
              {{ i18n.error(code) }}
              @if (code === 'p2p_disabled' || code === 'p2p_unavailable') {
                <a class="underline" routerLink="/network">{{ 'backups.goToNetwork' | t }}</a>
              }
            </p>
          </div>
        }

        <hlm-dialog-footer>
          <button hlmBtn variant="ghost" type="button" (click)="closed.emit()">
            {{ 'common.cancel' | t }}
          </button>
          <button hlmBtn type="submit" [disabled]="busy() || !name.trim() || !folder.trim()">
            @if (busy()) {
              <hlm-spinner class="size-4" />
            }
            {{ 'backups.guests.createInvitation' | t }}
          </button>
        </hlm-dialog-footer>
      </form>
    }
  `,
})
export class GuestInvitation {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);

  readonly closed = output<void>();

  protected name = '';
  protected folder = '';
  protected quotaGb: number | null = null;
  protected readonly picking = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly fieldErrors = signal<Record<string, string[]>>({});
  protected readonly invitation = signal<{ link: string; expiresAt: string } | null>(null);

  protected readonly loadDirectories: DirectoryLoader = async (path) =>
    (await this.gql.query(ServerDirectoriesQuery, { path })).serverDirectories;
  protected readonly createDirectory: DirectoryCreator = async (parent, name) =>
    (await this.gql.mutate(CreateServerDirectoryMutation, { input: { parent, name } }))
      .createServerDirectory;

  protected fieldError(field: string): string | null {
    const code = this.fieldErrors()[field]?.[0];
    return code ? this.i18n.error(code) : null;
  }

  protected async create(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.fieldErrors.set({});
    try {
      const quota = Number(this.quotaGb);
      const result = await this.gql.mutate(CreateBackupInvitationMutation, {
        input: {
          name: this.name.trim(),
          folder: this.folder.trim(),
          quotaGb: this.quotaGb !== null && `${this.quotaGb}` !== '' && quota > 0 ? quota : null,
        },
      });
      this.invitation.set(result.createBackupInvitation);
    } catch (error) {
      const apiError = ApiError.from(error);
      this.fieldErrors.set(apiError.fields);
      if (Object.keys(apiError.fields).length === 0) this.error.set(apiError.code);
    } finally {
      this.busy.set(false);
    }
  }
}
