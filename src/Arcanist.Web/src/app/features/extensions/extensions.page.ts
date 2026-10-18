import { HttpClient } from '@angular/common/http';
import { NgTemplateOutlet } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BrnAlertDialogContent } from '@spartan-ng/brain/alert-dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { firstValueFrom } from 'rxjs';
import { ApiError } from '../../core/api/rest';
import { CloudOffDialog } from '../../shared/cloud-off-dialog';
import { EnabledExtensions } from '../../core/extensions/enabled-extensions';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  ExtensionFieldsFragment as Extension,
  FrontendFieldsFragment as Frontend,
} from '../../graphql/generated/graphql';
import {
  ExtensionsQuery,
  FrontendsQuery,
  InstallExtensionFromUrlMutation,
  InstallFrontendFromUrlMutation,
  RestartServerMutation,
  SetDefaultFrontendMutation,
  SetExtensionEnabledMutation,
  UninstallExtensionMutation,
  UninstallFrontendMutation,
} from '../../graphql/operations';

type Kind = 'frontend' | 'extension';

/** A package waiting for the administrator to accept that it runs with full trust. */
interface PendingInstall {
  kind: Kind;
  file: File | null;
  url: string | null;
}

/**
 * Web players (served at / and /players/<id>/) and extensions (mStream, Subsonic and installed ones). Both run
 * with full trust, so installing asks for confirmation and a recent password.
 */
@Component({
  selector: 'app-extensions-page',
  imports: [
    CloudOffDialog,
    FormsModule,
    NgTemplateOutlet,
    BrnAlertDialogContent,
    HlmAlertImports,
    HlmAlertDialogImports,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmInputImports,
    HlmSwitchImports,
    TranslatePipe,
  ],
  template: `
    <header class="mb-6">
      <h1 class="text-2xl font-semibold">{{ 'extensions.title' | t }}</h1>
      <p class="text-sm text-muted-foreground">{{ 'extensions.subtitle' | t }}</p>
    </header>

    <div class="grid gap-6">
      <section hlmCard>
        <div hlmCardHeader>
          <h2 hlmCardTitle>{{ 'extensions.players.title' | t }}</h2>
          <p hlmCardDescription>{{ 'extensions.players.description' | t }}</p>
        </div>
        <div hlmCardContent class="grid gap-4">
          <ul class="grid grid-cols-[minmax(0,1fr)] gap-2">
            @for (frontend of frontends(); track frontend.id) {
              <li
                class="flex min-w-0 flex-wrap items-center justify-between gap-3 rounded-md border p-3"
              >
                <div class="min-w-0 text-sm">
                  <p class="font-medium">
                    {{ frontend.name }}
                    <span class="text-muted-foreground">{{ frontend.version }}</span>
                    @if (frontend.isDefault) {
                      <span hlmBadge class="ml-1">{{ 'extensions.players.default' | t }}</span>
                    }
                    @if (frontend.builtIn) {
                      <span hlmBadge variant="secondary" class="ml-1">{{
                        'extensions.builtIn' | t
                      }}</span>
                    }
                  </p>
                  @if (frontend.description) {
                    <p class="text-xs text-muted-foreground">{{ frontend.description }}</p>
                  }
                  <p class="text-xs text-muted-foreground">
                    <a class="underline" [href]="frontend.path" target="_blank" rel="noopener">{{
                      frontend.path
                    }}</a>
                    @if (frontend.author) {
                      · {{ frontend.author }}
                    }
                    @if (frontend.license) {
                      · {{ frontend.license }}
                    }
                  </p>
                </div>
                <div class="flex gap-2">
                  @if (!frontend.isDefault) {
                    <button
                      hlmBtn
                      variant="outline"
                      size="sm"
                      type="button"
                      [disabled]="busy()"
                      (click)="makeDefault(frontend)"
                    >
                      {{ 'extensions.players.makeDefault' | t }}
                    </button>
                  }
                  @if (!frontend.builtIn) {
                    <button
                      hlmBtn
                      variant="outline"
                      size="sm"
                      type="button"
                      [disabled]="busy()"
                      (click)="uninstallFrontend(frontend)"
                    >
                      {{ 'extensions.uninstall' | t }}
                    </button>
                  }
                </div>
              </li>
            } @empty {
              <li class="text-sm text-muted-foreground">{{ 'extensions.players.none' | t }}</li>
            }
          </ul>
          <ng-container
            [ngTemplateOutlet]="installer"
            [ngTemplateOutletContext]="{ kind: 'frontend' }"
          />
        </div>
      </section>

      <section hlmCard>
        <div hlmCardHeader>
          <h2 hlmCardTitle>{{ 'extensions.list.title' | t }}</h2>
          <p hlmCardDescription>{{ 'extensions.list.description' | t }}</p>
        </div>
        <div hlmCardContent class="grid gap-4">
          @if (restartNeeded() || restarting()) {
            <div hlmAlert class="flex flex-wrap items-center justify-between gap-3">
              <p hlmAlertDescription>
                {{
                  (restarting()
                    ? 'extensions.list.restartWaiting'
                    : 'extensions.list.restartNeeded'
                  ) | t
                }}
              </p>
              <button
                hlmBtn
                size="sm"
                type="button"
                [disabled]="busy() || restarting()"
                (click)="restart()"
              >
                {{ 'extensions.list.restartNow' | t }}
              </button>
            </div>
          }
          <ul class="grid grid-cols-[minmax(0,1fr)] gap-2">
            @for (extension of extensions(); track extension.id) {
              <li
                class="flex min-w-0 flex-wrap items-center justify-between gap-3 rounded-md border p-3"
              >
                <div class="min-w-0 text-sm">
                  <p class="font-medium">
                    {{ extension.name }}
                    <span class="text-muted-foreground">{{ extension.version }}</span>
                    @if (extension.builtIn) {
                      <span hlmBadge variant="secondary" class="ml-1">{{
                        'extensions.builtIn' | t
                      }}</span>
                    }
                    @if (extension.status === 'FAILED') {
                      <span hlmBadge variant="destructive" class="ml-1">{{
                        'extensions.list.failed' | t
                      }}</span>
                    }
                    @if (extension.restartRequired) {
                      <span hlmBadge variant="outline" class="ml-1">{{
                        'extensions.list.restartRequired' | t
                      }}</span>
                    }
                  </p>
                  @if (extension.builtIn) {
                    <p class="text-xs text-muted-foreground">
                      {{ 'extensions.descriptions.' + extension.id | t }}
                    </p>
                  } @else if (extension.description) {
                    <p class="text-xs text-muted-foreground">{{ extension.description }}</p>
                  }
                  <p class="text-xs text-muted-foreground">
                    {{ 'extensions.list.routes' | t }}:
                    {{ [extension.basePath].concat(extension.routes).join(', ') }}
                  </p>
                  @if (extension.errorCode) {
                    <p class="text-xs text-destructive">{{ i18n.error(extension.errorCode) }}</p>
                  }
                </div>
                <div class="flex items-center gap-3">
                  <label class="flex items-center gap-2 text-sm">
                    @for (version of [switchVersion()]; track version) {
                      <hlm-switch
                        [checked]="extension.enabled"
                        [disabled]="busy()"
                        (checkedChange)="setEnabled(extension, $event)"
                      />
                    }
                    {{ 'extensions.list.enabled' | t }}
                  </label>
                  @if (!extension.builtIn) {
                    <button
                      hlmBtn
                      variant="outline"
                      size="sm"
                      type="button"
                      [disabled]="busy()"
                      (click)="uninstallExtension(extension)"
                    >
                      {{ 'extensions.uninstall' | t }}
                    </button>
                  }
                </div>
              </li>
            }
          </ul>
          <ng-container
            [ngTemplateOutlet]="installer"
            [ngTemplateOutletContext]="{ kind: 'extension' }"
          />
        </div>
      </section>
    </div>

    <ng-template #installer let-kind="kind">
      <div class="grid gap-2 rounded-md border border-dashed p-3">
        <p class="text-sm font-medium">{{ 'extensions.install.title' | t }}</p>
        <div class="flex flex-wrap items-center gap-2">
          <label hlmBtn variant="outline" size="sm" class="cursor-pointer">
            {{ 'extensions.install.zip' | t }}
            <input
              type="file"
              accept=".zip,application/zip"
              class="sr-only"
              [disabled]="busy()"
              (change)="pickFile(kind, $event)"
            />
          </label>
          <span class="text-xs text-muted-foreground">{{ 'extensions.install.or' | t }}</span>
          <input
            hlmInput
            class="min-w-48 flex-1"
            type="url"
            [placeholder]="'extensions.install.urlPlaceholder' | t"
            [ngModel]="urls[kind]"
            (ngModelChange)="urls[kind] = $event"
          />
          <button
            hlmBtn
            size="sm"
            type="button"
            [disabled]="busy() || !urls[kind].trim()"
            (click)="pending.set({ kind, file: null, url: urls[kind].trim() })"
          >
            {{ 'extensions.install.fromUrl' | t }}
          </button>
        </div>
      </div>
    </ng-template>

    <app-cloud-off-dialog
      [open]="confirmingCloudOff() !== null"
      (confirmed)="switchCloudOff()"
      (cancelled)="keepCloudOn()"
    />

    <hlm-alert-dialog [state]="pending() ? 'open' : 'closed'" (closed)="pending.set(null)">
      <hlm-alert-dialog-content *brnAlertDialogContent="let ctx">
        <hlm-alert-dialog-header>
          <h3 hlmAlertDialogTitle>{{ 'extensions.install.confirmTitle' | t }}</h3>
          <p hlmAlertDialogDescription>
            {{
              'extensions.install.' +
                (pending()?.kind === 'frontend' ? 'playerTrust' : 'extensionTrust') | t
            }}
          </p>
          <p class="text-sm font-medium break-all">{{ pending()?.file?.name ?? pending()?.url }}</p>
        </hlm-alert-dialog-header>
        <hlm-alert-dialog-footer>
          <button hlmAlertDialogCancel type="button" (click)="pending.set(null)">
            {{ 'common.cancel' | t }}
          </button>
          <button hlmAlertDialogAction type="button" (click)="install()">
            {{ 'extensions.install.confirm' | t }}
          </button>
        </hlm-alert-dialog-footer>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class ExtensionsPage implements OnInit {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);
  private readonly enabledExtensions = inject(EnabledExtensions);
  private readonly http = inject(HttpClient);

  protected readonly frontends = signal<Frontend[]>([]);
  protected readonly extensions = signal<Extension[]>([]);
  protected readonly busy = signal(false);
  protected readonly restarting = signal(false);
  protected readonly restartNeeded = computed(() =>
    this.extensions().some((e) => e.restartRequired),
  );
  protected readonly pending = signal<PendingInstall | null>(null);
  /** URLs typed in each installer, by kind. */
  protected readonly urls: Record<string, string> = { frontend: '', extension: '' };

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  private async reload(): Promise<void> {
    const [frontends, extensions] = await Promise.all([
      this.gql.query(FrontendsQuery),
      this.gql.query(ExtensionsQuery),
    ]);
    this.frontends.set(frontends.frontends);
    this.extensions.set(extensions.extensions);
  }

  protected pickFile(kind: Kind, event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    input.value = '';
    if (file) this.pending.set({ kind, file, url: null });
  }

  protected async install(): Promise<void> {
    const pending = this.pending();
    this.pending.set(null);
    if (!pending) return;
    await this.run(async () => {
      if (pending.file) {
        const form = new FormData();
        form.append('file', pending.file);
        const path = pending.kind === 'frontend' ? '/rest/v1/frontends' : '/rest/v1/extensions';
        await firstValueFrom(this.http.post(path, form));
      } else if (pending.kind === 'frontend') {
        await this.gql.mutate(InstallFrontendFromUrlMutation, { url: pending.url! });
      } else {
        await this.gql.mutate(InstallExtensionFromUrlMutation, { url: pending.url! });
      }

      this.urls[pending.kind] = '';
      toast.success(
        this.i18n.t(
          pending.kind === 'frontend'
            ? 'extensions.players.installed'
            : 'extensions.list.installed',
        ),
      );
    });
  }

  protected makeDefault(frontend: Frontend): Promise<void> {
    return this.run(() => this.gql.mutate(SetDefaultFrontendMutation, { id: frontend.id }));
  }

  protected uninstallFrontend(frontend: Frontend): Promise<void> {
    return this.run(() => this.gql.mutate(UninstallFrontendMutation, { id: frontend.id }));
  }

  /** Ultimate Cloud waiting for the confirmation to be switched off. */
  protected readonly confirmingCloudOff = signal<Extension | null>(null);
  /** Remakes the switches, so one left on after a cancelled confirmation shows on again. */
  protected readonly switchVersion = signal(0);

  protected async switchCloudOff(): Promise<void> {
    const extension = this.confirmingCloudOff();
    this.confirmingCloudOff.set(null);
    if (extension) await this.setEnabled(extension, false, true);
  }

  protected keepCloudOn(): void {
    if (!this.confirmingCloudOff()) return;
    this.confirmingCloudOff.set(null);
    this.switchVersion.update((v) => v + 1);
  }

  protected async setEnabled(
    extension: Extension,
    enabled: boolean,
    confirmed = false,
  ): Promise<void> {
    if (extension.id === 'ultimate-cloud' && !enabled && !confirmed) {
      this.confirmingCloudOff.set(extension);
      return;
    }
    await this.run(async () => {
      const result = await this.gql.mutate(SetExtensionEnabledMutation, {
        id: extension.id,
        enabled,
      });
      if (result.setExtensionEnabled.restartRequired)
        toast.info(this.i18n.t('extensions.list.restartRequiredHelp'));
      await this.enabledExtensions.refresh();
    });
  }

  /**
   * Restarts the server so installed extensions load: it stops and its supervisor starts it again. Waits until it
   * answers again (it is down for a few seconds), then shows the lists as they are now.
   */
  protected async restart(): Promise<void> {
    this.busy.set(true);
    try {
      await this.gql.mutate(RestartServerMutation);
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
      this.busy.set(false);
      return;
    }
    this.restarting.set(true);
    const started = Date.now();
    let wentDown = false;
    // Down first (up to 15 s), then up again (up to 2 min).
    while (Date.now() - started < 120_000) {
      await new Promise((resolve) => setTimeout(resolve, 1000));
      const up = await fetch('/health', { cache: 'no-store' })
        .then((r) => r.ok)
        .catch(() => false);
      if (!up) wentDown = true;
      else if (wentDown || Date.now() - started > 15_000) break;
    }
    this.restarting.set(false);
    this.busy.set(false);
    try {
      await this.reload();
      await this.enabledExtensions.refresh();
      toast.success(this.i18n.t('extensions.list.restartDone'));
    } catch {
      toast.error(this.i18n.t('extensions.list.restartUnreachable'));
    }
  }

  protected uninstallExtension(extension: Extension): Promise<void> {
    return this.run(() => this.gql.mutate(UninstallExtensionMutation, { id: extension.id }));
  }

  /** Runs a change and shows the lists as the server has them afterwards. */
  private async run(change: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    try {
      await change();
    } catch (error) {
      const apiError = ApiError.from(error);
      toast.error(this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code));
    } finally {
      await this.reload();
      this.busy.set(false);
    }
  }
}
