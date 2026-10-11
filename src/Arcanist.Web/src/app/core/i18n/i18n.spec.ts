import { en } from './en';
import { es } from './es';
import type { Dictionary } from './i18n';

function keys(dictionary: Dictionary, prefix = ''): string[] {
  return Object.entries(dictionary).flatMap(([key, value]) =>
    typeof value === 'string' ? [prefix + key] : keys(value, `${prefix}${key}.`),
  );
}

describe('translations', () => {
  it('have the same keys in every language', () => {
    expect(keys(es).sort()).toEqual(keys(en).sort());
  });

  it('translate every API error code the server can return', () => {
    // Keep in sync with Arcanist.Core/Application/Errors.cs.
    const codes = [
      'setup_required',
      'setup_completed',
      'setup_code_invalid',
      'validation_failed',
      'required',
      'invalid_format',
      'too_short',
      'duplicate',
      'not_found',
      'access_denied',
      'connection_failed',
      'database_not_empty',
      'unauthorized',
      'forbidden',
      'reauthentication_required',
      'invalid_credentials',
      'account_disabled',
      'too_many_requests',
      'last_administrator',
      'cannot_modify_self',
      'overlapping',
      'scan_failed',
      'source_unavailable',
      'interrupted',
      'scan_finished',
      'invalid_refresh_token',
    ];
    const errors = keys(es)
      .filter((k) => k.startsWith('errors.'))
      .map((k) => k.slice('errors.'.length));

    expect(codes.filter((code) => !errors.includes(code))).toEqual([]);
  });
});
