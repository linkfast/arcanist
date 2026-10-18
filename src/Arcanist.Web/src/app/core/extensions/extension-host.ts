import { Injectable, effect, inject } from '@angular/core';
import { AuthService } from '../auth/auth.service';
import { I18n } from '../i18n/i18n';
import { Theme } from '../theme/theme';

/**
 * The panel's side of the host protocol (docs/developers/extensions.md): extension pages shown in the panel (an
 * iframe of this origin) ask for the session and get it, with the panel's language and look, again whenever the token
 * is refreshed, the language changes or the theme changes. Only the frames the panel adopted get it.
 */
@Injectable({ providedIn: 'root' })
export class ExtensionHost {
  private readonly auth = inject(AuthService);
  private readonly i18n = inject(I18n);
  private readonly theme = inject(Theme);
  private readonly frames = new Set<Window>();

  constructor() {
    window.addEventListener('message', (event) => void this.receive(event));
    effect(() => {
      // Read, so the effect follows them.
      this.auth.accessToken;
      this.i18n.language();
      this.theme.dark();
      requestAnimationFrame(() => this.sendAll());
    });
  }

  adopt(target: Window | null): void {
    if (target) this.frames.add(target);
  }

  release(target: Window | null): void {
    if (target) this.frames.delete(target);
  }

  /** Sends the session again (the token changed, for instance after a refresh). */
  sendAll(): void {
    for (const frame of this.frames) this.send(frame);
  }

  private send(target: Window): void {
    const token = this.auth.accessToken;
    if (!token) return;
    try {
      target.postMessage(
        { type: 'arcanist:session', token, language: this.i18n.language(), theme: this.look() },
        location.origin,
      );
    } catch {
      this.frames.delete(target);
    }
  }

  private async receive(event: MessageEvent): Promise<void> {
    if (event.origin !== location.origin || !event.source) return;
    const source = event.source as Window;
    // A frame of the panel's own pages (its script says hello before the frame's load event).
    if (!this.frames.has(source)) {
      if (![...document.querySelectorAll('iframe')].some((frame) => frame.contentWindow === source))
        return;
      this.frames.add(source);
    }
    const message = event.data as { type?: string } | null;
    if (message?.type === 'arcanist:hello') this.send(source);
    else if (message?.type === 'arcanist:token-expired' && (await this.auth.refresh()))
      this.sendAll();
  }

  /** The panel's colors, as its CSS variables have them. */
  private look() {
    const style = getComputedStyle(document.documentElement);
    const value = (name: string) => style.getPropertyValue(name).trim() || undefined;
    return {
      scheme: this.theme.dark() ? 'dark' : 'light',
      background: value('--background'),
      foreground: value('--foreground'),
      muted: value('--muted-foreground'),
      surface: value('--card'),
      border: value('--border'),
      accent: value('--primary'),
      accentForeground: value('--primary-foreground'),
      radius: value('--radius'),
      font: getComputedStyle(document.body).fontFamily || undefined,
    };
  }
}
