import { Component, computed, inject, input, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCopy,
  lucideDownload,
  lucideExternalLink,
  lucideTriangleAlert,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ApiError } from '../core/api/rest';
import { Gql } from '../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../core/i18n/i18n';
import type { ServerUpdateFieldsFragment as ServerUpdate } from '../graphql/generated/graphql';
import { InstallServerUpdateMutation, ServerUpdateInstallQuery } from '../graphql/operations';
import { BytesPipe } from './format';

/** How many changes show before «See all». */
const FEW = 8;

type Change = ServerUpdate['changes'][number];

/**
 * What a newer version brings and how this server gets it, as the update notice and the Updates card show it: the
 * changes by area, the file of this installation, commands to copy. Text only: nothing from a release becomes markup.
 */
@Component({
  selector: 'app-update-details',
  imports: [HlmButtonImports, HlmSpinnerImports, NgIcon, TranslatePipe, BytesPipe],
  providers: [
    provideIcons({ lucideCopy, lucideDownload, lucideExternalLink, lucideTriangleAlert }),
  ],
  template: `
    <div class="grid gap-4">
      <section class="grid gap-2">
        <h4 class="text-sm font-semibold">{{ 'updates.details.changes' | t }}</h4>
        @if (update().changes.length > 0) {
          <div class="grid gap-3 rounded-md border p-3">
            @for (group of groups(); track group.area) {
              <div class="grid gap-1">
                <p class="text-xs font-medium tracking-wide text-muted-foreground uppercase">
                  {{ group.area ?? ('updates.details.general' | t) }}
                </p>
                <ul class="grid gap-1">
                  @for (change of group.changes; track $index) {
                    <li class="relative pl-3.5 text-sm break-words">
                      <span class="absolute left-0 text-muted-foreground" aria-hidden="true"
                        >•</span
                      >
                      @if (change.url) {
                        <a
                          class="hover:underline"
                          [href]="change.url"
                          target="_blank"
                          rel="noopener noreferrer"
                          >{{ change.text }}</a
                        >
                      } @else {
                        {{ change.text }}
                      }
                    </li>
                  }
                </ul>
              </div>
            }
          </div>
          @if (hidden() > 0) {
            <button
              hlmBtn
              variant="link"
              size="sm"
              type="button"
              class="h-auto justify-self-start px-0"
              (click)="all.set(true)"
            >
              {{ 'updates.details.seeAll' | t: { count: update().changes.length } }}
            </button>
          } @else if (update().moreChanges) {
            <a
              class="text-sm underline"
              [href]="update().latest?.url"
              target="_blank"
              rel="noopener"
              >{{ 'updates.details.more' | t }}</a
            >
          }
        } @else {
          <p class="text-sm text-muted-foreground">
            {{ 'updates.details.noChanges' | t }}
            <a class="underline" [href]="update().latest?.url" target="_blank" rel="noopener">{{
              'updates.details.releasePage' | t
            }}</a>
          </p>
        }
      </section>

      @if (update().install; as install) {
        <section class="grid gap-2">
          <h4 class="text-sm font-semibold">{{ 'updates.details.how' | t }}</h4>
          <p class="text-sm text-muted-foreground">
            {{ 'updates.howTo.' + update().installKind | t }}
          </p>

          @if (install.selfUpdate === 'PANEL') {
            <div class="grid gap-2 rounded-md border p-3">
              <p class="text-sm">{{ 'updates.details.selfUpdate' | t }}</p>
              @if (installing()) {
                <p class="flex items-center gap-2 text-sm font-medium" role="status">
                  <hlm-spinner class="size-4" />
                  {{ 'updates.details.state.' + installing() | t }}
                </p>
              } @else {
                <button
                  hlmBtn
                  type="button"
                  class="justify-self-start gap-1"
                  (click)="installNow()"
                >
                  <ng-icon name="lucideDownload" />
                  {{ 'updates.details.installNow' | t }}
                </button>
              }
              @if (installError(); as code) {
                <p class="text-sm text-destructive" role="alert">{{ i18n.error(code) }}</p>
              }
            </div>
          }

          @if (install.dataInContainer) {
            <p
              class="flex items-start gap-2 rounded-md border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive"
              role="alert"
            >
              <ng-icon name="lucideTriangleAlert" class="mt-0.5 shrink-0" />
              {{ 'updates.details.dataInContainer' | t }}
            </p>
          } @else if (install.librariesOutsideMounts.length > 0) {
            <p
              class="flex items-start gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-800 dark:text-amber-300"
              role="alert"
            >
              <ng-icon name="lucideTriangleAlert" class="mt-0.5 shrink-0" />
              {{
                'updates.details.librariesOutside'
                  | t: { libraries: install.librariesOutsideMounts.join(', ') }
              }}
            </p>
          }

          @for (download of install.downloads; track download.url) {
            <a
              hlmBtn
              [variant]="$first && !install.updatesItself ? 'default' : 'outline'"
              size="sm"
              class="h-auto justify-start gap-2 py-2 text-left whitespace-normal"
              [href]="download.url"
              rel="noopener"
            >
              <ng-icon name="lucideDownload" class="shrink-0" />
              <span class="min-w-0 break-all">{{ download.name }}</span>
              <span class="ml-auto shrink-0 font-normal opacity-70">{{
                download.size | bytes
              }}</span>
            </a>
          }

          @if (install.commands.length > 1) {
            <div class="flex flex-wrap gap-1" role="tablist">
              @for (command of install.commands; track command.kind) {
                <button
                  hlmBtn
                  size="sm"
                  type="button"
                  role="tab"
                  [attr.aria-selected]="selected()?.kind === command.kind"
                  [variant]="selected()?.kind === command.kind ? 'secondary' : 'ghost'"
                  (click)="chosen.set(command.kind)"
                >
                  {{ 'updates.details.commands.' + command.kind | t }}
                </button>
              }
            </div>
          }
          @if (!install.dataInContainer || selected()?.kind !== 'DOCKER_RUN') {
            @if (selected(); as command) {
              <p class="text-xs text-muted-foreground">
                {{ 'updates.details.commandHelp.' + command.kind | t }}
              </p>
              <div class="relative min-w-0 rounded-md border bg-muted">
                <pre
                  class="max-h-44 overflow-auto p-3 pr-20 font-mono text-xs whitespace-pre"
                ><code>{{ command.script }}</code></pre>
                <button
                  hlmBtn
                  variant="outline"
                  size="sm"
                  type="button"
                  class="absolute top-2 right-2 gap-1 bg-background"
                  (click)="copy(command.script)"
                >
                  <ng-icon name="lucideCopy" />
                  {{ 'common.copy' | t }}
                </button>
              </div>
            }
          }

          @if (install.mounts.length > 0 && selected()?.kind === 'DOCKER_RUN') {
            <p class="text-xs text-muted-foreground">
              {{ 'updates.details.mounts' | t: { mounts: install.mounts.join(', ') } }}
            </p>
          }

          @if (install.guideUrl) {
            <a
              class="inline-flex items-center gap-1 justify-self-start text-sm underline"
              [href]="install.guideUrl"
              target="_blank"
              rel="noopener"
            >
              {{ 'updates.details.guide' | t }}
              <ng-icon name="lucideExternalLink" class="text-xs" />
            </a>
          }
        </section>
      }
    </div>
  `,
})
export class UpdateDetails {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);

  readonly update = input.required<ServerUpdate>();

  protected readonly all = signal(false);
  protected readonly chosen = signal<string | null>(null);
  /** Where installing from the panel is; null when it is not. */
  protected readonly installing = signal<string | null>(null);
  protected readonly installError = signal<string | null>(null);

  protected readonly hidden = computed(() =>
    this.all() ? 0 : Math.max(0, this.update().changes.length - FEW),
  );

  /** The changes in view, by area: areas in the order they first appear, those without one last. */
  protected readonly groups = computed(() => {
    const changes = this.update().changes;
    const shown = this.all() ? changes : changes.slice(0, FEW);
    const groups = new Map<string | null, Change[]>();
    for (const change of shown) {
      const area = change.area ?? null;
      groups.set(area, [...(groups.get(area) ?? []), change]);
    }
    return [...groups.entries()]
      .sort(([a], [b]) => Number(a === null) - Number(b === null))
      .map(([area, changes]) => ({ area, changes }));
  });

  protected readonly selected = computed(() => {
    const commands = this.update().install?.commands ?? [];
    return commands.find((c) => c.kind === this.chosen()) ?? commands[0] ?? null;
  });

  /** Installs the release from the panel, then follows it until the new version answers. */
  protected async installNow(): Promise<void> {
    this.installError.set(null);
    this.installing.set('DOWNLOADING');
    try {
      const started = (await this.gql.mutate(InstallServerUpdateMutation)).installServerUpdate;
      await this.follow(started.version);
    } catch (error) {
      this.installing.set(null);
      this.installError.set(ApiError.from(error).code);
    }
  }

  private async follow(version: string | null | undefined): Promise<void> {
    const deadline = Date.now() + 25 * 60 * 1000;
    let lost = false;
    while (Date.now() < deadline) {
      await new Promise((resolve) => setTimeout(resolve, 2000));
      try {
        const status = (await this.gql.query(ServerUpdateInstallQuery)).serverUpdateInstall;
        if (status.state === 'FAILED')
          throw new ApiError(status.errorCode ?? 'update_install_failed');
        // A server that was gone and is back, or that says it finished, is the new one.
        if (lost || (status.state === 'IDLE' && status.version === version)) {
          location.reload();
          return;
        }
        if (status.state !== 'IDLE') this.installing.set(status.state);
      } catch (error) {
        const apiError = ApiError.from(error);
        if (
          apiError.code === 'update_install_failed' ||
          apiError.code === 'update_checksum_mismatch'
        )
          throw error;
        // Not answering: it is being replaced.
        lost = true;
        this.installing.set('RESTARTING');
      }
    }
    throw new ApiError('update_install_failed');
  }

  protected async copy(text: string): Promise<void> {
    await navigator.clipboard.writeText(text);
    toast.success(this.i18n.t('common.copied'));
  }
}
