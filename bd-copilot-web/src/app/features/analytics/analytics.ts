import { CUSTOM_ELEMENTS_SCHEMA, Component, OnInit, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { ApiService } from '../../core/services/api.service';
import { CustomerUsageSummary, RoiDashboardSummary } from '../../core/models/api-models';

@Component({
  selector: 'app-analytics',
  templateUrl: './analytics.html',
  imports: [DecimalPipe],
  schemas: [CUSTOM_ELEMENTS_SCHEMA]
})
export class Analytics implements OnInit {
  private readonly api = inject(ApiService);

  protected readonly roi = signal<RoiDashboardSummary | null>(null);
  protected readonly usage = signal<CustomerUsageSummary | null>(null);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    let pending = 2;
    const done = () => {
      pending -= 1;
      if (pending === 0) this.loading.set(false);
    };
    this.api.getRoiDashboard().subscribe({
      next: r => { this.roi.set(r); done(); },
      error: err => { console.error(err); this.errorMessage.set('Could not load ROI analytics.'); done(); }
    });
    this.api.getCustomerUsage().subscribe({
      next: u => { this.usage.set(u); done(); },
      error: err => { console.error(err); done(); }
    });
  }
}
