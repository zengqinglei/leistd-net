import { Routes } from '@angular/router';

export const WORKSPACE_ROUTES: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'dashboard'
  },
  {
    path: 'dashboard',
    loadComponent: () => import('./components/dashboard/workspace-dashboard').then(m => m.WorkspaceDashboardPage)
  },
  {
    path: 'placeholder',
    loadComponent: () => import('./components/placeholder/workspace-placeholder').then(m => m.WorkspacePlaceholder)
  }
];
