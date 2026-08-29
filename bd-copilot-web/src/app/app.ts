import { Component, OnInit, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs/operators';
import { TeamsService } from './core/services/teams.service';
import { Toast } from './shell/toast/toast';
import { MenuStateService } from './core/services/menu-state.service';
import { TietoIconComponent } from './shared/tieto-icon.component';

const DOC_PREFIXES = ['/architecture', '/stack', '/roadmap'];

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Toast, TietoIconComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App implements OnInit {
  private readonly teams = inject(TeamsService);
  private readonly router = inject(Router);
  private readonly menuState = inject(MenuStateService);

  protected readonly user = this.teams.user;
  protected readonly menuOpen = this.menuState.open;
  protected readonly isPrototypeRoute = signal(true);

  ngOnInit(): void {
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

  private syncRoute(url: string): void {
    const path = url.split('?')[0];
    this.isPrototypeRoute.set(!DOC_PREFIXES.some(p => path.startsWith(p)));
  }
}
