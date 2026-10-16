import type { BackupSchedule, DayOfWeek } from '../../graphql/generated/graphql';
import type { I18n } from '../../core/i18n/i18n';

export const DAYS_OF_WEEK: DayOfWeek[] = [
  'MONDAY',
  'TUESDAY',
  'WEDNESDAY',
  'THURSDAY',
  'FRIDAY',
  'SATURDAY',
  'SUNDAY',
];

/** "HH:MM" from minutes after midnight (the API's `timeOfDay`). */
export function minutesToTime(minutes: number | null | undefined): string {
  const value = Math.min(Math.max(Math.round(minutes ?? 0), 0), 24 * 60 - 1);
  const hours = Math.floor(value / 60);
  return `${String(hours).padStart(2, '0')}:${String(value % 60).padStart(2, '0')}`;
}

/** Minutes after midnight from "HH:MM" (or "H:MM"); null when it is not a time of day. */
export function timeToMinutes(time: string | null | undefined): number | null {
  const match = /^(\d{1,2}):(\d{2})$/.exec((time ?? '').trim());
  if (!match) return null;
  const hours = Number(match[1]);
  const minutes = Number(match[2]);
  return hours < 24 && minutes < 60 ? hours * 60 + minutes : null;
}

/** The time zone of this browser, which a new backup's schedule follows. */
export function browserTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  } catch {
    return 'UTC';
  }
}

/** When a backup runs, in words: "Every day at 03:00". */
export function scheduleSummary(
  i18n: I18n,
  backup: {
    schedule: BackupSchedule;
    intervalHours?: number | null;
    timeOfDay?: number | null;
    dayOfWeek?: DayOfWeek | null;
  },
): string {
  switch (backup.schedule) {
    case 'HOURS':
      return backup.intervalHours === 1
        ? i18n.t('backups.schedule.summary.everyHour')
        : i18n.t('backups.schedule.summary.hours', { count: backup.intervalHours ?? 0 });
    case 'DAILY':
      return i18n.t('backups.schedule.summary.daily', { time: minutesToTime(backup.timeOfDay) });
    case 'WEEKLY':
      return i18n.t('backups.schedule.summary.weekly', {
        day: i18n.t('backups.days.' + (backup.dayOfWeek ?? 'MONDAY')),
        time: minutesToTime(backup.timeOfDay),
      });
    default:
      return i18n.t('backups.schedule.summary.manual');
  }
}
