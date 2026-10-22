import { Injectable, Pipe, PipeTransform, computed, inject, signal } from '@angular/core';

export type Language = 'en' | 'es';
export type Dictionary = { [key: string]: string | Dictionary };

/**
 * Each language in a chunk of its own: only the one in use is downloaded (the dictionaries are the largest part of
 * the panel's first load), the other when the user switches to it.
 */
const loaders: Record<Language, () => Promise<Dictionary>> = {
  en: () => import('./en').then((m) => m.en),
  es: () => import('./es').then((m) => m.es),
};
const storageKey = 'arcanist.language';

/** Minimal translations: nested dictionaries addressed by dotted keys, with {placeholder} substitution. */
@Injectable({ providedIn: 'root' })
export class I18n {
  readonly languages: readonly Language[] = ['es', 'en'];
  readonly language = signal<Language>(initialLanguage());
  private readonly loaded = signal<Partial<Record<Language, Dictionary>>>({});
  private readonly dictionary = computed(() => this.loaded()[this.language()] ?? {});

  /** Loads the language in use; the app waits for it before showing anything. */
  async ready(): Promise<void> {
    await this.load(this.language());
    document.documentElement.lang = this.language();
  }

  async setLanguage(language: Language): Promise<void> {
    await this.load(language);
    this.language.set(language);
    document.documentElement.lang = language;
    try {
      localStorage.setItem(storageKey, language);
    } catch {
      // Storage can be unavailable (private mode); the choice then lasts for the session.
    }
  }

  private async load(language: Language): Promise<void> {
    if (this.loaded()[language]) return;
    const dictionary = await loaders[language]();
    this.loaded.update((loaded) => ({ ...loaded, [language]: dictionary }));
  }

  t(key: string, params?: Record<string, string | number>): string {
    const value = lookup(this.dictionary(), key) ?? key;
    return params
      ? value.replace(/\{(\w+)\}/g, (_, name: string) => String(params[name] ?? `{${name}}`))
      : value;
  }

  /** Whether the current language has a text for `key`. */
  has(key: string): boolean {
    return lookup(this.dictionary(), key) !== undefined;
  }

  /** Message for an API error code, falling back to a generic one. */
  error(code: string | null | undefined): string {
    return code && lookup(this.dictionary(), `errors.${code}`)
      ? this.t(`errors.${code}`)
      : this.t('errors.unexpected');
  }
}

/** Translates a key in templates: `{{ 'nav.libraries' | t }}`. Impure so it follows language changes. */
@Pipe({ name: 't', pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly i18n = inject(I18n);

  transform(key: string, params?: Record<string, string | number>): string {
    return this.i18n.t(key, params);
  }
}

function lookup(dictionary: Dictionary, key: string): string | undefined {
  let node: string | Dictionary | undefined = dictionary;
  for (const part of key.split('.')) {
    if (typeof node !== 'object') return undefined;
    node = node[part];
  }
  return typeof node === 'string' ? node : undefined;
}

function initialLanguage(): Language {
  try {
    const stored = localStorage.getItem(storageKey);
    if (stored === 'en' || stored === 'es') return stored;
  } catch {
    // Ignore unavailable storage.
  }
  return navigator.language?.toLowerCase().startsWith('es') ? 'es' : 'en';
}
