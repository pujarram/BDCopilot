import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from './api.service';
import { DynamicsDealContext, Opportunity } from '../models/api-models';

export interface GeneratePursuitContext {
  opportunityId: string;
  name: string;
  client: string;
  stage: string;
  dealSize?: number | null;
  notes?: string | null;
  dynamicsOpportunityId?: string | null;
  /** Prefill text for generators (Dynamics + pursuit notes). */
  focusNotes: string;
  /** Optional Dynamics CRM id when resolved. */
  dynamicsDealId?: string;
}

/**
 * Phase 3 — shared pursuit / Dynamics context for the Generate hub.
 * Select a pursuit once; child generators apply customer + focus notes.
 */
@Injectable({ providedIn: 'root' })
export class GeneratePursuitContextService {
  private readonly api = inject(ApiService);

  readonly opportunities = signal<Opportunity[]>([]);
  readonly dynamicsDeals = signal<DynamicsDealContext[]>([]);
  readonly loading = signal(false);
  readonly context = signal<GeneratePursuitContext | null>(null);
  readonly errorMessage = signal<string | null>(null);

  readonly hasContext = computed(() => !!this.context());
  readonly bannerLabel = computed(() => {
    const c = this.context();
    if (!c) return null;
    return `${c.name} · ${c.client}${c.stage ? ` · ${c.stage}` : ''}`;
  });

  loadLists(): void {
    this.loading.set(true);
    this.errorMessage.set(null);
    let pending = 2;
    const done = () => {
      pending -= 1;
      if (pending === 0) this.loading.set(false);
    };

    this.api.listOpportunities().subscribe({
      next: rows => {
        this.opportunities.set(rows);
        done();
      },
      error: err => {
        console.error(err);
        this.errorMessage.set('Could not load pursuits.');
        done();
      }
    });

    this.api.listDynamicsDeals().subscribe({
      next: deals => {
        this.dynamicsDeals.set(deals);
        done();
      },
      error: err => {
        console.error(err);
        done();
      }
    });
  }

  clear(): void {
    this.context.set(null);
  }

  /** Apply a local opportunity; enrich with Dynamics when client / Dynamics id matches. */
  selectOpportunity(opp: Opportunity): void {
    this.applyOpportunity(opp);
  }

  selectOpportunityById(id: string): void {
    const opp = this.opportunities().find(o => o.id === id);
    if (opp) {
      this.applyOpportunity(opp);
      return;
    }
    this.api.listOpportunities().subscribe({
      next: rows => {
        this.opportunities.set(rows);
        const found = rows.find(o => o.id === id);
        if (found) this.applyOpportunity(found);
        else this.errorMessage.set('Pursuit not found.');
      },
      error: err => {
        console.error(err);
        this.errorMessage.set('Could not load pursuit.');
      }
    });
  }

  /** Apply a Dynamics demo deal as ad-hoc context (no local Guid until linked). */
  selectDynamicsDeal(deal: DynamicsDealContext): void {
    const focus = this.buildFocusNotes({
      client: deal.client,
      name: deal.name,
      stage: deal.stage,
      notes: deal.focusNotes,
      dealSize: deal.estimatedValue
    });
    this.context.set({
      opportunityId: '',
      name: deal.name,
      client: deal.client,
      stage: deal.stage,
      dealSize: deal.estimatedValue,
      notes: deal.focusNotes,
      dynamicsOpportunityId: deal.opportunityId,
      dynamicsDealId: deal.opportunityId,
      focusNotes: focus
    });
  }

  private applyOpportunity(opp: Opportunity): void {
    const dyn =
      this.dynamicsDeals().find(
        d =>
          (!!opp.dynamicsOpportunityId && d.opportunityId === opp.dynamicsOpportunityId)
          || d.client.toLowerCase() === opp.client.toLowerCase()
      ) ?? null;

    const focus = this.buildFocusNotes({
      client: opp.client,
      name: opp.name,
      stage: opp.stage,
      notes: [opp.notes, dyn?.focusNotes].filter(Boolean).join('\n\n') || null,
      dealSize: opp.dealSize ?? dyn?.estimatedValue
    });

    this.context.set({
      opportunityId: opp.id,
      name: opp.name,
      client: opp.client,
      stage: opp.stage,
      dealSize: opp.dealSize,
      notes: opp.notes,
      dynamicsOpportunityId: opp.dynamicsOpportunityId ?? dyn?.opportunityId ?? null,
      dynamicsDealId: dyn?.opportunityId,
      focusNotes: focus
    });

    // Async enrich if Dynamics not already matched but client known
    if (!dyn) {
      this.api.getDynamicsDealContext(opp.dynamicsOpportunityId ?? undefined, opp.client).subscribe({
        next: deal => {
          const current = this.context();
          if (!current || current.opportunityId !== opp.id) return;
          const enriched = this.buildFocusNotes({
            client: deal.client || opp.client,
            name: opp.name,
            stage: deal.stage || opp.stage,
            notes: [opp.notes, deal.focusNotes].filter(Boolean).join('\n\n') || null,
            dealSize: opp.dealSize ?? deal.estimatedValue
          });
          this.context.set({
            ...current,
            client: deal.client || current.client,
            focusNotes: enriched,
            dynamicsDealId: deal.opportunityId,
            dynamicsOpportunityId: current.dynamicsOpportunityId ?? deal.opportunityId
          });
        },
        error: () => { /* demo Dynamics optional */ }
      });
    }
  }

  private buildFocusNotes(parts: {
    client: string;
    name: string;
    stage: string;
    notes?: string | null;
    dealSize?: number | null;
  }): string {
    const lines = [
      `Pursuit: ${parts.name}`,
      `Client: ${parts.client}`,
      `Stage: ${parts.stage}`,
      parts.dealSize != null ? `Deal size: ${parts.dealSize}` : null,
      parts.notes?.trim() ? `CRM / pursuit notes:\n${parts.notes.trim()}` : null,
      'Prefer win-language and prior approved responses for similar clients.'
    ].filter(Boolean) as string[];
    return lines.join('\n');
  }
}
