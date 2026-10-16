import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Permission } from '../api/rest';
import { SetupService } from '../setup/setup.service';
import { AuthService } from './auth.service';

/** Sends everything to the wizard until the server is set up. */
export const setupCompletedGuard: CanActivateFn = async () => {
  // Injected before awaiting: inject() only works synchronously.
  const setup = inject(SetupService);
  const router = inject(Router);
  const status = setup.status() ?? (await setup.loadStatus());
  return status.completed ? true : router.parseUrl('/setup');
};

/** The wizard is only reachable while setup is pending, or while the migration it started runs (its progress). */
export const setupPendingGuard: CanActivateFn = async () => {
  const setup = inject(SetupService);
  const router = inject(Router);
  let status = setup.status() ?? (await setup.loadStatus());
  if (status.completed) status = await setup.loadStatus();
  return status.completed && !status.migrating ? router.parseUrl('/') : true;
};

/** While the migration the wizard started runs, the panel shows its progress (the wizard's last screen). */
export const migrationGuard: CanActivateFn = async () => {
  const setup = inject(SetupService);
  const router = inject(Router);
  const status = await setup.loadStatus();
  return status.migrating ? router.parseUrl('/setup') : true;
};

/** Requires a session, restoring it from the refresh cookie after a page reload. */
export const authGuard: CanActivateFn = async (_, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.isAuthenticated() || (await auth.refresh())) return true;
  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

export const permissionGuard =
  (permission: Permission): CanActivateFn =>
  () =>
    inject(AuthService).can(permission) ? true : inject(Router).parseUrl('/');

/** Administrators only. Waits for the session to be restored, as it runs beside authGuard on top-level routes. */
export const adminGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isAuthenticated()) await auth.refresh();
  return auth.isAdmin() ? true : router.parseUrl('/');
};
