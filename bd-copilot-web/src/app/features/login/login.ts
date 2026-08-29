import { CUSTOM_ELEMENTS_SCHEMA, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  templateUrl: './login.html',
  schemas: [CUSTOM_ELEMENTS_SCHEMA]
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected username = 'admin';
  protected password = 'admin123';
  protected readonly busy = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected submit(): void {
    this.busy.set(true);
    this.errorMessage.set(null);
    this.auth.login(this.username, this.password).subscribe({
      next: () => {
        this.busy.set(false);
        void this.router.navigateByUrl('/admin');
      },
      error: err => {
        console.error(err);
        this.busy.set(false);
        this.errorMessage.set(err?.error?.title ?? 'Login failed.');
      }
    });
  }
}
