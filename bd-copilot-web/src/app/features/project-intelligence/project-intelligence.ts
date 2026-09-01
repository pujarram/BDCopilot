import { CUSTOM_ELEMENTS_SCHEMA, Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { ToastService } from '../../core/services/toast.service';
import {
  PlannerHealthSummary,
  PlannerTaskListItem,
  PlannerWorkloadRow,
  ProjectManagerInsight,
  StakeholderWeeklyReport,
  PlannerBurndownPoint,
  UnifiedIntelligenceResponse
} from '../../core/models/api-models';

type PiView = 'overview' | 'gantt' | 'timeline' | 'workload' | 'insights' | 'burndown' | 'unified';

interface GanttRow {
  task: PlannerTaskListItem;
  leftPct: number;
  widthPct: number;
  label: string;
}

interface TimelineGroup {
  bucket: string;
  tasks: PlannerTaskListItem[];
}

interface TimelineMarker {
  label: string;
  leftPct: number;
}

@Component({
  selector: 'app-project-intelligence',
  templateUrl: './project-intelligence.html',
  imports: [DatePipe],
  schemas: [CUSTOM_ELEMENTS_SCHEMA]
})
export class ProjectIntelligence implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly health = signal<PlannerHealthSummary | null>(null);
  protected readonly tasks = signal<PlannerTaskListItem[]>([]);
  protected readonly workload = signal<PlannerWorkloadRow[]>([]);
  protected readonly insights = signal<ProjectManagerInsight | null>(null);
  protected readonly report = signal<StakeholderWeeklyReport | null>(null);
  protected readonly reportBusy = signal(false);
  protected readonly burndown = signal<PlannerBurndownPoint[]>([]);
  protected readonly unified = signal<UnifiedIntelligenceResponse | null>(null);
  protected readonly unifiedBusy = signal(false);
  protected readonly unifiedQuery = signal('');
  protected readonly loading = signal(true);
  protected readonly syncBusy = signal(false);
  protected readonly delayedOnly = signal(false);
  protected readonly assigneeFilter = signal<string | null>(null);
  protected readonly dueFrom = signal<string | null>(null);
  protected readonly dueTo = signal<string | null>(null);
  protected readonly view = signal<PiView>('overview');
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly range = computed(() => this.computeRange(this.tasks()));
  protected readonly ganttRows = computed(() => this.buildGanttRows(this.tasks(), this.range()));
  protected readonly ganttMarkers = computed(() => this.buildMarkers(this.range()));
  protected readonly timelineGroups = computed(() => this.buildTimelineGroups(this.tasks()));
  protected readonly todayPct = computed(() => {
    const r = this.range();
    if (!r) return null;
    const now = Date.now();
    if (now < r.startMs || now > r.endMs) return null;
    return ((now - r.startMs) / r.spanMs) * 100;
  });

  ngOnInit(): void {
    this.applyQueryParams(this.route.snapshot.queryParamMap);
    this.reload();
  }

  protected setView(v: PiView): void {
    this.view.set(v);
    this.patchQuery({ view: v });
    if (v === 'workload' && this.workload().length === 0) {
      this.loadWorkload();
    }
    if (v === 'insights' && !this.insights()) {
      this.loadInsights();
    }
    if (v === 'burndown' && this.burndown().length === 0) {
      this.loadBurndown();
    }
  }

  protected toggleDelayed(): void {
    this.delayedOnly.update(v => !v);
    this.patchQuery({ delayedOnly: this.delayedOnly() ? 'true' : null });
    this.loadTasks();
  }

  protected remaining(w: PlannerWorkloadRow): number {
    return Math.max(w.totalTasks - w.completed - w.inProgress, 0);
  }

  protected loadReport(): void {
    this.reportBusy.set(true);
    this.api.getPlannerStakeholderReport(true).subscribe({
      next: r => {
        this.report.set(r);
        this.reportBusy.set(false);
      },
      error: err => {
        console.error(err);
        this.reportBusy.set(false);
        this.toast.show('Could not generate stakeholder report.');
      }
    });
  }

  protected downloadReportDocx(): void {
    window.open(this.api.plannerReportDocxUrl(true), '_blank');
  }

  protected runUnified(): void {
    this.unifiedBusy.set(true);
    this.api
      .queryUnifiedIntelligence({
        query: this.unifiedQuery().trim() || null,
        userObjectId: 'web-project-intelligence',
        includeDelayedTasks: true,
        includeSharePoint: true,
        useLlmSummary: true
      })
      .subscribe({
        next: r => {
          this.unified.set(r);
          this.unifiedBusy.set(false);
        },
        error: err => {
          console.error(err);
          this.unifiedBusy.set(false);
          this.toast.show('Unified query failed.');
        }
      });
  }

  protected burndownMaxCompleted(): number {
    const rows = this.burndown();
    return Math.max(...rows.map(r => r.completed), 1);
  }

  protected sync(): void {
    this.syncBusy.set(true);
    this.api.syncPlanner().subscribe({
      next: result => {
        this.syncBusy.set(false);
        this.toast.show(result.statusMessage || 'Planner sync finished.');
        this.reload();
      },
      error: err => {
        console.error(err);
        this.syncBusy.set(false);
        this.toast.show('Planner sync failed — see console.');
      }
    });
  }

  private applyQueryParams(q: { get(name: string): string | null }): void {
    const view = (q.get('view') || '').toLowerCase();
    if (view === 'gantt' || view === 'timeline' || view === 'workload' || view === 'overview' || view === 'insights' || view === 'burndown' || view === 'unified') {
      this.view.set(view);
    }
    this.delayedOnly.set(q.get('delayedOnly') === 'true' || q.get('delayedOnly') === '1');
    const assignee = q.get('assignee')?.trim();
    this.assigneeFilter.set(assignee || null);
    this.dueFrom.set(q.get('dueFrom') || null);
    this.dueTo.set(q.get('dueTo') || null);
  }

  private patchQuery(patch: Record<string, string | null>): void {
    const next: Record<string, string | null> = {
      view: this.view(),
      delayedOnly: this.delayedOnly() ? 'true' : null,
      assignee: this.assigneeFilter(),
      dueFrom: this.dueFrom(),
      dueTo: this.dueTo(),
      ...patch
    };
    const queryParams: Record<string, string | null> = {};
    for (const [k, v] of Object.entries(next)) {
      queryParams[k] = v && v.length ? v : null;
    }
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      queryParamsHandling: 'merge',
      replaceUrl: true
    });
  }

  private reload(): void {
    this.loading.set(true);
    this.errorMessage.set(null);
    this.api.getPlannerHealth().subscribe({
      next: h => {
        this.health.set(h);
        this.loadTasks();
        if (this.view() === 'workload') this.loadWorkload();
        if (this.view() === 'insights') this.loadInsights();
        if (this.view() === 'burndown') this.loadBurndown();
      },
      error: err => {
        console.error(err);
        this.errorMessage.set('Could not load Project Intelligence — is the API running?');
        this.loading.set(false);
      }
    });
  }

  private loadTasks(): void {
    this.api
      .listPlannerTasks({
        delayedOnly: this.delayedOnly(),
        assignee: this.assigneeFilter() ?? undefined,
        dueFrom: this.dueFrom() ?? undefined,
        dueTo: this.dueTo() ?? undefined
      })
      .subscribe({
        next: rows => {
          this.tasks.set(rows);
          this.loading.set(false);
        },
        error: err => {
          console.error(err);
          this.loading.set(false);
          this.errorMessage.set('Could not load Planner tasks.');
        }
      });
  }

  private loadWorkload(): void {
    this.api.getPlannerWorkload().subscribe({
      next: rows => this.workload.set(rows),
      error: err => console.error(err)
    });
  }

  private loadInsights(): void {
    this.api.getPlannerInsights().subscribe({
      next: i => this.insights.set(i),
      error: err => {
        console.error(err);
        this.toast.show('Could not load AI Project Manager insights.');
      }
    });
  }

  private loadBurndown(): void {
    this.api.getPlannerBurndown(30).subscribe({
      next: rows => this.burndown.set(rows),
      error: err => console.error(err)
    });
  }

  private computeRange(tasks: PlannerTaskListItem[]): { startMs: number; endMs: number; spanMs: number } | null {
    const dated = tasks
      .map(t => {
        const start = t.startDate ? Date.parse(t.startDate) : NaN;
        const due = t.dueDate ? Date.parse(t.dueDate) : NaN;
        if (Number.isNaN(start) && Number.isNaN(due)) return null;
        const s = !Number.isNaN(start) ? start : due - 3 * 86400000;
        const e = !Number.isNaN(due) ? due : start + 3 * 86400000;
        return { s, e: Math.max(e, s + 86400000) };
      })
      .filter((x): x is { s: number; e: number } => x !== null);

    if (!dated.length) return null;

    const startMs = Math.min(...dated.map(d => d.s));
    const endMs = Math.max(...dated.map(d => d.e));
    const pad = 2 * 86400000;
    const s = startMs - pad;
    const e = endMs + pad;
    return { startMs: s, endMs: e, spanMs: Math.max(e - s, 86400000) };
  }

  private buildGanttRows(
    tasks: PlannerTaskListItem[],
    range: { startMs: number; endMs: number; spanMs: number } | null
  ): GanttRow[] {
    if (!range) return [];

    return tasks
      .map(task => {
        const start = task.startDate ? Date.parse(task.startDate) : NaN;
        const due = task.dueDate ? Date.parse(task.dueDate) : NaN;
        if (Number.isNaN(start) && Number.isNaN(due)) return null;

        const s = !Number.isNaN(start) ? start : due - 3 * 86400000;
        const e = !Number.isNaN(due) ? Math.max(due, s + 86400000) : s + 3 * 86400000;
        const leftPct = Math.max(0, ((s - range.startMs) / range.spanMs) * 100);
        const widthPct = Math.max(1.2, ((e - s) / range.spanMs) * 100);

        return {
          task,
          leftPct,
          widthPct: Math.min(widthPct, 100 - leftPct),
          label: `${task.percentComplete}%`
        } satisfies GanttRow;
      })
      .filter((x): x is GanttRow => x !== null);
  }

  private buildMarkers(range: { startMs: number; endMs: number; spanMs: number } | null): TimelineMarker[] {
    if (!range) return [];

    const markers: TimelineMarker[] = [];
    const start = new Date(range.startMs);
    start.setUTCDate(1);
    start.setUTCHours(0, 0, 0, 0);

    for (let t = start.getTime(); t <= range.endMs; t = this.addMonthsUtc(t, 1)) {
      if (t < range.startMs - 86400000) continue;
      markers.push({
        label: new Date(t).toLocaleString('en', { month: 'short', year: '2-digit', timeZone: 'UTC' }),
        leftPct: ((t - range.startMs) / range.spanMs) * 100
      });
    }

    return markers.slice(0, 12);
  }

  private addMonthsUtc(ms: number, months: number): number {
    const d = new Date(ms);
    d.setUTCMonth(d.getUTCMonth() + months);
    return d.getTime();
  }

  private buildTimelineGroups(tasks: PlannerTaskListItem[]): TimelineGroup[] {
    const map = new Map<string, PlannerTaskListItem[]>();
    for (const t of tasks) {
      const key = t.bucketName?.trim() || 'Uncategorized';
      if (!map.has(key)) map.set(key, []);
      map.get(key)!.push(t);
    }

    const order = ['Backlog', 'In Progress', 'Testing', 'Completed', 'Uncategorized'];
    return [...map.entries()]
      .map(([bucket, list]) => ({
        bucket,
        tasks: [...list].sort((a, b) => {
          const da = a.dueDate ? Date.parse(a.dueDate) : Number.MAX_SAFE_INTEGER;
          const db = b.dueDate ? Date.parse(b.dueDate) : Number.MAX_SAFE_INTEGER;
          return da - db;
        })
      }))
      .sort((a, b) => {
        const ia = order.indexOf(a.bucket);
        const ib = order.indexOf(b.bucket);
        return (ia === -1 ? 99 : ia) - (ib === -1 ? 99 : ib) || a.bucket.localeCompare(b.bucket);
      });
  }
}
