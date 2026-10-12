import { Routes } from '@angular/router';
import {
  authGuard,
  permissionGuard,
  setupCompletedGuard,
  setupPendingGuard,
} from './core/auth/guards';

export const routes: Routes = [
  {
    path: 'setup',
    canActivate: [setupPendingGuard],
    loadComponent: () => import('./features/setup/setup.page').then((m) => m.SetupPage),
  },
  {
    path: 'login',
    canActivate: [setupCompletedGuard],
    loadComponent: () => import('./features/login/login.page').then((m) => m.LoginPage),
  },
  {
    path: '',
    canActivate: [setupCompletedGuard, authGuard],
    loadComponent: () => import('./shell/shell').then((m) => m.Shell),
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () =>
          import('./features/overview/overview.page').then((m) => m.OverviewPage),
      },
      {
        path: 'libraries',
        loadComponent: () =>
          import('./features/libraries/libraries.page').then((m) => m.LibrariesPage),
      },
      {
        path: 'users',
        canActivate: [permissionGuard('MANAGE_USERS')],
        loadComponent: () => import('./features/users/users.page').then((m) => m.UsersPage),
      },
      {
        path: 'settings',
        canActivate: [permissionGuard('MANAGE_SETTINGS')],
        loadComponent: () =>
          import('./features/settings/settings.page').then((m) => m.SettingsPage),
      },
      {
        path: 'account',
        loadComponent: () => import('./features/account/account.page').then((m) => m.AccountPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
