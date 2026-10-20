import { parse } from './release-notes';

describe('release notes', () => {
  it('reads what GitHub writes, links and bold included', () => {
    const blocks = parse(
      [
        "## What's Changed",
        '* Install extensions from the marketplace by @someone in https://github.com/linkfast/arcanist/pull/12',
        '',
        '**Full Changelog**: https://github.com/linkfast/arcanist/compare/v0.1.0...v0.2.0',
        '- See [the docs](https://example.com/docs)',
      ].join('\r\n'),
    );

    expect(blocks.map((b) => b.kind)).toEqual(['heading', 'item', 'paragraph', 'item']);
    expect(blocks[1].parts.at(-1)).toEqual({
      text: '#12',
      href: 'https://github.com/linkfast/arcanist/pull/12',
    });
    expect(blocks[2].parts[0]).toEqual({ text: 'Full Changelog', strong: true });
    expect(blocks[2].parts.at(-1)?.text).toBe('v0.1.0...v0.2.0');
    expect(blocks[3].parts[1]).toEqual({ text: 'the docs', href: 'https://example.com/docs' });
  });

  it('never turns text into markup or other links', () => {
    const [block] = parse('<img src=x onerror=alert(1)> [x](javascript:alert(1))');
    expect(block.parts.every((part) => !part.href)).toBe(true);
    expect(block.parts.map((part) => part.text).join('')).toContain('<img');
  });
});
