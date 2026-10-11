import { Injectable, Pipe, PipeTransform, computed, inject, signal } from '@angular/core';
import { en } from './en';
import { es } from './es';

export type Language = 'en' | 'es';
export type Dictionary = { [key: string]: string | Dictionary };

const dictionaries: Record<Language, Dictionary> = { en, es };
const storageKey = 'arcanist.language';

/** Minimal translations: nested dictionaries addressed by dotted keys, with {placeholder} substitution. */
@Injectable({ providedIn: 'root' })
export class I18n {
  readonly languages: readonly Language[] = ['es', 'en'];
  readonly language = signal<Language>(initialLanguage());
  private readonly dictionary = computed(() => dictionaries[this.language()]);

  setLanguage(language: Language): void {
    this.language.set(language);
    document.documentElement.lang = language;
    try {
      localStorage.setItem(storageKey, language);
    } catch {
      // Storage can be unavailable (private mode); the choice then lasts for the session.
    }
  }

  t(key: string, params?: Record<string, string | number>): string {
    const value = lookup(this.dictionary(), key) ?? lookup(en, key) ?? key;
    return params
      ? value.replace(/\{(\w+)\}/g, (_, name: string) => String(params[name] ?? `{${name}}`))
      : value;
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
