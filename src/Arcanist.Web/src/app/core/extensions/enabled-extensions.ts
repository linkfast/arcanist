import { Injectable, computed, inject, signal } from '@angular/core';
import { Gql } from '../graphql/gql.service';
import type { ExtensionFieldsFragment } from '../../graphql/generated/graphql';
import { ExtensionsQuery } from '../../graphql/operations';

/**
 * The extensions and which are on, for parts of the panel that only exist with them (the mStream page, the settings
 * pages of installed extensions). Administrators only; pages that switch extensions refresh it.
 */
@Injectable({ providedIn: 'root' })
export class EnabledExtensions {
  private readonly gql = inject(Gql);

  readonly all = signal<readonly ExtensionFieldsFragment[]>([]);
  readonly ids = computed<ReadonlySet<string>>(
    () =>
      new Set(
        this.all()
          .filter((e) => e.enabled)
          .map((e) => e.id),
      ),
  );

  /** Installed extensions with a settings page, running: shown in the panel under Extensions. */
  readonly pages = computed(() =>
    this.all().filter(
      (e) => !e.builtIn && e.enabled && e.status === 'RUNNING' && !e.restartRequired && e.adminUrl,
    ),
  );

  has(id: string): boolean {
    return this.ids().has(id);
  }

  async refresh(): Promise<void> {
    try {
      const result = await this.gql.query(ExtensionsQuery);
      this.all.set(result.extensions);
    } catch {
      // The menu just keeps what it showed.
    }
  }
}
