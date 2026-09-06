import { CUSTOM_ELEMENTS_SCHEMA, Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../core/services/auth.service';
import { TeamsService } from '../../core/services/teams.service';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  templateUrl: './login.html',
  schemas: [CUSTOM_ELEMENTS_SCHEMA]
})
export class Login implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly teams = inject(TeamsService);
  private readonly router = inject(Router);

  protected username = 'admin';
  protected password = 'admin123';
  protected readonly busy = signal(false);
  protected readonly entraBusy = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly entraEnabled = this.auth.entraEnabled;
  protected readonly allowPilot = this.auth.allowPilotAdminLogin;

  ngOnInit(): void {
    this.auth.ensureConfigured().subscribe({
      next: () => {
        if (this.auth.isLoggedIn()) {
          void this.router.navigateByUrl('/dashboard');
        }
      },
      error: () => {
        /* pilot login still available */
      }
    });
  }

  protected submit(): void {
    this.busy.set(true);
    this.errorMessage.set(null);
    this.auth.login(this.username, this.password).subscribe({
      next: () => {
        this.busy.set(false);
        this.teams.applyAuthIdentity();
        void this.router.navigateByUrl('/dashboard');
      },
      error: err => {
        console.error(err);
        this.busy.set(false);
        this.errorMessage.set(err?.error?.title ?? 'Login failed.');
      }
    });
  }

  protected async signInWithMicrosoft(): Promise<void> {
    this.entraBusy.set(true);
    this.errorMessage.set(null);
    try {
      await this.auth.loginWithMicrosoft();
    } catch (err: unknown) {
      console.error(err);
      this.entraBusy.set(false);
      this.errorMessage.set(err instanceof Error ? err.message : 'Microsoft sign-in failed.');
    }
  }
}
