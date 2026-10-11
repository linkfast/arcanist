import { Injectable, signal } from '@angular/core';

/**
 * Asks the user to confirm their password when the server answers `reauthentication_required`. The dialog that
 * shows the prompt lives in the shell (ReauthDialog) and resolves the pending request.
 */
@Injectable({ providedIn: 'root' })
export class ReauthService {
  readonly open = signal(false);
  private pending: ((confirmed: boolean) => void)[] = [];

  /** Resolves true once the password is confirmed, false if the user cancels. */
  prompt(): Promise<boolean> {
    this.open.set(true);
    return new Promise((resolve) => this.pending.push(resolve));
  }

  settle(confirmed: boolean): void {
    this.open.set(false);
    const waiting = this.pending;
    this.pending = [];
    waiting.forEach((resolve) => resolve(confirmed));
  }
}
