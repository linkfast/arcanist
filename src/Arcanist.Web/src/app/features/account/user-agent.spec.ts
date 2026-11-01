import { describe, expect, it } from 'vitest';
import { describeDevice } from './user-agent';

describe('describeDevice', () => {
  it('names the browser and the system', () => {
    expect(
      describeDevice(
        'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36',
      ),
    ).toBe('Chrome · Windows');
    expect(
      describeDevice(
        'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15',
      ),
    ).toBe('Safari · macOS');
    expect(
      describeDevice(
        'Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/150.0 Safari/537.36 Edg/150.0',
      ),
    ).toBe('Edge · Windows');
    expect(
      describeDevice('Mozilla/5.0 (X11; Linux x86_64; rv:140.0) Gecko/20100101 Firefox/140.0'),
    ).toBe('Firefox · Linux');
    expect(
      describeDevice(
        'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile Safari/604.1',
      ),
    ).toBe('Safari · iOS');
  });

  it('says nothing of what is not a browser', () => {
    expect(describeDevice(null)).toBeNull();
    expect(describeDevice('Symfonium/10.2')).toBeNull();
  });
});
