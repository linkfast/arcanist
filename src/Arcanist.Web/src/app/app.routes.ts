import { Routes } from '@angular/router';
import {
  adminGuard,
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
        path: 'duplicates',
        canActivate: [permissionGuard('MANAGE_LIBRARIES')],
        loadComponent: () =>
          import('./features/duplicates/duplicates.page').then((m) => m.DuplicatesPage),
      },
      {
        path: 'import',
        canActivate: [permissionGuard('MANAGE_LIBRARIES')],
        loadComponent: () => import('./features/import/import.page').then((m) => m.ImportPage),
      },
      {
        path: 'stats',
        canActivate: [permissionGuard('VIEW_ALL_STATS')],
        loadComponent: () => import('./features/stats/stats.page').then((m) => m.StatsPage),
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
        path: 'extensions',
        canActivate: [adminGuard],
        loadComponent: () =>
          import('./features/extensions/extensions.page').then((m) => m.ExtensionsPage),
      },
      {
        path: 'network',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/network/network.page').then((m) => m.NetworkPage),
      },
      {
        path: 'dlna',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/dlna/dlna.page').then((m) => m.DlnaPage),
      },
      {
        path: 'account',
        loadComponent: () => import('./features/account/account.page').then((m) => m.AccountPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
