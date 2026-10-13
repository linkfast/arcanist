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

/** The wizard is only reachable while setup is pending. */
export const setupPendingGuard: CanActivateFn = async () => {
  const setup = inject(SetupService);
  const router = inject(Router);
  const status = setup.status() ?? (await setup.loadStatus());
  return status.completed ? router.parseUrl('/') : true;
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

export const adminGuard: CanActivateFn = () =>
  inject(AuthService).isAdmin() ? true : inject(Router).parseUrl('/');
