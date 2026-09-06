import { CUSTOM_ELEMENTS_SCHEMA, Component, OnInit, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { DashboardSummary } from '../../core/models/api-models';

@Component({
  selector: 'app-dashboard',
  templateUrl: './dashboard.html',
  imports: [DecimalPipe, RouterLink],
  schemas: [CUSTOM_ELEMENTS_SCHEMA]
})
export class Dashboard implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly auth = inject(AuthService);

  protected readonly summary = signal<DashboardSummary | null>(null);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    this.api.getDashboardSummary().subscribe({
      next: s => {
        this.summary.set(s);
        this.loading.set(false);
      },
      error: err => {
        console.error(err);
        this.errorMessage.set('Could not load dashboard — confirm you are signed in and the API is running.');
        this.loading.set(false);
      }
    });
  }
}
