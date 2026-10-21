import { Component, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import { CreateS3LibraryMutation, TestS3ConnectionMutation } from '../../graphql/operations';

/** A new library in an S3 bucket: the connection, a test, and its name. Place it inside a dialog. */
@Component({
  selector: 'app-s3-library',
  imports: [
    FormsModule,
    HlmButtonImports,
    HlmDialogImports,
    HlmFieldImports,
    HlmInputImports,
    HlmSpinnerImports,
    HlmSwitchImports,
    TranslatePipe,
  ],
  host: { class: 'block w-full min-w-0' },
  template: `
    <form class="grid grid-cols-[minmax(0,1fr)] gap-4" (ngSubmit)="create()">
      <hlm-field>
        <label hlmFieldLabel for="s3-name">{{ 'libraries.name' | t }}</label>
        <input hlmInput id="s3-name" name="name" [(ngModel)]="name" required />
      </hlm-field>
      <hlm-field>
        <label hlmFieldLabel for="s3-endpoint">{{ 's3.endpoint' | t }}</label>
        <input
          hlmInput
          id="s3-endpoint"
          name="endpoint"
          class="font-mono text-xs"
          placeholder="https://s3.eu-central-1.wasabisys.com"
          [(ngModel)]="endpoint"
          (ngModelChange)="tested.set(null)"
        />
        <p hlmFieldDescription>{{ 's3.endpointHelp' | t }}</p>
      </hlm-field>
      <div class="grid gap-3 sm:grid-cols-3">
        <hlm-field>
          <label hlmFieldLabel for="s3-bucket">{{ 's3.bucket' | t }}</label>
          <input
            hlmInput
            id="s3-bucket"
            name="bucket"
            [(ngModel)]="bucket"
            (ngModelChange)="tested.set(null)"
          />
        </hlm-field>
        <hlm-field>
          <label hlmFieldLabel for="s3-prefix">{{ 's3.prefix' | t }}</label>
          <input
            hlmInput
            id="s3-prefix"
            name="prefix"
            [placeholder]="'s3.prefixPlaceholder' | t"
            [(ngModel)]="prefix"
            (ngModelChange)="tested.set(null)"
          />
        </hlm-field>
        <hlm-field>
          <label hlmFieldLabel for="s3-region">{{ 's3.region' | t }}</label>
          <input
            hlmInput
            id="s3-region"
            name="region"
            placeholder="us-east-1"
            [(ngModel)]="region"
            (ngModelChange)="tested.set(null)"
          />
        </hlm-field>
      </div>
      <div class="grid gap-3 sm:grid-cols-2">
        <hlm-field>
          <label hlmFieldLabel for="s3-key">{{ 's3.accessKeyId' | t }}</label>
          <input
            hlmInput
            id="s3-key"
            name="accessKeyId"
            autocomplete="off"
            class="font-mono text-xs"
            [(ngModel)]="accessKeyId"
            (ngModelChange)="tested.set(null)"
          />
        </hlm-field>
        <hlm-field>
          <label hlmFieldLabel for="s3-secret">{{ 's3.secretAccessKey' | t }}</label>
          <input
            hlmInput
            id="s3-secret"
            name="secretAccessKey"
            type="password"
            autocomplete="new-password"
            class="font-mono text-xs"
            [(ngModel)]="secretAccessKey"
            (ngModelChange)="tested.set(null)"
          />
        </hlm-field>
      </div>
      <label class="flex items-start gap-3 text-sm">
        <hlm-switch name="pathStyle" [(ngModel)]="pathStyle" (ngModelChange)="tested.set(null)" />
        <span>
          {{ 's3.pathStyle' | t }}
          <span class="block text-xs text-muted-foreground">{{ 's3.pathStyleHelp' | t }}</span>
        </span>
      </label>
      <p class="text-xs text-muted-foreground">{{ 's3.notes' | t }}</p>

      <label class="flex items-start gap-3 text-sm">
        <hlm-switch name="encrypt" [(ngModel)]="encrypt" (ngModelChange)="encryptionChanged()" />
        <span>
          {{ 's3.encrypt' | t }}
          <span class="block text-xs text-muted-foreground">{{ 's3.encryptHelp' | t }}</span>
        </span>
      </label>
      @if (encrypt) {
        <div class="grid gap-2 rounded-md border p-3">
          <hlm-field>
            <label hlmFieldLabel for="s3-key">{{ 's3.key' | t }}</label>
            <div class="flex gap-2">
              <input
                hlmInput
                id="s3-key"
                name="encryptionKey"
                class="min-w-0 flex-1 font-mono text-xs"
                autocomplete="off"
                spellcheck="false"
                [(ngModel)]="encryptionKey"
              />
              <button hlmBtn variant="outline" size="sm" type="button" (click)="newKey()">
                {{ 's3.newKey' | t }}
              </button>
              <button hlmBtn variant="outline" size="sm" type="button" (click)="copyKey()">
                {{ 'common.copy' | t }}
              </button>
            </div>
          </hlm-field>
          <p class="text-xs text-amber-700 dark:text-amber-400">{{ 's3.keyWarning' | t }}</p>
          <label class="flex items-center gap-2 text-sm">
            <input type="checkbox" name="keySaved" [(ngModel)]="keySaved" />
            {{ 's3.keySaved' | t }}
          </label>
        </div>
      }

      <div class="flex flex-wrap items-center gap-3">
        <button
          hlmBtn
          variant="outline"
          type="button"
          [disabled]="busy() || !ready()"
          (click)="test()"
        >
          {{ 'setup.database.test' | t }}
        </button>
        @if (tested(); as result) {
          @if (result.success) {
            <span class="text-sm text-green-600 dark:text-green-400">{{
              (result.truncated ? 's3.foundMore' : 's3.found') | t: { count: result.audioFiles }
            }}</span>
          } @else {
            <span class="text-sm text-destructive">{{
              i18n.error(result.errorCode ?? 'unexpected')
            }}</span>
          }
        }
      </div>

      <hlm-dialog-footer>
        <button hlmBtn variant="ghost" type="button" (click)="cancelled.emit()">
          {{ 'common.cancel' | t }}
        </button>
        <button
          hlmBtn
          type="submit"
          [disabled]="
            busy() ||
            !ready() ||
            !name.trim() ||
            !tested()?.success ||
            (encrypt && (!keySaved || !validKey()))
          "
        >
          @if (busy()) {
            <hlm-spinner class="size-4" />
          }
          {{ 's3.create' | t }}
        </button>
      </hlm-dialog-footer>
    </form>
  `,
})
export class S3LibraryForm {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);

  readonly created = output<void>();
  readonly cancelled = output<void>();

  protected name = '';
  protected endpoint = '';
  protected region = '';
  protected bucket = '';
  protected prefix = '';
  protected accessKeyId = '';
  protected secretAccessKey = '';
  protected pathStyle = true;
  protected encrypt = false;
  protected encryptionKey = '';
  protected keySaved = false;
  protected readonly busy = signal(false);
  protected readonly tested = signal<{
    success: boolean;
    errorCode?: string | null;
    audioFiles: number;
    truncated: boolean;
  } | null>(null);

  protected ready(): boolean {
    return (
      !!this.endpoint.trim() &&
      !!this.bucket.trim() &&
      !!this.accessKeyId.trim() &&
      !!this.secretAccessKey
    );
  }

  private connection() {
    return {
      endpoint: this.endpoint.trim(),
      region: this.region.trim() || null,
      bucket: this.bucket.trim(),
      prefix: this.prefix.trim() || null,
      accessKeyId: this.accessKeyId.trim(),
      secretAccessKey: this.secretAccessKey,
      pathStyle: this.pathStyle,
    };
  }

  protected async test(): Promise<void> {
    await this.run(async () => {
      this.tested.set(
        (await this.gql.mutate(TestS3ConnectionMutation, { input: this.connection() }))
          .testS3Connection,
      );
    });
  }

  protected encryptionChanged(): void {
    if (this.encrypt && !this.encryptionKey) this.newKey();
  }

  /** A new 256-bit key, made here: the server sees it only when the library is created. */
  protected newKey(): void {
    const bytes = crypto.getRandomValues(new Uint8Array(32));
    this.encryptionKey = btoa(String.fromCharCode(...bytes));
    this.keySaved = false;
  }

  protected validKey(): boolean {
    try {
      const text = this.encryptionKey.trim().replace(/-/g, '+').replace(/_/g, '/');
      return atob(text.padEnd(text.length + ((4 - (text.length % 4)) % 4), '=')).length === 32;
    } catch {
      return false;
    }
  }

  protected async copyKey(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.encryptionKey);
      toast.success(this.i18n.t('s3.keyCopied'));
    } catch {
      // Without the clipboard (an insecure page), the key is in the field to copy by hand.
    }
  }

  protected async create(): Promise<void> {
    await this.run(async () => {
      await this.gql.mutate(CreateS3LibraryMutation, {
        input: {
          name: this.name.trim(),
          connection: this.connection(),
          encryptionKey: this.encrypt ? this.encryptionKey.trim() : null,
        },
      });
      toast.success(this.i18n.t('s3.created'));
      this.created.emit();
    });
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
    } catch (error) {
      const apiError = ApiError.from(error);
      toast.error(this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code));
    } finally {
      this.busy.set(false);
    }
  }
}
