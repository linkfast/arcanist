import { Injectable, inject, signal } from '@angular/core';
import { Gql } from '../graphql/gql.service';
import { ExtensionsQuery } from '../../graphql/operations';

/**
 * Which extensions are on, for parts of the panel that only exist with them (the mStream page). Administrators
 * only; pages that switch extensions refresh it.
 */
@Injectable({ providedIn: 'root' })
export class EnabledExtensions {
  private readonly gql = inject(Gql);

  readonly ids = signal<ReadonlySet<string>>(new Set());

  has(id: string): boolean {
    return this.ids().has(id);
  }

  async refresh(): Promise<void> {
    try {
      const result = await this.gql.query(ExtensionsQuery);
      this.ids.set(new Set(result.extensions.filter((e) => e.enabled).map((e) => e.id)));
    } catch {
      // The menu just keeps what it showed.
    }
  }
}
