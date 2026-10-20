import { HttpClient, HttpParams } from '@angular/common/http';
import {
  Component,
  OnDestroy,
  OnInit,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { BrnAlertDialogContent } from '@spartan-ng/brain/alert-dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmProgressImports } from '@spartan-ng/helm/progress';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { firstValueFrom } from 'rxjs';
import { ApiError } from '../../core/api/rest';
import { EnabledExtensions } from '../../core/extensions/enabled-extensions';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import { LibrariesQuery } from '../../graphql/operations';

/** What an extension describes (IExtensionAdminPage in the SDK). */
type Tone = 'NEUTRAL' | 'GOOD' | 'WARNING' | 'BAD';
interface Block {
  kind: 'FIELD' | 'STATUS' | 'NOTICE' | 'PROGRESS' | 'LIST';
  key?: string | null;
  label?: string | null;
  help?: string | null;
  type?:
    | 'TEXT'
    | 'SECRET'
    | 'NUMBER'
    | 'SWITCH'
    | 'SELECT'
    | 'MULTI_SELECT'
    | 'TEXT_AREA'
    | 'URL'
    | 'LIBRARY'
    | null;
  value?: unknown;
  options?: { value: string; label: string }[] | null;
  placeholder?: string | null;
  min?: number | null;
  max?: number | null;
  readOnly?: boolean;
  text?: string | null;
  tone?: Tone;
  percent?: number | null;
  items?: string[] | null;
}
interface Action {
  id: string;
  label: string;
  style: 'PRIMARY' | 'SECONDARY' | 'DESTRUCTIVE';
  confirm?: string | null;
}
interface Section {
  id: string;
  title: string;
  description?: string | null;
  blocks?: Block[] | null;
  actions?: Action[] | null;
}
interface Page {
  sections: Section[];
  refreshSeconds?: number | null;
}
interface Result {
  ok: boolean;
  message?: string | null;
  errors?: Record<string, string> | null;
}

/**
 * The settings page of an installed extension, drawn by the panel from what the extension describes: fields,
 * statuses, notices, progress and actions, with the panel's own components and look. Nothing of the extension runs
 * here.
 */
@Component({
  selector: 'app-extension-page',
  imports: [
    FormsModule,
    RouterLink,
    BrnAlertDialogContent,
    HlmAlertImports,
    HlmAlertDialogImports,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmCheckboxImports,
    HlmFieldImports,
    HlmInputImports,
    HlmNativeSelectImports,
    HlmProgressImports,
    HlmSpinnerImports,
    HlmSwitchImports,
    TranslatePipe,
  ],
  host: { class: 'grid gap-6' },
  template: `
    @if (extension(); as current) {
      <header class="flex items-center gap-3">
        @if (current.iconUrl) {
          <img [src]="current.iconUrl" alt="" class="size-9 rounded-lg" />
        }
        <div class="min-w-0">
          <h1 class="truncate text-2xl font-semibold">{{ current.name }}</h1>
          <p class="text-sm text-muted-foreground">
            {{ current.version }}
            @if (current.author) {
              · {{ current.author }}
            }
          </p>
        </div>
      </header>

      @if (page(); as p) {
        @for (section of p.sections; track section.id) {
          <section hlmCard>
            <div hlmCardHeader>
              <h2 hlmCardTitle>{{ section.title }}</h2>
              @if (section.description) {
                <p hlmCardDescription>{{ section.description }}</p>
              }
            </div>
            <div hlmCardContent class="grid gap-4">
              @for (block of section.blocks ?? []; track $index) {
                @switch (block.kind) {
                  @case ('FIELD') {
                    <div hlmField>
                      @if (block.type === 'SWITCH') {
                        <label class="flex items-start justify-between gap-3 text-sm">
                          <span>
                            {{ block.label }}
                            @if (block.help) {
                              <span class="block text-xs text-muted-foreground">{{
                                block.help
                              }}</span>
                            }
                          </span>
                          <hlm-switch
                            [checked]="!!values()[block.key!]"
                            [disabled]="block.readOnly || busy()"
                            (checkedChange)="set(block.key!, $event)"
                          />
                        </label>
                      } @else {
                        <label hlmFieldLabel [for]="'f-' + block.key">{{ block.label }}</label>
                        @switch (block.type) {
                          @case ('TEXT_AREA') {
                            <textarea
                              hlmInput
                              rows="4"
                              class="min-h-24"
                              [id]="'f-' + block.key"
                              [placeholder]="block.placeholder ?? ''"
                              [readOnly]="!!block.readOnly"
                              [ngModel]="values()[block.key!]"
                              (ngModelChange)="set(block.key!, $event)"
                            ></textarea>
                          }
                          @case ('SELECT') {
                            <select
                              hlmNativeSelect
                              [id]="'f-' + block.key"
                              [disabled]="!!block.readOnly"
                              [ngModel]="values()[block.key!]"
                              (ngModelChange)="set(block.key!, $event)"
                            >
                              @for (option of block.options ?? []; track option.value) {
                                <option [value]="option.value">{{ option.label }}</option>
                              }
                            </select>
                          }
                          @case ('LIBRARY') {
                            <select
                              hlmNativeSelect
                              [id]="'f-' + block.key"
                              [disabled]="!!block.readOnly"
                              [ngModel]="values()[block.key!] ?? ''"
                              (ngModelChange)="set(block.key!, $event || null)"
                            >
                              <option value="">{{ 'extensions.page.noLibrary' | t }}</option>
                              @for (library of libraries(); track library.id) {
                                <option [value]="library.id">{{ library.name }}</option>
                              }
                            </select>
                          }
                          @case ('MULTI_SELECT') {
                            <div class="grid gap-2 sm:grid-cols-2">
                              @for (option of block.options ?? []; track option.value) {
                                <label class="flex items-center gap-2 text-sm">
                                  <hlm-checkbox
                                    [checked]="chosen(block.key!, option.value)"
                                    [disabled]="!!block.readOnly"
                                    (checkedChange)="choose(block.key!, option.value, $event)"
                                  />
                                  {{ option.label }}
                                </label>
                              }
                            </div>
                          }
                          @default {
                            <input
                              hlmInput
                              [id]="'f-' + block.key"
                              [type]="inputType(block)"
                              [placeholder]="
                                block.type === 'SECRET'
                                  ? ('extensions.page.secretKept' | t)
                                  : (block.placeholder ?? '')
                              "
                              [attr.min]="block.min ?? null"
                              [attr.max]="block.max ?? null"
                              [readOnly]="!!block.readOnly"
                              [ngModel]="values()[block.key!]"
                              (ngModelChange)="
                                set(block.key!, block.type === 'NUMBER' ? toNumber($event) : $event)
                              "
                            />
                          }
                        }
                        @if (block.help) {
                          <p hlmFieldDescription>{{ block.help }}</p>
                        }
                      }
                      @if (errors()[block.key!]; as error) {
                        <p class="text-sm text-destructive">{{ error }}</p>
                      }
                    </div>
                  }
                  @case ('STATUS') {
                    <div class="flex flex-wrap items-center justify-between gap-2 text-sm">
                      <span class="text-muted-foreground">{{ block.label }}</span>
                      <span
                        hlmBadge
                        [variant]="badge(block.tone)"
                        [class]="toneClass(block.tone)"
                        >{{ block.text }}</span
                      >
                      @if (block.help) {
                        <p class="w-full text-xs text-muted-foreground">{{ block.help }}</p>
                      }
                    </div>
                  }
                  @case ('NOTICE') {
                    <div
                      hlmAlert
                      [variant]="block.tone === 'BAD' ? 'destructive' : 'default'"
                      [class]="toneClass(block.tone)"
                    >
                      <p hlmAlertDescription class="whitespace-pre-line">{{ block.text }}</p>
                    </div>
                  }
                  @case ('PROGRESS') {
                    <div class="grid gap-1 text-sm">
                      <div class="flex justify-between gap-2">
                        <span>{{ block.label }}</span>
                        <span class="text-muted-foreground">{{
                          block.text ?? (block.percent ?? 0) + ' %'
                        }}</span>
                      </div>
                      <hlm-progress [value]="block.percent ?? 0" class="h-2"
                        ><hlm-progress-indicator
                      /></hlm-progress>
                    </div>
                  }
                  @case ('LIST') {
                    <div class="grid gap-1 text-sm">
                      <span class="text-muted-foreground">{{ block.label }}</span>
                      <ul class="grid gap-0.5">
                        @for (item of block.items ?? []; track $index) {
                          <li class="truncate font-mono text-xs">{{ item }}</li>
                        } @empty {
                          <li class="text-xs text-muted-foreground">
                            {{ 'extensions.page.emptyList' | t }}
                          </li>
                        }
                      </ul>
                    </div>
                  }
                }
              }
              @if (section.actions?.length) {
                <div class="flex flex-wrap gap-2">
                  @for (action of section.actions; track action.id) {
                    <button
                      hlmBtn
                      size="sm"
                      type="button"
                      [variant]="
                        action.style === 'PRIMARY'
                          ? 'default'
                          : action.style === 'DESTRUCTIVE'
                            ? 'destructive'
                            : 'outline'
                      "
                      [disabled]="busy()"
                      (click)="ask(action)"
                    >
                      {{ action.label }}
                    </button>
                  }
                </div>
              }
            </div>
          </section>
        }

        @if (editable()) {
          <div class="flex items-center gap-3">
            <button hlmBtn type="button" [disabled]="busy() || !dirty()" (click)="save()">
              {{ 'common.save' | t }}
            </button>
            @if (dirty()) {
              <span class="text-xs text-muted-foreground">{{ 'extensions.page.unsaved' | t }}</span>
            }
          </div>
        }
      } @else if (failed()) {
        <div hlmAlert variant="destructive">
          <p hlmAlertDescription>{{ i18n.error(failed()) }}</p>
        </div>
      } @else {
        <hlm-spinner class="size-5" />
      }
    } @else if (loaded()) {
      <p class="text-sm text-muted-foreground">{{ 'extensions.page.unavailable' | t }}</p>
      <a hlmBtn variant="outline" size="sm" class="justify-self-start" routerLink="/extensions">{{
        'nav.extensions' | t
      }}</a>
    }

    <hlm-alert-dialog [state]="confirming() ? 'open' : 'closed'" (closed)="confirming.set(null)">
      <hlm-alert-dialog-content *brnAlertDialogContent="let ctx">
        <hlm-alert-dialog-header>
          <h3 hlmAlertDialogTitle>{{ confirming()?.label }}</h3>
          <p hlmAlertDialogDescription>{{ confirming()?.confirm }}</p>
        </hlm-alert-dialog-header>
        <hlm-alert-dialog-footer>
          <button hlmAlertDialogCancel type="button" (click)="confirming.set(null)">
            {{ 'common.cancel' | t }}
          </button>
          <button hlmAlertDialogAction type="button" (click)="confirmed()">
            {{ confirming()?.label }}
          </button>
        </hlm-alert-dialog-footer>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class ExtensionPage implements OnInit, OnDestroy {
  readonly id = input.required<string>();
  protected readonly i18n = inject(I18n);
  private readonly extensions = inject(EnabledExtensions);
  private readonly http = inject(HttpClient);
  private readonly gql = inject(Gql);

  protected readonly extension = computed(
    () => this.extensions.pages().find((e) => e.id === this.id()) ?? null,
  );
  protected readonly loaded = computed(() => this.extensions.all().length > 0);
  protected readonly page = signal<Page | null>(null);
  protected readonly failed = signal<string | null>(null);
  protected readonly values = signal<Record<string, unknown>>({});
  protected readonly errors = signal<Record<string, string>>({});
  protected readonly dirty = signal(false);
  protected readonly busy = signal(false);
  protected readonly confirming = signal<Action | null>(null);
  protected readonly libraries = signal<{ id: string; name: string }[]>([]);
  protected readonly editable = computed(() =>
    (this.page()?.sections ?? []).some((s) =>
      (s.blocks ?? []).some((b) => b.kind === 'FIELD' && !b.readOnly),
    ),
  );

  private refresh: ReturnType<typeof setInterval> | null = null;

  constructor() {
    // Another extension's page: start over.
    effect(() => {
      this.id();
      this.page.set(null);
      this.dirty.set(false);
      void this.load();
    });
  }

  async ngOnInit(): Promise<void> {
    if (!this.loaded()) await this.extensions.refresh();
  }

  ngOnDestroy(): void {
    if (this.refresh) clearInterval(this.refresh);
  }

  protected inputType(block: Block): string {
    return block.type === 'SECRET'
      ? 'password'
      : block.type === 'NUMBER'
        ? 'number'
        : block.type === 'URL'
          ? 'url'
          : 'text';
  }

  protected toNumber(value: unknown): number | null {
    const number = Number(value);
    return value === '' || value === null || Number.isNaN(number) ? null : number;
  }

  protected set(key: string, value: unknown): void {
    this.values.update((values) => ({ ...values, [key]: value }));
    this.errors.update(({ [key]: _, ...rest }) => rest);
    this.dirty.set(true);
  }

  protected chosen(key: string, option: string): boolean {
    const value = this.values()[key];
    return Array.isArray(value) && value.includes(option);
  }

  protected choose(key: string, option: string, on: boolean): void {
    const current = (this.values()[key] as string[] | undefined) ?? [];
    this.set(
      key,
      on ? [...current.filter((v) => v !== option), option] : current.filter((v) => v !== option),
    );
  }

  protected badge(tone?: Tone): 'default' | 'secondary' | 'destructive' | 'outline' {
    return tone === 'BAD' ? 'destructive' : tone === 'GOOD' ? 'secondary' : 'outline';
  }

  protected toneClass(tone?: Tone): string {
    return tone === 'GOOD'
      ? 'text-emerald-700 dark:text-emerald-400'
      : tone === 'WARNING'
        ? 'text-amber-700 dark:text-amber-400'
        : '';
  }

  protected ask(action: Action): void {
    if (action.confirm) this.confirming.set(action);
    else void this.run(action);
  }

  protected confirmed(): void {
    const action = this.confirming();
    this.confirming.set(null);
    if (action) void this.run(action);
  }

  protected async save(): Promise<void> {
    await this.send(
      this.http.put<Result>(
        this.base(),
        { values: this.editedValues() },
        { params: this.params() },
      ),
    );
  }

  private async run(action: Action): Promise<void> {
    await this.send(
      this.http.post<Result>(
        `${this.base()}/actions/${encodeURIComponent(action.id)}`,
        { values: this.editedValues() },
        {
          params: this.params(),
        },
      ),
    );
  }

  private async send(request: import('rxjs').Observable<Result>): Promise<void> {
    this.busy.set(true);
    try {
      const result = await firstValueFrom(request);
      this.errors.set(result.errors ?? {});
      if (result.ok) {
        if (result.message) toast.success(result.message);
        this.dirty.set(false);
        await this.load(true);
      } else if (result.message) {
        toast.error(result.message);
      }
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }

  /** The fields' values to send: an empty secret means "keep the one there is". */
  private editedValues(): Record<string, unknown> {
    const secrets = new Set(
      (this.page()?.sections ?? [])
        .flatMap((s) => s.blocks ?? [])
        .filter((b) => b.type === 'SECRET')
        .map((b) => b.key),
    );
    const editable = new Set(
      (this.page()?.sections ?? [])
        .flatMap((s) => s.blocks ?? [])
        .filter((b) => b.kind === 'FIELD' && !b.readOnly)
        .map((b) => b.key),
    );
    return Object.fromEntries(
      Object.entries(this.values()).filter(
        ([key, value]) => editable.has(key) && !(secrets.has(key) && !value),
      ),
    );
  }

  private base(): string {
    return `/rest/v1/extensions/${encodeURIComponent(this.id())}/admin`;
  }

  private params(): HttpParams {
    return new HttpParams().set('language', this.i18n.language());
  }

  /** Reads the page; while the administrator edits, what they typed stays. */
  private async load(force = false): Promise<void> {
    if (this.dirty() && !force) return;
    try {
      const page = await firstValueFrom(
        this.http.get<Page>(this.base(), { params: this.params() }),
      );
      this.failed.set(null);
      this.page.set(page);
      const values: Record<string, unknown> = {};
      for (const block of page.sections.flatMap((s) => s.blocks ?? []))
        if (block.kind === 'FIELD' && block.key)
          values[block.key] = block.type === 'SECRET' ? '' : block.value;
      this.values.set(values);
      if (
        page.sections.some((s) => (s.blocks ?? []).some((b) => b.type === 'LIBRARY')) &&
        !this.libraries().length
      )
        this.libraries.set(
          (await this.gql.query(LibrariesQuery)).libraries.map((l) => ({ id: l.id, name: l.name })),
        );
      if (this.refresh) clearInterval(this.refresh);
      this.refresh = page.refreshSeconds
        ? setInterval(() => void this.load(), page.refreshSeconds * 1000)
        : null;
    } catch (error) {
      this.failed.set(ApiError.from(error).code);
    }
  }
}
