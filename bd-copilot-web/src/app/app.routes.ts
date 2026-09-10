import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/guards/auth.guard';
import { PrototypeShell } from './shell/prototype-shell/prototype-shell';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/login/login').then(m => m.Login)
  },
  {
    path: '',
    component: PrototypeShell,
    canActivate: [authGuard],
    children: [
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
      {
        path: 'dashboard',
        loadComponent: () => import('./features/dashboard/dashboard').then(m => m.Dashboard)
      },
      { path: 'chat', loadComponent: () => import('./features/chat/chat').then(m => m.Chat) },
      { path: 'rfp', loadComponent: () => import('./features/rfp-generator/rfp-generator').then(m => m.RfpGenerator) },
      {
        path: 'business-case',
        loadComponent: () => import('./features/business-case/business-case').then(m => m.BusinessCase)
      },
      {
        path: 'proposal',
        loadComponent: () => import('./features/proposal-generator/proposal-generator').then(m => m.ProposalGenerator)
      },
      {
        path: 'competitive',
        loadComponent: () => import('./features/competitive/competitive').then(m => m.Competitive)
      },
      {
        path: 'search',
        loadComponent: () => import('./features/knowledge-search/knowledge-search').then(m => m.KnowledgeSearch)
      },
      {
        path: 'library',
        loadComponent: () => import('./features/document-library/document-library').then(m => m.DocumentLibrary)
      },
      {
        path: 'projects',
        loadComponent: () => import('./features/project-intelligence/project-intelligence').then(m => m.ProjectIntelligence)
      },
      {
        path: 'pursuits',
        loadComponent: () => import('./features/pursuits/pursuits').then(m => m.Pursuits)
      },
      {
        path: 'analytics',
        loadComponent: () => import('./features/analytics/analytics').then(m => m.Analytics)
      },
      { path: 'settings', loadComponent: () => import('./features/settings/settings').then(m => m.Settings) },
      { path: 'admin', loadComponent: () => import('./features/admin/admin').then(m => m.Admin) },
      { path: 'admin/onboard', loadComponent: () => import('./features/admin-onboard/admin-onboard').then(m => m.AdminOnboard) }
    ]
  },
  { path: '**', redirectTo: 'dashboard' }
];
