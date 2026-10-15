import { Injectable, computed, signal } from '@angular/core';

export type ThemePreference = 'system' | 'light' | 'dark';

const storageKey = 'arcanist.theme';

/**
 * Light or dark panel: the system's choice unless the user picks one. The `dark` class on <html> switches the
 * colors (`styles.css`); `index.html` sets it before the app starts, so the page never flashes the other theme.
 */
@Injectable({ providedIn: 'root' })
export class Theme {
  readonly preferences: readonly ThemePreference[] = ['system', 'light', 'dark'];
  readonly preference = signal<ThemePreference>(initialPreference());
  private readonly systemDark = signal(systemQuery()?.matches ?? false);
  readonly dark = computed(() =>
    this.preference() === 'system' ? this.systemDark() : this.preference() === 'dark',
  );

  constructor() {
    systemQuery()?.addEventListener('change', (event) => {
      this.systemDark.set(event.matches);
      this.apply();
    });
    this.apply();
  }

  set(preference: ThemePreference): void {
    this.preference.set(preference);
    try {
      if (preference === 'system') localStorage.removeItem(storageKey);
      else localStorage.setItem(storageKey, preference);
    } catch {
      // Storage can be unavailable (private mode); the choice then lasts for the session.
    }
    this.apply();
  }

  private apply(): void {
    document.documentElement.classList.toggle('dark', this.dark());
  }
}

function initialPreference(): ThemePreference {
  try {
    const stored = localStorage.getItem(storageKey);
    return stored === 'light' || stored === 'dark' ? stored : 'system';
  } catch {
    return 'system';
  }
}

function systemQuery(): MediaQueryList | null {
  return typeof matchMedia === 'function' ? matchMedia('(prefers-color-scheme: dark)') : null;
}
