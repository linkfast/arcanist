import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, Permission, TokenResponse, UserAccount } from '../api/rest';
import { createPasskey, getPasskey } from './webauthn';

/** The password was right, and more is needed. */
export interface SecondFactorAnswer {
  code: 'second_factor_required' | 'second_factor_setup_required';
  /** Null on a step-up: the session says who it is. */
  challenge: string | null;
  /** TOTP, RECOVERY_CODE, PASSKEY. */
  methods: string[];
  /** Where the user's passkeys work. */
  passkeyDomains: string[];
}

export type LoginChallenge = SecondFactorAnswer & { challenge: string };

/** Pairing a second factor, at the sign-in or from My account. Each answers the new recovery codes, if any. */
export interface SecondFactorPairing {
  startTotp(): Promise<{ secret: string; uri: string }>;
  confirmTotp(code: string): Promise<readonly string[]>;
  registerPasskey(name: string): Promise<readonly string[]>;
}

function secondFactorAnswer(error: unknown): SecondFactorAnswer | null {
  if (!(error instanceof HttpErrorResponse)) return null;
  const body = error.error as SecondFactorAnswer | null;
  return body?.code === 'second_factor_required' || body?.code === 'second_factor_setup_required'
    ? body
    : null;
}

/**
 * Session of the admin panel. The access token lives only in memory; the refresh token is an HttpOnly cookie the
 * browser sends to /rest/v1/auth, so reloading the page restores the session without exposing tokens to scripts.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly token = signal<string | null>(null);
  private refreshing: Promise<boolean> | null = null;

  readonly user = signal<UserAccount | null>(null);
  readonly isAuthenticated = computed(() => this.user() !== null);
  readonly isAdmin = computed(() => this.user()?.role === 'ADMIN');

  get accessToken(): string | null {
    return this.token();
  }

  can(permission: Permission): boolean {
    const user = this.user();
    return !!user && (user.role === 'ADMIN' || user.permissions.includes(permission));
  }

  /** Signs in; with a second factor, the answer says what is still needed and null means it is done. */
  async login(userName: string, password: string): Promise<LoginChallenge | null> {
    try {
      const response = await firstValueFrom(
        this.http.post<TokenResponse>('/rest/v1/auth/login', {
          userName,
          password,
          deviceName: 'Arcanist admin panel',
          useCookie: true,
        }),
      );
      this.accept(response);
      return null;
    } catch (error) {
      const challenge = secondFactorAnswer(error);
      if (challenge?.challenge) return challenge as LoginChallenge;
      throw ApiError.from(error);
    }
  }

  loginWithCode(challenge: string, code: string): Promise<TokenResponse> {
    return this.finish('/rest/v1/auth/login/second-factor', { challenge, code });
  }

  async loginWithPasskey(challenge: string): Promise<TokenResponse> {
    const options = await this.post<{ options: string }>('/rest/v1/auth/login/passkey/options', {
      challenge,
    });
    const assertion = await getPasskey(options.options);
    return this.finish('/rest/v1/auth/login/passkey', { challenge, assertion });
  }

  /** How a user who must pair a factor to sign in does it: finishing it signs them in. */
  pairingAtLogin(challenge: string): SecondFactorPairing {
    return {
      startTotp: () => this.post('/rest/v1/auth/login/setup/totp', { challenge }),
      confirmTotp: async (code) =>
        (await this.finish('/rest/v1/auth/login/setup/totp/confirm', { challenge, code }))
          .recoveryCodes ?? [],
      registerPasskey: async (name) => {
        const { options } = await this.post<{ options: string }>(
          '/rest/v1/auth/login/setup/passkey/options',
          { challenge },
        );
        const attestation = await createPasskey(options);
        return (
          (await this.finish('/rest/v1/auth/login/setup/passkey', { challenge, name, attestation }))
            .recoveryCodes ?? []
        );
      },
    };
  }

  private async finish(url: string, body: unknown): Promise<TokenResponse> {
    const response = await this.post<TokenResponse>(url, body);
    this.accept(response);
    return response;
  }

  private async post<T>(url: string, body: unknown): Promise<T> {
    try {
      return await firstValueFrom(this.http.post<T>(url, body));
    } catch (error) {
      throw ApiError.from(error);
    }
  }

  /** Uses the refresh cookie to get a new access token. Concurrent callers share one request. */
  refresh(): Promise<boolean> {
    this.refreshing ??= firstValueFrom(this.http.post<TokenResponse>('/rest/v1/auth/refresh', {}))
      .then((response) => {
        this.accept(response);
        return true;
      })
      .catch(() => {
        this.clear();
        return false;
      })
      .finally(() => (this.refreshing = null));

    return this.refreshing;
  }

  /**
   * Confirms the password (and the second factor, for who has one) for sensitive actions. Answers true when done,
   * false when the password is wrong, and what is still needed when it asks for the factor.
   */
  async reauthenticate(
    password: string,
    code?: string,
    passkey?: string,
  ): Promise<boolean | SecondFactorAnswer> {
    try {
      const response = await firstValueFrom(
        this.http.post<TokenResponse>('/rest/v1/auth/reauthenticate', { password, code, passkey }),
      );
      this.accept(response);
      return true;
    } catch (error) {
      const answer = secondFactorAnswer(error);
      if (answer) return answer;
      const apiError = ApiError.from(error);
      if (apiError.code === 'invalid_credentials') return false;
      throw apiError;
    }
  }

  async reauthenticateWithPasskey(password: string): Promise<boolean | SecondFactorAnswer> {
    const { options } = await this.post<{ options: string }>(
      '/rest/v1/auth/reauthenticate/passkey/options',
      {},
    );
    return this.reauthenticate(password, undefined, await getPasskey(options));
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/rest/v1/auth/logout', {}));
    } finally {
      this.clear();
      await this.router.navigateByUrl('/login');
    }
  }

  /** Called when a refresh fails mid-session: the session is gone, so go back to the login page. */
  async expire(): Promise<void> {
    this.clear();
    await this.router.navigate(['/login'], { queryParams: { returnUrl: this.router.url } });
  }

  updateUser(user: UserAccount): void {
    this.user.set(user);
  }

  private accept(response: TokenResponse): void {
    this.token.set(response.accessToken);
    this.user.set(response.user);
  }

  private clear(): void {
    this.token.set(null);
    this.user.set(null);
  }
}
