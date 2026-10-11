import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Permission } from '../api/rest';
import { SetupService } from '../setup/setup.service';
import { AuthService } from './auth.service';

/** Sends everything to the wizard until the server is set up. */
export const setupCompletedGuard: CanActivateFn = async () => {
  const setup = inject(SetupService);
  const status = setup.status() ?? (await setup.loadStatus());
  return status.completed ? true : inject(Router).parseUrl('/setup');
};

/** The wizard is only reachable while setup is pending. */
export const setupPendingGuard: CanActivateFn = async () => {
  const setup = inject(SetupService);
  const status = setup.status() ?? (await setup.loadStatus());
  return status.completed ? inject(Router).parseUrl('/') : true;
};

/** Requires a session, restoring it from the refresh cookie after a page reload. */
export const authGuard: CanActivateFn = async (_, state) => {
  const auth = inject(AuthService);
  if (auth.isAuthenticated() || (await auth.refresh())) return true;
  return inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

export const permissionGuard =
  (permission: Permission): CanActivateFn =>
  () =>
    inject(AuthService).can(permission) ? true : inject(Router).parseUrl('/');
