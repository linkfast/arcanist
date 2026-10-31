import { Component, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideUpload } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { TranslatePipe } from '../../core/i18n/i18n';

/**
 * Where a package (.zip) is installed from by hand: dropped here, picked with a button that stands out, or
 * downloaded from a URL.
 */
@Component({
  selector: 'app-package-dropzone',
  imports: [FormsModule, NgIcon, HlmButtonImports, HlmInputImports, TranslatePipe],
  providers: [provideIcons({ lucideUpload })],
  template: `
    <div
      class="grid gap-3 rounded-lg border-2 border-dashed p-5 text-center transition-colors"
      data-tour="extensions-install"
      [class.border-primary]="dragging()"
      [class.bg-muted]="dragging()"
      (dragover)="over($event)"
      (dragleave)="dragging.set(false)"
      (drop)="drop($event)"
    >
      <ng-icon name="lucideUpload" class="mx-auto text-muted-foreground" size="28" />
      <div>
        <p class="text-sm font-medium">{{ 'extensions.install.dropTitle.' + kind() | t }}</p>
        <p class="text-xs text-muted-foreground">{{ 'extensions.install.dropHelp' | t }}</p>
      </div>
      <div>
        <button hlmBtn type="button" [disabled]="busy()" (click)="picker.click()">
          <ng-icon name="lucideUpload" class="mr-1.5" />
          {{ 'extensions.install.zip' | t }}
        </button>
        <input
          #picker
          type="file"
          accept=".zip,application/zip"
          class="hidden"
          (change)="pick($event)"
        />
      </div>
      <div class="flex flex-wrap items-center gap-2 border-t pt-3 text-left">
        <span class="text-xs text-muted-foreground">{{ 'extensions.install.or' | t }}</span>
        <input
          hlmInput
          class="min-w-48 flex-1"
          type="url"
          [placeholder]="'extensions.install.urlPlaceholder' | t"
          [(ngModel)]="url"
        />
        <button
          hlmBtn
          variant="outline"
          size="sm"
          type="button"
          [disabled]="busy() || !url.trim()"
          (click)="fromUrl()"
        >
          {{ 'extensions.install.fromUrl' | t }}
        </button>
      </div>
    </div>
  `,
})
export class PackageDropzone {
  readonly kind = input.required<'frontend' | 'extension'>();
  readonly busy = input(false);
  readonly file = output<File>();
  readonly urlChosen = output<string>();

  protected readonly dragging = signal(false);
  protected url = '';

  protected over(event: DragEvent): void {
    event.preventDefault();
    if (!this.busy()) this.dragging.set(true);
  }

  protected drop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    const file = event.dataTransfer?.files?.[0];
    if (file && !this.busy()) this.file.emit(file);
  }

  protected pick(event: Event): void {
    const element = event.target as HTMLInputElement;
    const file = element.files?.[0];
    element.value = '';
    if (file) this.file.emit(file);
  }

  protected fromUrl(): void {
    this.urlChosen.emit(this.url.trim());
    this.url = '';
  }
}
