import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, Permission, TokenResponse, UserAccount } from '../api/rest';

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

  async login(userName: string, password: string): Promise<void> {
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

  /** Confirms the password for sensitive actions; returns false when it is wrong. */
  async reauthenticate(password: string): Promise<boolean> {
    try {
      const response = await firstValueFrom(
        this.http.post<TokenResponse>('/rest/v1/auth/reauthenticate', { password }),
      );
      this.accept(response);
      return true;
    } catch (error) {
      const apiError = ApiError.from(error);
      if (apiError.code === 'invalid_credentials') return false;
      throw apiError;
    }
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
