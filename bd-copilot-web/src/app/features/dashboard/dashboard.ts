import { CUSTOM_ELEMENTS_SCHEMA, Component, OnInit, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { CopilotChatService } from '../../core/services/copilot-chat.service';
import { CopilotPanelStateService } from '../../core/services/copilot-panel-state.service';
import {
  DashboardQuickLink,
  DashboardSummary,
  PlannerStalledAlert
} from '../../core/models/api-models';

/** Seller quick actions aligned with Phase 1 nav (Home · Ask · Generate · Library · Pursuits). */
const SELLER_QUICK_PATHS = new Set(['/chat', '/generate', '/library', '/pursuits']);

@Component({
  selector: 'app-dashboard',
  templateUrl: './dashboard.html',
  imports: [DecimalPipe, RouterLink],
  schemas: [CUSTOM_ELEMENTS_SCHEMA]
})
export class Dashboard implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly chat = inject(CopilotChatService);
  private readonly panel = inject(CopilotPanelStateService);
  protected readonly auth = inject(AuthService);

  protected readonly summary = signal<DashboardSummary | null>(null);
  protected readonly stalled = signal<PlannerStalledAlert[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly quickLinks = computed((): DashboardQuickLink[] => {
    const links = this.summary()?.quickLinks ?? [];
    return links.filter(link => {
      if (SELLER_QUICK_PATHS.has(link.path)) return true;
      if (this.auth.isAdmin() && (link.path === '/admin' || link.path === '/analytics')) return true;
      return false;
    });
  });

  protected readonly showRiskStrip = computed(() => {
    const s = this.summary();
    if (!s) return false;
    const delayed = s.plannerHealth?.delayed ?? 0;
    const risk = (s.projectInsights?.riskLevel ?? s.plannerHealth?.riskLevel ?? '').toLowerCase();
    return delayed > 0 || (risk.length > 0 && risk !== 'low') || this.stalled().length > 0;
  });

  ngOnInit(): void {
    this.api.getDashboardSummary().subscribe({
      next: s => {
        this.summary.set(s);
        this.loading.set(false);
        const delayed = s.plannerHealth?.delayed ?? 0;
        const risk = (s.projectInsights?.riskLevel ?? s.plannerHealth?.riskLevel ?? '').toLowerCase();
        if (delayed > 0 || (risk && risk !== 'low')) {
          this.loadStalled();
        }
      },
      error: err => {
        console.error(err);
        this.errorMessage.set('Could not load dashboard — confirm you are signed in and the API is running.');
        this.loading.set(false);
      }
    });
  }

  protected askAboutRisk(): void {
    const titles = this.stalled()
      .slice(0, 3)
      .map(a => a.title)
      .filter(Boolean);
    const delayed = this.summary()?.plannerHealth?.delayed ?? 0;
    const prompt =
      titles.length > 0
        ? `What is blocking progress on these stalled or delayed items, and what should I escalate? ${titles.join('; ')}`
        : `We have ${delayed} delayed Planner task(s). Summarize delivery risk and what BD should escalate.`;
    this.chat.send(prompt);
    this.panel.openPanel();
    void this.router.navigateByUrl('/chat');
  }

  private loadStalled(): void {
    this.api.getPlannerStalledAlerts(7).subscribe({
      next: rows => this.stalled.set((rows ?? []).slice(0, 3)),
      error: err => console.error(err)
    });
  }
}
