import { describe, expect, it } from 'vitest';
import { accountSections, activeSection, isWithin, settingsSections } from './section-layout';

describe('activeSection', () => {
  const sections = settingsSections(false);

  it('is the one the URL asks for', () => {
    expect(activeSection(sections, 'privacy')?.id).toBe('privacy');
  });

  it('none, one that does not exist, or one this user does not see, is the first', () => {
    expect(activeSection(sections, undefined)?.id).toBe('general');
    expect(activeSection(sections, 'maintenance')?.id).toBe('general');
  });
});

describe('the sections', () => {
  it('of settings depend on being an administrator', () => {
    expect(settingsSections(false).map((s) => s.id)).toEqual(['general', 'privacy']);
    expect(settingsSections(true).map((s) => s.id)).toContain('security');
  });

  it('of the account have Ultimate Cloud only while it is on', () => {
    expect(accountSections(false).map((s) => s.id)).not.toContain('cloud');
    expect(accountSections(true).map((s) => s.id)).toContain('cloud');
  });
});

describe('isWithin', () => {
  it('is the page and what is below it, with or without a query', () => {
    expect(isWithin('/settings', '/settings')).toBe(true);
    expect(isWithin('/settings/privacy?x=1', '/settings')).toBe(true);
  });

  it('is not another page that starts the same', () => {
    expect(isWithin('/settings-old', '/settings')).toBe(false);
  });
});
