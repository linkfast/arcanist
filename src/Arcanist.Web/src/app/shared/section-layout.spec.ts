import { describe, expect, it } from 'vitest';
import { PanelSection, activeSection } from './section-layout';

const sections: PanelSection[] = ['general', 'privacy'].map((id) => ({
  id,
  label: id,
  hint: id,
  icon: id,
}));

describe('activeSection', () => {
  it('is the one the URL asks for', () => {
    expect(activeSection(sections, 'privacy', false)?.id).toBe('privacy');
    expect(activeSection(sections, 'privacy', true)?.id).toBe('privacy');
  });

  it('without one, a wide screen shows the first and a small one the menu', () => {
    expect(activeSection(sections, undefined, true)?.id).toBe('general');
    expect(activeSection(sections, undefined, false)).toBeNull();
  });

  it('one that does not exist, or that this user does not see, is the first', () => {
    expect(activeSection(sections, 'maintenance', false)?.id).toBe('general');
    expect(activeSection(sections, 'maintenance', true)?.id).toBe('general');
  });
});
