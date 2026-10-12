import { Pipe, PipeTransform, inject } from '@angular/core';
import { I18n } from '../core/i18n/i18n';

/** Date and time in the user's language: `{{ value | dateTime }}`. Empty for null. */
@Pipe({ name: 'dateTime', pure: false })
export class DateTimePipe implements PipeTransform {
  private readonly i18n = inject(I18n);

  transform(value: string | null | undefined): string {
    if (!value) return '';
    return new Intl.DateTimeFormat(this.i18n.language(), {
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(new Date(value));
  }
}

/** Approximate time from now: "3 minutes ago". */
@Pipe({ name: 'relativeTime', pure: false })
export class RelativeTimePipe implements PipeTransform {
  private readonly i18n = inject(I18n);

  transform(value: string | null | undefined): string {
    if (!value) return '';
    const seconds = (new Date(value).getTime() - Date.now()) / 1000;
    const format = new Intl.RelativeTimeFormat(this.i18n.language(), { numeric: 'auto' });
    const units: [Intl.RelativeTimeFormatUnit, number][] = [
      ['year', 31536000],
      ['month', 2592000],
      ['day', 86400],
      ['hour', 3600],
      ['minute', 60],
    ];
    for (const [unit, size] of units) {
      if (Math.abs(seconds) >= size) return format.format(Math.round(seconds / size), unit);
    }
    return format.format(Math.round(seconds), 'second');
  }
}

/** Track length as m:ss (or h:mm:ss) from milliseconds. */
@Pipe({ name: 'duration' })
export class DurationPipe implements PipeTransform {
  transform(milliseconds: number | null | undefined): string {
    const total = Math.round((milliseconds ?? 0) / 1000);
    const hours = Math.floor(total / 3600);
    const minutes = Math.floor((total % 3600) / 60);
    const seconds = String(total % 60).padStart(2, '0');
    return hours > 0
      ? `${hours}:${String(minutes).padStart(2, '0')}:${seconds}`
      : `${minutes}:${seconds}`;
  }
}

/** File sizes in the user's language: "184 MB". */
@Pipe({ name: 'bytes', pure: false })
export class BytesPipe implements PipeTransform {
  private readonly i18n = inject(I18n);

  transform(value: number | null | undefined): string {
    const units = ['byte', 'kilobyte', 'megabyte', 'gigabyte'];
    let size = value ?? 0;
    let unit = 0;
    while (size >= 1000 && unit < units.length - 1) {
      size /= 1000;
      unit++;
    }
    return new Intl.NumberFormat(this.i18n.language(), {
      style: 'unit',
      unit: units[unit],
      unitDisplay: 'short',
      maximumFractionDigits: unit < 2 ? 0 : 1,
    }).format(size);
  }
}

/** Thousands separators in the user's language. */
@Pipe({ name: 'count', pure: false })
export class CountPipe implements PipeTransform {
  private readonly i18n = inject(I18n);

  transform(value: number | null | undefined): string {
    return new Intl.NumberFormat(this.i18n.language()).format(value ?? 0);
  }
}
