import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiError } from '../api/rest';

/** What an app's link to the authorize page asks for. */
export interface BrowserLoginRequest {
  clientName?: string;
  redirectUri?: string;
  codeChallenge?: string;
  state?: string;
  deviceName?: string;
}

export interface BrowserLoginCheck {
  clientName: string;
  deviceName: string | null;
  target: 'LOOPBACK' | 'APP';
  scheme: string | null;
  denyUrl: string;
}

export interface DeviceLoginDetails {
  userCode: string;
  clientName: string;
  deviceName: string | null;
  ipAddress: string | null;
  createdAt: string;
  expiresAt: string;
}

/** The browser's side of an app signing in here: the session is this panel's, the tokens go to the app. */
@Injectable({ providedIn: 'root' })
export class BrowserLogin {
  private readonly http = inject(HttpClient);

  check(request: BrowserLoginRequest): Promise<BrowserLoginCheck> {
    return this.post('/rest/v1/auth/browser/check', request);
  }

  async approve(request: BrowserLoginRequest): Promise<string> {
    const { redirectUrl } = await this.post<{ redirectUrl: string }>(
      '/rest/v1/auth/browser/approve',
      request,
    );
    return redirectUrl;
  }

  lookUpDevice(userCode: string): Promise<DeviceLoginDetails> {
    return this.post('/rest/v1/auth/device/lookup', { userCode });
  }

  approveDevice(userCode: string): Promise<void> {
    return this.post('/rest/v1/auth/device/approve', { userCode });
  }

  denyDevice(userCode: string): Promise<void> {
    return this.post('/rest/v1/auth/device/deny', { userCode });
  }

  private async post<T>(url: string, body: unknown): Promise<T> {
    try {
      return await firstValueFrom(this.http.post<T>(url, body));
    } catch (error) {
      throw ApiError.from(error);
    }
  }
}
