import { Routes } from '@angular/router';
import { adminGuard, authGuard, guestGuard } from './core/guards/auth.guard';
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
      {
        path: 'generate',
        loadComponent: () => import('./features/generate/generate-workspace').then(m => m.GenerateWorkspace),
        children: [
          { path: '', redirectTo: 'rfp', pathMatch: 'full' },
          {
            path: 'rfp',
            loadComponent: () => import('./features/rfp-generator/rfp-generator').then(m => m.RfpGenerator)
          },
          {
            path: 'business-case',
            loadComponent: () => import('./features/business-case/business-case').then(m => m.BusinessCase)
          },
          {
            path: 'proposal',
            loadComponent: () => import('./features/proposal-generator/proposal-generator').then(m => m.ProposalGenerator)
          },
          {
            path: 'battle-card',
            loadComponent: () => import('./features/battle-card/battle-card').then(m => m.BattleCard)
          }
        ]
      },
      { path: 'rfp', redirectTo: 'generate/rfp', pathMatch: 'full' },
      { path: 'business-case', redirectTo: 'generate/business-case', pathMatch: 'full' },
      { path: 'proposal', redirectTo: 'generate/proposal', pathMatch: 'full' },
      { path: 'battle-card', redirectTo: 'generate/battle-card', pathMatch: 'full' },
      { path: 'competitive', redirectTo: 'generate/battle-card', pathMatch: 'full' },
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
        canActivate: [adminGuard],
        loadComponent: () => import('./features/analytics/analytics').then(m => m.Analytics)
      },
      {
        path: 'settings',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/settings/settings').then(m => m.Settings)
      },
      { path: 'admin', canActivate: [adminGuard], loadComponent: () => import('./features/admin/admin').then(m => m.Admin) },
      {
        path: 'admin/onboard',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/admin-onboard/admin-onboard').then(m => m.AdminOnboard)
      }
    ]
  },
  { path: '**', redirectTo: 'dashboard' }
];
