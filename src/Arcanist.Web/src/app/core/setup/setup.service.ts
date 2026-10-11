import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  ApiError,
  DatabaseProvider,
  ServerDirectoryListing,
  SetupRequest,
  SetupStatus,
} from '../api/rest';

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
