import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type InstallKind =
  | 'WINDOWS'
  | 'WINDOWS_SERVICE'
  | 'MAC_OS'
  | 'DOCKER'
  | 'SYNOLOGY'
  | 'QNAP'
  | 'OPEN_MEDIA_VAULT'
  | 'TRUE_NAS'
  | 'UNRAID'
  | 'ROCKSTOR'
  | 'DEBIAN_PACKAGE'
  | 'SYSTEMD'
  | 'OTHER';

/** Where and how the server runs (serverHost): how it was installed, its system, user and data folder. */
export interface ServerHost {
  kind: InstallKind;
  os: 'WINDOWS' | 'MAC_OS' | 'LINUX' | 'OTHER';
  architecture: string;
  container: boolean;
  supervised: boolean;
  user: string;
  dataDirectory: string;
}

/**
 * The host the server runs on, so pages give the steps that fit it when something needs doing outside Arcanist
 * (see shared/host-help.ts). Administrators read it from the API; the setup wizard gets it with the setup status.
 */
@Injectable({ providedIn: 'root' })
export class Host {
  private readonly http = inject(HttpClient);
  private loading: Promise<void> | null = null;

  readonly current = signal<ServerHost | null>(null);

  set(host: ServerHost | null | undefined): void {
    if (host) this.current.set(host);
  }

  /** Reads it once; a user who is not an administrator simply gets no host-specific steps. */
  load(): Promise<void> {
    if (this.current()) return Promise.resolve();
    this.loading ??= firstValueFrom(this.http.get<ServerHost>('/rest/v1/server/host'))
      .then((host) => this.current.set(host))
      // Not an administrator (or setup is pending): no host-specific steps, and no asking again.
      .catch(() => undefined);
    return this.loading;
  }
}
