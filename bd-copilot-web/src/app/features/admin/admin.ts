import { CUSTOM_ELEMENTS_SCHEMA, Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { TeamsService } from '../../core/services/teams.service';
import { ToastService } from '../../core/services/toast.service';
import { AuthService } from '../../core/services/auth.service';
import {
  AccessAuditRecord,
  AdminTelemetrySummary,
  SyncHealthStatus,
  TeamTokenCostRow,
  TenantCutoverStatus
} from '../../core/models/api-models';

@Component({
  selector: 'app-admin',
  templateUrl: './admin.html',
  imports: [DatePipe, DecimalPipe, RouterLink],
  schemas: [CUSTOM_ELEMENTS_SCHEMA]
})
export class Admin implements OnInit {
  private readonly api = inject(ApiService);
  private readonly teams = inject(TeamsService);
  private readonly toast = inject(ToastService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly authService = this.auth;
  protected readonly syncHealth = signal<SyncHealthStatus | null>(null);
  protected readonly tokenCosts = signal<TeamTokenCostRow[]>([]);
  protected readonly accessAudits = signal<AccessAuditRecord[]>([]);
  protected readonly telemetry = signal<AdminTelemetrySummary | null>(null);
  protected readonly cutover = signal<TenantCutoverStatus | null>(null);
  protected readonly goldenEvalResult = signal<string | null>(null);

  protected readonly loading = signal(true);
  protected readonly reindexBusy = signal(false);
  protected readonly evalBusy = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    this.loadAll();
  }

  protected logout(): void {
    this.auth.logout();
    void this.router.navigateByUrl('/login');
  }

  protected reindex(): void {
    this.reindexBusy.set(true);
    this.api.reindex().subscribe({
      next: health => {
        this.syncHealth.set(health);
        this.reindexBusy.set(false);
        this.toast.show('Reindex completed.');
      },
      error: err => {
        console.error(err);
        this.reindexBusy.set(false);
        this.toast.show('Reindex failed — see the browser console.');
      }
    });
  }

  protected runGoldenEval(): void {
    this.evalBusy.set(true);
    this.goldenEvalResult.set(null);
    this.api.runGoldenEval(this.teams.user().objectId).subscribe({
      next: result => {
        this.goldenEvalResult.set(JSON.stringify(result, null, 2));
        this.evalBusy.set(false);
        this.toast.show('Golden eval completed.');
      },
      error: err => {
        console.error(err);
        this.evalBusy.set(false);
        this.toast.show('Golden eval failed — see the browser console.');
      }
    });
  }

  private loadAll(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    let pending = 5;
    const done = () => {
      pending -= 1;
      if (pending === 0) this.loading.set(false);
    };

    this.api.getSyncHealth().subscribe({
      next: health => { this.syncHealth.set(health); done(); },
      error: err => { console.error(err); this.errorMessage.set('Could not load admin data — see the browser console.'); done(); }
    });

    this.api.getTokenCosts().subscribe({
      next: rows => { this.tokenCosts.set(rows); done(); },
      error: err => { console.error(err); this.errorMessage.set('Could not load admin data — see the browser console.'); done(); }
    });

    this.api.getAccessAudits(50).subscribe({
      next: rows => { this.accessAudits.set(rows); done(); },
      error: err => { console.error(err); this.errorMessage.set('Could not load admin data — see the browser console.'); done(); }
    });

    this.api.getAdminTelemetry().subscribe({
      next: t => { this.telemetry.set(t); done(); },
      error: err => { console.error(err); done(); }
    });

    this.api.getCutoverStatus().subscribe({
      next: c => { this.cutover.set(c); done(); },
      error: err => { console.error(err); done(); }
    });
  }
}
