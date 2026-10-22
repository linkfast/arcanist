import { Component, computed, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSearch } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { TranslatePipe } from '../../core/i18n/i18n';
import { searchKey } from '../../shared/format';

/** What a user may do in a library: nothing, listen, or listen and add music (uploads). */
export type AccessLevel = 'none' | 'listen' | 'upload';

export interface AccessLibrary {
  id: string;
  name: string;
  visibleToAll: boolean;
}

/** Each library's level, by id. */
export type AccessLevels = Record<string, AccessLevel>;

const Levels: AccessLevel[] = ['none', 'listen', 'upload'];

/** The levels of a user's grants: given ones upload unless they are read only; those visible to all, the same. */
export function levelsOf(
  libraries: AccessLibrary[],
  libraryIds: readonly string[],
  readOnlyLibraryIds: readonly string[],
): AccessLevels {
  return Object.fromEntries(
    libraries.map((library) => [
      library.id,
      readOnlyLibraryIds.includes(library.id)
        ? 'listen'
        : library.visibleToAll || libraryIds.includes(library.id)
          ? 'upload'
          : 'none',
    ]),
  );
}

/** The levels as the API takes them: libraries given, and those only listened to. */
export function grantsOf(
  libraries: AccessLibrary[],
  levels: AccessLevels,
): { libraryIds: string[]; readOnlyLibraryIds: string[] } {
  return {
    libraryIds: libraries
      .filter((l) => !l.visibleToAll && levels[l.id] === 'upload')
      .map((l) => l.id),
    readOnlyLibraryIds: libraries.filter((l) => levels[l.id] === 'listen').map((l) => l.id),
  };
}

/**
 * A user's access to each library, as a list that grows down rather than across: a search when there are many,
 * a three-way choice per library (none, listen, listen and upload; libraries visible to all are always listened
 * to), and one click to set every library shown.
 */
@Component({
  selector: 'app-library-access-editor',
  imports: [FormsModule, NgIcon, HlmButtonImports, HlmInputImports, TranslatePipe],
  providers: [provideIcons({ lucideSearch })],
  template: `
    <div class="grid gap-3">
      @if (libraries().length > searchFrom) {
        <div class="relative">
          <ng-icon
            name="lucideSearch"
            class="pointer-events-none absolute top-1/2 left-2.5 -translate-y-1/2 text-muted-foreground"
          />
          <input
            hlmInput
            type="search"
            class="w-full pl-8"
            [attr.aria-label]="'users.accessEditor.search' | t"
            [placeholder]="'users.accessEditor.search' | t"
            [ngModel]="query()"
            (ngModelChange)="query.set($event)"
          />
        </div>
      }
      <div class="flex flex-wrap items-center gap-1 text-xs">
        <span class="mr-1 text-muted-foreground">{{
          (query().trim() ? 'users.accessEditor.allShown' : 'users.accessEditor.all') | t
        }}</span>
        @for (level of levels; track level) {
          <button
            hlmBtn
            variant="outline"
            size="sm"
            type="button"
            class="h-7 px-2 text-xs"
            [disabled]="disabled() || shown().length === 0"
            (click)="setAll(level)"
          >
            {{ 'users.access.' + level | t }}
          </button>
        }
      </div>
      <ul class="grid max-h-80 divide-y overflow-y-auto rounded-md border">
        @for (library of shown(); track library.id) {
          <li class="flex flex-wrap items-center justify-between gap-2 px-3 py-2">
            <span class="min-w-0 text-sm">
              <span class="block truncate">{{ library.name }}</span>
              @if (library.visibleToAll) {
                <span class="block text-xs text-muted-foreground">{{
                  'users.visibleToAll' | t
                }}</span>
              }
            </span>
            <span
              class="inline-flex shrink-0 rounded-md border p-0.5"
              role="radiogroup"
              [attr.aria-label]="library.name"
            >
              @for (level of levels; track level) {
                <button
                  type="button"
                  role="radio"
                  class="rounded px-2 py-1 text-xs transition-colors disabled:opacity-40"
                  [class]="
                    value()[library.id] === level
                      ? level === 'none'
                        ? 'bg-muted font-medium'
                        : level === 'listen'
                          ? 'bg-sky-600 font-medium text-white'
                          : 'bg-emerald-600 font-medium text-white'
                      : 'text-muted-foreground hover:bg-muted'
                  "
                  [attr.aria-checked]="value()[library.id] === level"
                  [disabled]="disabled() || (level === 'none' && library.visibleToAll)"
                  (click)="set(library.id, level)"
                >
                  {{ 'users.access.' + level | t }}
                </button>
              }
            </span>
          </li>
        } @empty {
          <li class="px-3 py-4 text-sm text-muted-foreground">
            {{ 'users.accessEditor.none' | t }}
          </li>
        }
      </ul>
      <p class="text-xs text-muted-foreground">
        {{
          (summary().listen ? 'users.accessEditor.summaryListen' : 'users.accessEditor.summary')
            | t: summary()
        }}
      </p>
    </div>
  `,
})
export class LibraryAccessEditor {
  readonly libraries = input.required<AccessLibrary[]>();
  readonly value = input.required<AccessLevels>();
  readonly disabled = input(false);
  readonly changed = output<AccessLevels>();

  /** A search appears from this many libraries. */
  protected readonly searchFrom = 6;
  protected readonly levels = Levels;
  protected readonly query = signal('');

  protected readonly shown = computed(() => {
    const words = searchKey(this.query()).trim().split(/\s+/).filter(Boolean);
    return this.libraries().filter((library) =>
      words.every((word) => searchKey(library.name).includes(word)),
    );
  });

  protected readonly summary = computed(() => {
    const levels = Object.values(this.value());
    return {
      count: levels.filter((l) => l !== 'none').length,
      total: this.libraries().length,
      listen: levels.filter((l) => l === 'listen').length,
    };
  });

  protected set(libraryId: string, level: AccessLevel): void {
    this.changed.emit({ ...this.value(), [libraryId]: level });
  }

  /** Every library shown to that level (those visible to all cannot go below listening). */
  protected setAll(level: AccessLevel): void {
    const next = { ...this.value() };
    for (const library of this.shown())
      next[library.id] = level === 'none' && library.visibleToAll ? 'listen' : level;
    this.changed.emit(next);
  }
}
