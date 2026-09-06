import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MenuStateService } from '../../core/services/menu-state.service';
import { TietoIconComponent } from '../../shared/tieto-icon.component';
import { TietoIconKey } from '../../shared/tieto-icons';

interface NavItem {
  path: string;
  label: string;
  icon: TietoIconKey;
}

interface NavSection {
  title: string;
  items: NavItem[];
}

@Component({
  selector: 'app-prototype-shell',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, TietoIconComponent],
  templateUrl: './prototype-shell.html'
})
export class PrototypeShell {
  private readonly menuState = inject(MenuStateService);

  protected readonly menuOpen = this.menuState.open;

  protected readonly nav: NavSection[] = [
    {
      title: 'Home',
      items: [{ path: '/dashboard', label: 'Dashboard', icon: 'portfolio' }]
    },
    {
      title: 'Copilot',
      items: [{ path: '/chat', label: 'Chat', icon: 'copilot' }]
    },
    {
      title: 'Generate',
      items: [
        { path: '/rfp', label: 'RFP Generator', icon: 'documents' },
        { path: '/business-case', label: 'Business Case', icon: 'scenarios' },
        { path: '/proposal', label: 'Proposal Generator', icon: 'proposals' }
      ]
    },
    {
      title: 'Explore',
      items: [
        { path: '/search', label: 'Find & reuse', icon: 'research' },
        { path: '/library', label: 'Document Library', icon: 'portfolio' },
        { path: '/projects', label: 'Project Intelligence', icon: 'scenarios' }
      ]
    },
    {
      title: 'Operations',
      items: [
        { path: '/admin', label: 'Admin Console', icon: 'admin' },
        { path: '/settings', label: 'Settings', icon: 'settings' }
      ]
    },
    {
      title: 'Docs',
      items: [
        { path: '/architecture', label: 'Architecture', icon: 'architecture' },
        { path: '/stack', label: 'Tech Stack', icon: 'stack' },
        { path: '/roadmap', label: 'Roadmap', icon: 'roadmap' }
      ]
    }
  ];

  protected closeMenu(): void {
    this.menuState.close();
  }
}
