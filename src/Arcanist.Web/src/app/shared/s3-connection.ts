import { Component, input, model } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { TranslatePipe } from '../core/i18n/i18n';

/** An S3 bucket being typed in: what the API's S3ConnectionInput needs. */
export interface S3ConnectionDraft {
  endpoint: string;
  region: string;
  bucket: string;
  prefix: string;
  accessKeyId: string;
  secretAccessKey: string;
  pathStyle: boolean;
}

export function emptyS3Connection(): S3ConnectionDraft {
  return {
    endpoint: '',
    region: '',
    bucket: '',
    prefix: '',
    accessKeyId: '',
    secretAccessKey: '',
    pathStyle: true,
  };
}

/** Everything a bucket needs is filled in. */
export function s3ConnectionReady(draft: S3ConnectionDraft): boolean {
  return (
    !!draft.endpoint.trim() &&
    !!draft.bucket.trim() &&
    !!draft.accessKeyId.trim() &&
    !!draft.secretAccessKey
  );
}

/** The draft as the API takes it (S3ConnectionInput). */
export function s3ConnectionInput(draft: S3ConnectionDraft) {
  return {
    endpoint: draft.endpoint.trim(),
    region: draft.region.trim() || null,
    bucket: draft.bucket.trim(),
    prefix: draft.prefix.trim() || null,
    accessKeyId: draft.accessKeyId.trim(),
    secretAccessKey: draft.secretAccessKey,
    pathStyle: draft.pathStyle,
  };
}

/** The fields of an S3 bucket's connection: endpoint, bucket, prefix, region, keys and addressing style. */
@Component({
  selector: 'app-s3-connection',
  imports: [FormsModule, HlmFieldImports, HlmInputImports, HlmSwitchImports, TranslatePipe],
  host: { class: 'grid grid-cols-[minmax(0,1fr)] gap-4' },
  template: `
    <hlm-field>
      <label hlmFieldLabel [for]="fieldId() + '-endpoint'">{{ 's3.endpoint' | t }}</label>
      <input
        hlmInput
        [id]="fieldId() + '-endpoint'"
        class="font-mono text-xs"
        placeholder="https://s3.eu-central-1.wasabisys.com"
        [ngModelOptions]="{ standalone: true }"
        [ngModel]="value().endpoint"
        (ngModelChange)="patch({ endpoint: $event })"
      />
      <p hlmFieldDescription>{{ 's3.endpointHelp' | t }}</p>
    </hlm-field>
    <div class="grid gap-3 sm:grid-cols-3">
      <hlm-field>
        <label hlmFieldLabel [for]="fieldId() + '-bucket'">{{ 's3.bucket' | t }}</label>
        <input
          hlmInput
          [id]="fieldId() + '-bucket'"
          [ngModelOptions]="{ standalone: true }"
          [ngModel]="value().bucket"
          (ngModelChange)="patch({ bucket: $event })"
        />
      </hlm-field>
      <hlm-field>
        <label hlmFieldLabel [for]="fieldId() + '-prefix'">{{ 's3.prefix' | t }}</label>
        <input
          hlmInput
          [id]="fieldId() + '-prefix'"
          [placeholder]="'s3.prefixPlaceholder' | t"
          [ngModelOptions]="{ standalone: true }"
          [ngModel]="value().prefix"
          (ngModelChange)="patch({ prefix: $event })"
        />
      </hlm-field>
      <hlm-field>
        <label hlmFieldLabel [for]="fieldId() + '-region'">{{ 's3.region' | t }}</label>
        <input
          hlmInput
          [id]="fieldId() + '-region'"
          placeholder="us-east-1"
          [ngModelOptions]="{ standalone: true }"
          [ngModel]="value().region"
          (ngModelChange)="patch({ region: $event })"
        />
      </hlm-field>
    </div>
    <div class="grid gap-3 sm:grid-cols-2">
      <hlm-field>
        <label hlmFieldLabel [for]="fieldId() + '-key'">{{ 's3.accessKeyId' | t }}</label>
        <input
          hlmInput
          [id]="fieldId() + '-key'"
          autocomplete="off"
          class="font-mono text-xs"
          [ngModelOptions]="{ standalone: true }"
          [ngModel]="value().accessKeyId"
          (ngModelChange)="patch({ accessKeyId: $event })"
        />
      </hlm-field>
      <hlm-field>
        <label hlmFieldLabel [for]="fieldId() + '-secret'">{{ 's3.secretAccessKey' | t }}</label>
        <input
          hlmInput
          [id]="fieldId() + '-secret'"
          type="password"
          autocomplete="new-password"
          class="font-mono text-xs"
          [ngModelOptions]="{ standalone: true }"
          [ngModel]="value().secretAccessKey"
          (ngModelChange)="patch({ secretAccessKey: $event })"
        />
      </hlm-field>
    </div>
    <label class="flex items-start gap-3 text-sm">
      <hlm-switch [checked]="value().pathStyle" (checkedChange)="patch({ pathStyle: $event })" />
      <span>
        {{ 's3.pathStyle' | t }}
        <span class="block text-xs text-muted-foreground">{{ 's3.pathStyleHelp' | t }}</span>
      </span>
    </label>
  `,
})
export class S3ConnectionFields {
  readonly value = model.required<S3ConnectionDraft>();
  /** Prefix of the fields' ids, to keep them unique on a page. */
  readonly fieldId = input('s3');

  protected patch(change: Partial<S3ConnectionDraft>): void {
    this.value.update((value) => ({ ...value, ...change }));
  }
}
