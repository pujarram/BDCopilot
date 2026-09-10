import { Component, OnInit, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs/operators';
import { TeamsService } from './core/services/teams.service';
import { AuthService } from './core/services/auth.service';
import { Toast } from './shell/toast/toast';
import { MenuStateService } from './core/services/menu-state.service';
import { TietoIconComponent } from './shared/tieto-icon.component';
import { CopilotFab } from './shell/copilot-fab/copilot-fab';
import { CopilotPanel } from './shell/copilot-panel/copilot-panel';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Toast, TietoIconComponent, CopilotFab, CopilotPanel],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App implements OnInit {
  private readonly teams = inject(TeamsService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly menuState = inject(MenuStateService);

  protected readonly authService = this.auth;
  protected readonly menuOpen = this.menuState.open;
  protected readonly isLoginRoute = signal(false);

  async ngOnInit(): Promise<void> {
    try {
      const fromRedirect = await this.auth.handleRedirect();
      if (fromRedirect && this.auth.isLoggedIn()) {
        this.teams.applyAuthIdentity();
        void this.router.navigateByUrl('/dashboard');
      }
    } catch (err) {
      console.warn('MSAL redirect handling failed', err);
    }

    void this.teams.initialize();
    this.syncRoute(this.router.url);
    this.router.events
      .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
      .subscribe(e => {
        this.syncRoute(e.urlAfterRedirects);
        this.closeMenu();
      });
  }

  protected toggleMenu(): void {
    this.menuState.toggle();
  }

  protected closeMenu(): void {
    this.menuState.close();
  }

  protected signOut(): void {
    this.auth.logout();
    void this.router.navigateByUrl('/login');
  }

  private syncRoute(url: string): void {
    const path = url.split('?')[0];
    this.isLoginRoute.set(path === '/login');
  }
}
