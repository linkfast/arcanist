import type {
  BackupRunStatus,
  RestoreJobFieldsFragment as RestoreJob,
  RestoreStatus,
} from '../../graphql/generated/graphql';

export type BadgeVariant = 'default' | 'secondary' | 'destructive' | 'outline';

/** A run that is waiting or going on: it can be followed and cancelled. */
export function backupRunActive(status: BackupRunStatus | null | undefined): boolean {
  return status === 'QUEUED' || status === 'RUNNING';
}

export function backupRunVariant(status: BackupRunStatus): BadgeVariant {
  switch (status) {
    case 'COMPLETED':
      return 'secondary';
    case 'COMPLETED_WITH_ERRORS':
    case 'FAILED':
      return 'destructive';
    case 'CANCELLED':
      return 'outline';
    default:
      return 'default';
  }
}

export function restoreActive(status: RestoreStatus): boolean {
  return (
    status === 'QUEUED' ||
    status === 'COPYING' ||
    status === 'WAITING_FOR_SCANS' ||
    status === 'APPLYING'
  );
}

export function restoreVariant(status: RestoreStatus): BadgeVariant {
  switch (status) {
    case 'COMPLETED':
      return 'secondary';
    case 'COMPLETED_WITH_ERRORS':
    case 'FAILED':
      return 'destructive';
    case 'CANCELLED':
      return 'outline';
    default:
      return 'default';
  }
}

/** How far the files of a restore got, in percent; null before they are counted. */
export function restorePercent(job: RestoreJob): number | null {
  if (!job.files || job.filesTotal <= 0) return null;
  const done = job.restored + job.identical + job.renamed + job.filesFailed;
  return Math.min(100, Math.round((done * 100) / job.filesTotal));
}
