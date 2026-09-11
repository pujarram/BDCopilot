import { Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
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

/** Phase 1 seller shell — ≤ 5 primary destinations + Admin for platform roles. */
const SELLER_NAV: NavItem[] = [
  { path: '/dashboard', label: 'Home', icon: 'portfolio' },
  { path: '/chat', label: 'Ask', icon: 'copilot' },
  { path: '/generate', label: 'Generate', icon: 'documents' },
  { path: '/library', label: 'Library', icon: 'portfolio' },
  { path: '/pursuits', label: 'Pursuits', icon: 'scenarios' }
];

@Component({
  selector: 'app-prototype-shell',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, TietoIconComponent],
  templateUrl: './prototype-shell.html'
})
export class PrototypeShell {
  private readonly menuState = inject(MenuStateService);
  private readonly auth = inject(AuthService);

  protected readonly menuOpen = this.menuState.open;

  protected readonly nav = computed((): NavSection[] => {
    const sections: NavSection[] = [
      { title: 'Workspace', items: SELLER_NAV }
    ];
    if (this.auth.isAdmin()) {
      sections.push({
        title: 'Admin',
        items: [{ path: '/admin', label: 'Admin Console', icon: 'admin' }]
      });
    }
    return sections;
  });

  protected closeMenu(): void {
    this.menuState.close();
  }
}
