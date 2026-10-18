import { Routes } from '@angular/router';
import {
  adminGuard,
  migrationGuard,
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
    // The setup wizard again, on a server already set up: its answers, without the database or the account.
    path: 'reconfigure',
    canActivate: [setupCompletedGuard, authGuard, adminGuard],
    data: { reconfigure: true },
    loadComponent: () => import('./features/setup/setup.page').then((m) => m.SetupPage),
  },
  {
    path: 'login',
    canActivate: [setupCompletedGuard],
    loadComponent: () => import('./features/login/login.page').then((m) => m.LoginPage),
  },
  {
    path: '',
    canActivate: [setupCompletedGuard, authGuard, migrationGuard],
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
        path: 'database',
        canActivate: [adminGuard],
        loadComponent: () =>
          import('./features/database/database.page').then((m) => m.DatabasePage),
      },
      {
        path: 'backups',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/backups/backups.page').then((m) => m.BackupsPage),
      },
      {
        path: 'cloud',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/cloud/cloud.page').then((m) => m.CloudPage),
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
        path: 'ai',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/ai/ai.page').then((m) => m.AiPage),
      },
      {
        path: 'server-audio',
        canActivate: [adminGuard],
        loadComponent: () =>
          import('./features/server-audio/server-audio.page').then((m) => m.ServerAudioPage),
      },
      {
        path: 'mstream',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/mstream/mstream.page').then((m) => m.MstreamPage),
      },
      {
        path: 'account',
        loadComponent: () => import('./features/account/account.page').then((m) => m.AccountPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
