import { CUSTOM_ELEMENTS_SCHEMA, Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { ApiService } from '../../core/services/api.service';
import { TeamsService } from '../../core/services/teams.service';
import { ToastService } from '../../core/services/toast.service';
import {
  DynamicsDealContext,
  GeneratedDocument,
  GeneratedSection,
  MultiApprovalStatus,
  RfpStreamEvent
} from '../../core/models/api-models';

@Component({
  selector: 'app-competitive',
  templateUrl: './competitive.html',
  schemas: [CUSTOM_ELEMENTS_SCHEMA]
})
export class Competitive implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly teams = inject(TeamsService);
  private readonly toast = inject(ToastService);
  private abort: AbortController | null = null;

  protected readonly competitor = signal('Incumbent Chatbot Vendor');
  protected readonly ourSolution = signal('BD Copilot grounded assistant');
  protected readonly customerContext = signal('');
  protected readonly complianceRegion = signal('EU');
  protected readonly deals = signal<DynamicsDealContext[]>([]);
  protected readonly generating = signal(false);
  protected readonly result = signal<GeneratedDocument | null>(null);
  protected readonly approval = signal<MultiApprovalStatus | null>(null);
  protected readonly progressLabel = signal('');
  protected readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    this.api.listDynamicsDeals().subscribe({
      next: d => this.deals.set(d),
      error: err => console.error(err)
    });
  }

  ngOnDestroy(): void {
    this.abort?.abort();
  }

  protected applyDeal(deal: DynamicsDealContext): void {
    this.customerContext.set(deal.focusNotes);
    this.toast.show(`Pulled Dynamics context for ${deal.client}.`);
  }

  protected async generate(): Promise<void> {
    this.abort?.abort();
    this.abort = new AbortController();
    this.generating.set(true);
    this.result.set(null);
    this.approval.set(null);
    this.errorMessage.set(null);
    this.progressLabel.set('Generating competitive brief…');

    try {
      let doc: GeneratedDocument | null = null;
      for await (const evt of this.api.generateCompetitiveStream(
        {
          competitor: this.competitor(),
          ourSolution: this.ourSolution(),
          customerContext: this.customerContext() || undefined,
          userObjectId: this.teams.user().objectId,
          complianceRegion: this.complianceRegion()
        },
        this.abort.signal
      )) {
        this.applyEvent(evt);
        if (evt.type === 'complete' && evt.document) doc = evt.document;
      }
      if (doc) {
        this.result.set(doc);
        this.startApprovals(doc);
      }
    } catch (err) {
      if (!this.abort?.signal.aborted) {
        console.error(err);
        this.errorMessage.set('Competitive generation failed — is the API running?');
      }
    } finally {
      this.generating.set(false);
      this.progressLabel.set('');
    }
  }

  private applyEvent(evt: RfpStreamEvent): void {
    if (evt.type === 'outline') this.progressLabel.set('Outline ready…');
    if (evt.type === 'section-start') this.progressLabel.set(`Writing ${evt.title}…`);
  }

  private startApprovals(doc: GeneratedDocument): void {
    this.api.startMultiApproval({
      generationId: doc.generationId,
      documentTitle: doc.title,
      userObjectId: this.teams.user().objectId
    }).subscribe({
      next: s => this.approval.set(s),
      error: err => console.error(err)
    });
  }

  protected decide(role: string, status: 'Approved' | 'Rejected'): void {
    const doc = this.result();
    if (!doc) return;
    this.api.decideApproval({
      generationId: doc.generationId,
      role,
      status,
      userObjectId: this.teams.user().objectId,
      displayName: this.teams.user().displayName
    }).subscribe({
      next: s => {
        this.approval.set(s);
        if (s.isFullyApproved) {
          this.result.update(d => d ? { ...d, status: 'Approved' } : d);
          this.toast.show('Legal + Sales approved.');
        }
      },
      error: err => { console.error(err); this.toast.show('Approval update failed.'); }
    });
  }

  protected sections(): GeneratedSection[] {
    return this.result()?.sections ?? [];
  }
}
