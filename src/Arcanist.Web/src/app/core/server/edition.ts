import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type ServerEdition = 'OPEN_SOURCE' | 'STANDARD' | 'ULTIMATE';

/**
 * What the server is, shown under ARCANIST in its mark: ULTIMATE with Ultimate Cloud unlocked, STANDARD with closed
 * source parts in use (a player such as Ultimate Web Player, an extension), OPENSOURCE otherwise; and whether the
 * 100% open source mode is on. From serverInfo, which needs no session (the sign-in page shows it too).
 */
@Injectable({ providedIn: 'root' })
export class Edition {
  private readonly http = inject(HttpClient);

  readonly edition = signal<ServerEdition | null>(null);
  readonly openSourceOnly = signal(false);

  /** The label under the wordmark, as Ultimate writes its editions. */
  readonly label = computed(() => {
    switch (this.edition()) {
      case 'ULTIMATE':
        return 'Ultimate';
      case 'STANDARD':
        return 'Standard';
      case 'OPEN_SOURCE':
        return 'Opensource';
      default:
        return null;
    }
  });

  async refresh(): Promise<void> {
    try {
      const info = await firstValueFrom(
        this.http.get<{ edition: ServerEdition; openSourceOnly: boolean }>('/rest/v1/server-info'),
      );
      this.edition.set(info.edition);
      this.openSourceOnly.set(info.openSourceOnly);
    } catch {
      // The mark keeps what it showed.
    }
  }
}
