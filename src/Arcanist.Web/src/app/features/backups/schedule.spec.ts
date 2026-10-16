import { minutesToTime, timeToMinutes } from './schedule';

describe('backup schedule times', () => {
  it('turn minutes after midnight into HH:MM', () => {
    expect(minutesToTime(0)).toBe('00:00');
    expect(minutesToTime(3 * 60 + 5)).toBe('03:05');
    expect(minutesToTime(23 * 60 + 59)).toBe('23:59');
    expect(minutesToTime(null)).toBe('00:00');
  });

  it('read HH:MM back as minutes', () => {
    expect(timeToMinutes('00:00')).toBe(0);
    expect(timeToMinutes('03:05')).toBe(185);
    expect(timeToMinutes('7:30')).toBe(450);
    expect(timeToMinutes('23:59')).toBe(1439);
  });

  it('reject what is not a time of day', () => {
    expect(timeToMinutes('24:00')).toBeNull();
    expect(timeToMinutes('12:60')).toBeNull();
    expect(timeToMinutes('')).toBeNull();
    expect(timeToMinutes('noon')).toBeNull();
  });

  it('round-trip every minute of the day', () => {
    for (let minutes = 0; minutes < 24 * 60; minutes += 7)
      expect(timeToMinutes(minutesToTime(minutes))).toBe(minutes);
  });
});
