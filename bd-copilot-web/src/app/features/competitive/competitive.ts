import { CUSTOM_ELEMENTS_SCHEMA, Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { ApiService } from '../../core/services/api.service';
import { TeamsService } from '../../core/services/teams.service';
import { ToastService } from '../../core/services/toast.service';
import {
  Citation,
  DynamicsDealContext,
  GeneratedDocument,
  MultiApprovalStatus,
  RfpStreamEvent
} from '../../core/models/api-models';

type StreamStatus = 'queued' | 'writing-title' | 'generating' | 'done' | 'error';

interface StreamSection {
  order: number;
  title: string;
  displayTitle: string;
  content: string;
  status: StreamStatus;
  sources: Citation[];
}

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
  protected readonly streaming = signal(false);
  protected readonly result = signal<GeneratedDocument | null>(null);
  protected readonly approval = signal<MultiApprovalStatus | null>(null);
  protected readonly progressLabel = signal('');
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly streamSections = signal<StreamSection[]>([]);

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
    this.streaming.set(true);
    this.result.set(null);
    this.approval.set(null);
    this.errorMessage.set(null);
    this.streamSections.set([]);
    this.progressLabel.set('Connecting to BD Copilot…');

    try {
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
        if (this.abort.signal.aborted) return;
        this.applyStreamEvent(evt);
      }
    } catch (err) {
      if (!this.abort?.signal.aborted) {
        console.error(err);
        this.errorMessage.set('Competitive generation failed — is the API running?');
        this.generating.set(false);
        this.streaming.set(false);
        this.progressLabel.set('');
      }
    }
  }

  private applyStreamEvent(evt: RfpStreamEvent): void {
    switch (evt.type) {
      case 'outline': {
        const titles = evt.sectionTitles ?? [];
        this.progressLabel.set('Outline ready — retrieving sources…');
        this.streamSections.set(
          titles.map((title, i) => ({
            order: i + 1,
            title,
            displayTitle: title,
            content: '',
            status: 'queued' as StreamStatus,
            sources: []
          }))
        );
        break;
      }
      case 'section-start': {
        const order = evt.order ?? 1;
        this.generating.set(false);
        this.progressLabel.set(`Writing ${String(order).padStart(2, '0')} · ${evt.title ?? ''}`);
        this.patchByOrder(order, s => ({
          ...s,
          title: evt.title ?? s.title,
          displayTitle: evt.title ?? s.displayTitle,
          status: 'generating',
          content: '',
          sources: evt.sources ?? []
        }));
        break;
      }
      case 'token': {
        const order = evt.order ?? 1;
        const delta = evt.delta ?? '';
        if (!delta) return;
        this.patchByOrder(order, s => ({ ...s, status: 'generating', content: s.content + delta }));
        break;
      }
      case 'section-done': {
        const order = evt.order ?? 1;
        this.patchByOrder(order, s => ({
          ...s,
          status: 'done',
          content: evt.content ?? s.content,
          sources: evt.sources ?? s.sources
        }));
        break;
      }
      case 'complete': {
        if (evt.document) {
          this.result.set(evt.document);
          this.startApprovals(evt.document);
          this.toast.show(`Generated ${evt.document.sections.length} sections.`);
        }
        this.generating.set(false);
        this.streaming.set(false);
        this.progressLabel.set('Draft ready for review');
        break;
      }
      case 'error': {
        this.errorMessage.set(evt.message ?? 'Generation failed.');
        this.generating.set(false);
        this.streaming.set(false);
        this.progressLabel.set('');
        break;
      }
    }
  }

  private patchByOrder(order: number, fn: (s: StreamSection) => StreamSection): void {
    this.streamSections.update(list => {
      if (!list.some(s => s.order === order)) {
        return [
          ...list,
          fn({
            order,
            title: '',
            displayTitle: '',
            content: '',
            status: 'queued',
            sources: []
          })
        ].sort((a, b) => a.order - b.order);
      }
      return list.map(s => (s.order === order ? fn(s) : s));
    });
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
}
