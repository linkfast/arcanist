import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  ApiError,
  DatabaseProvider,
  ServerDirectory,
  ServerDirectoryListing,
  SetupFfmpegCheck,
  SetupRequest,
  SetupStatus,
} from '../api/rest';
import type {
  BrowseDlnaMediaServerQuery,
  DlnaMediaServersQuery,
  MstreamAnalysisFieldsFragment,
} from '../../graphql/generated/graphql';
import type { MstreamSourceInput } from '../../shared/mstream-import';

type DlnaMediaServer = DlnaMediaServersQuery['dlnaMediaServers'][number];
type DlnaFolderContents = BrowseDlnaMediaServerQuery['browseDlnaMediaServer'];

/** Talks to /rest/v1/setup. From other devices every call carries the one-time setup code. */
@Injectable({ providedIn: 'root' })
export class SetupService {
  private readonly http = inject(HttpClient);

  readonly status = signal<SetupStatus | null>(null);
  readonly code = signal('');

  async loadStatus(): Promise<SetupStatus> {
    const status = await firstValueFrom(this.http.get<SetupStatus>('/rest/v1/setup/status'));
    this.status.set(status);
    return status;
  }

  /** Checks the code by listing the file system roots, which requires it. */
  async verifyCode(code: string): Promise<boolean> {
    this.code.set(code.trim());
    try {
      await this.directories(null);
      return true;
    } catch (error) {
      if (ApiError.from(error).code === 'setup_code_invalid') return false;
      throw error;
    }
  }

  async testDatabase(
    provider: DatabaseProvider,
    connectionString: string | null,
  ): Promise<{ success: boolean; error: string | null }> {
    return this.call(
      this.http.post<{ success: boolean; error: string | null }>(
        '/rest/v1/setup/database/test',
        { provider, connectionString },
        { headers: this.headers() },
      ),
    );
  }

  directories(path: string | null): Promise<ServerDirectoryListing> {
    const params = path ? new HttpParams().set('path', path) : undefined;
    return this.call(
      this.http.get<ServerDirectoryListing>('/rest/v1/setup/directories', {
        params,
        headers: this.headers(),
      }),
    );
  }

  createDirectory(parent: string, name: string): Promise<ServerDirectory> {
    return this.call(
      this.http.post<ServerDirectory>(
        '/rest/v1/setup/directories',
        { parent, name },
        { headers: this.headers() },
      ),
    );
  }

  /** The ffmpeg at the path, or the one the server would find by itself when path is null. */
  checkFfmpeg(path: string | null): Promise<SetupFfmpegCheck> {
    const params = path ? new HttpParams().set('path', path) : undefined;
    return this.call(
      this.http.get<SetupFfmpegCheck>('/rest/v1/setup/ffmpeg', { params, headers: this.headers() }),
    );
  }

  /** Reads an mStream installation, to choose what to import. */
  analyzeMstream(source: MstreamSourceInput): Promise<MstreamAnalysisFieldsFragment> {
    return this.call(
      this.http.post<MstreamAnalysisFieldsFragment>('/rest/v1/setup/mstream/analyze', source, {
        headers: this.headers(),
      }),
    );
  }

  /** DLNA media servers on the local networks, to copy from once set up. */
  dlnaServers(): Promise<DlnaMediaServer[]> {
    return this.call(
      this.http.get<DlnaMediaServer[]>('/rest/v1/setup/dlna/servers', { headers: this.headers() }),
    );
  }

  dlnaServer(location: string): Promise<DlnaMediaServer> {
    return this.call(
      this.http.get<DlnaMediaServer>('/rest/v1/setup/dlna/server', {
        params: new HttpParams().set('location', location),
        headers: this.headers(),
      }),
    );
  }

  dlnaBrowse(location: string, folderId: string | null): Promise<DlnaFolderContents> {
    let params = new HttpParams().set('location', location);
    if (folderId) params = params.set('folderId', folderId);
    return this.call(
      this.http.get<DlnaFolderContents>('/rest/v1/setup/dlna/browse', {
        params,
        headers: this.headers(),
      }),
    );
  }

  complete(request: SetupRequest): Promise<unknown> {
    return this.call(
      this.http.post('/rest/v1/setup/complete', request, { headers: this.headers() }),
    );
  }

  private headers(): HttpHeaders {
    return this.code() ? new HttpHeaders({ 'X-Setup-Code': this.code() }) : new HttpHeaders();
  }

  private async call<T>(request: import('rxjs').Observable<T>): Promise<T> {
    try {
      return await firstValueFrom(request);
    } catch (error) {
      throw ApiError.from(error);
    }
  }
}
