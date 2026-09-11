import { CUSTOM_ELEMENTS_SCHEMA, Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ApiService } from '../../core/services/api.service';
import { TeamsService } from '../../core/services/teams.service';
import { ToastService } from '../../core/services/toast.service';
import { SearchReuseService, SearchReusePayload } from '../../core/services/search-reuse.service';
import { GeneratePursuitContextService } from '../../core/services/generate-pursuit-context.service';
import {
  GeneratedDocument,
  GeneratedSection,
  RfpDocument,
  RfpDocumentListItem,
  RfpStreamEvent,
  CorpusSource,
  ComplianceChecklistItem,
  GenerationVersionDiff
} from '../../core/models/api-models';

type StreamStatus = 'queued' | 'writing-title' | 'generating' | 'done' | 'error';

interface StreamSection {
  order: number;
  title: string;
  displayTitle: string;
  content: string;
  status: StreamStatus;
  sources: GeneratedSection['sources'];
}

@Component({
  selector: 'app-rfp-generator',
  templateUrl: './rfp-generator.html',
  imports: [DatePipe],
  schemas: [CUSTOM_ELEMENTS_SCHEMA]
})
export class RfpGenerator implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly teams = inject(TeamsService);
  private readonly toast = inject(ToastService);
  private readonly searchReuse = inject(SearchReuseService);
  private readonly pursuitCtx = inject(GeneratePursuitContextService);

  protected readonly title = signal('Wealth Management Platform — RFP Response');
  protected readonly customer = signal('Meridian Private Bank');
  protected readonly reuseScope = signal<'All' | 'Last12Months'>('All');
  protected readonly tone = signal('Formal');
  protected readonly corpusSource = signal<CorpusSource>('Online');
  protected readonly complianceRegion = signal('EU');
  protected readonly focusNotes = signal<string | null>(null);
  protected readonly linkedOpportunityId = signal<string | null>(null);
  protected readonly reuseBanner = signal<SearchReusePayload | null>(null);

  protected readonly generating = signal(false);
  protected readonly streaming = signal(false);
  protected readonly approving = signal(false);
  protected readonly exporting = signal(false);
  protected readonly saving = signal(false);
  protected readonly channelBusy = signal(false);
  protected readonly result = signal<GeneratedDocument | null>(null);
  protected readonly savedRfp = signal<RfpDocument | null>(null);
  protected readonly history = signal<RfpDocumentListItem[]>([]);
  protected readonly historyDetail = signal<RfpDocument | null>(null);
  protected readonly streamSections = signal<StreamSection[]>([]);
  protected readonly selectedSection = signal<GeneratedSection | null>(null);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly progressLabel = signal('');
  protected readonly dynamicsDeals = signal<import('../../core/models/api-models').DynamicsDealContext[]>([]);
  protected readonly multiApproval = signal<import('../../core/models/api-models').MultiApprovalStatus | null>(null);
  protected readonly showCompliance = signal(false);
  protected readonly complianceItems = signal<ComplianceChecklistItem[]>([]);
  protected readonly complianceBusy = signal(false);
  protected readonly versionDiff = signal<GenerationVersionDiff | null>(null);
  protected readonly diffBusy = signal(false);
  protected readonly snapshotCount = signal(0);

  private pendingExportFormat: 'docx' | 'pptx' | 'zip' = 'docx';
  private abort: AbortController | null = null;

  ngOnInit(): void {
    this.applyPursuitContext();
    this.applySearchReuse();
    this.refreshHistory();
    this.api.listDynamicsDeals().subscribe({
      next: d => this.dynamicsDeals.set(d),
      error: err => console.error(err)
    });
  }

  protected pullDynamics(clientHint?: string): void {
    this.api.getDynamicsDealContext(undefined, clientHint || this.customer()).subscribe({
      next: deal => {
        this.focusNotes.set(deal.focusNotes);
        this.customer.set(deal.client);
        this.toast.show(`Dynamics context loaded for ${deal.client}.`);
      },
      error: err => { console.error(err); this.toast.show('Dynamics context unavailable.'); }
    });
  }

  protected clearReuse(): void {
    this.reuseBanner.set(null);
    this.focusNotes.set(null);
  }

  private applyPursuitContext(): void {
    const ctx = this.pursuitCtx.context();
    if (!ctx) return;
    this.customer.set(ctx.client);
    this.focusNotes.set(ctx.focusNotes);
    if (ctx.opportunityId) this.linkedOpportunityId.set(ctx.opportunityId);
    this.title.set(`${ctx.name} — RFP Response`);
    this.toast.show(`Grounded on pursuit “${ctx.name}”.`);
  }

  private applySearchReuse(): void {
    const payload = this.searchReuse.consume('rfp');
    if (!payload) return;

    this.reuseBanner.set(payload);
    this.focusNotes.set(payload.excerpt);
    this.corpusSource.set(payload.corpusSource);
    if (payload.query) {
      this.title.set(`${payload.query} — RFP Response`);
    }
    this.toast.show(`Reusing “${payload.label}” from Knowledge Search.`);
  }

  ngOnDestroy(): void {
    this.abort?.abort();
  }

  protected generate(): void {
    this.abort?.abort();
    this.abort = new AbortController();

    this.generating.set(true);
    this.streaming.set(true);
    this.errorMessage.set(null);
    this.result.set(null);
    this.savedRfp.set(null);
    this.selectedSection.set(null);
    this.streamSections.set([]);
    this.progressLabel.set('Connecting to BD Copilot…');

    void this.runLiveStream(this.abort.signal);
  }

  protected isApproved(): boolean {
    return this.result()?.status === 'Approved';
  }

  protected approve(): void {
    const doc = this.result();
    if (!doc) return;

    this.approving.set(true);
    this.api
      .approveGeneration({
        generationId: doc.generationId,
        userObjectId: this.teams.user().objectId,
        document: doc
      })
      .subscribe({
        next: updated => {
          this.result.set(updated);
          this.approving.set(false);
          this.persistToDatabase(updated);
          this.api.startMultiApproval({
            generationId: updated.generationId,
            documentTitle: updated.title,
            userObjectId: this.teams.user().objectId
          }).subscribe({
            next: s => this.multiApproval.set(s),
            error: err => console.error(err)
          });
          this.toast.show('Draft approved — collect Legal + Sales sign-off for gated export.');
        },
        error: err => {
          console.error(err);
          this.approving.set(false);
          this.toast.show('Could not approve draft.');
        }
      });
  }

  protected decideReview(role: string, status: 'Approved' | 'Rejected'): void {
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
        this.multiApproval.set(s);
        if (s.isFullyApproved) this.toast.show('Legal + Sales both signed off.');
      },
      error: err => { console.error(err); this.toast.show('Review update failed.'); }
    });
  }

  protected exportDocx(): void {
    this.openComplianceGate('docx');
  }

  protected openComplianceGate(format: 'docx' | 'pptx' | 'zip' = 'docx'): void {
    const doc = this.result();
    if (!doc) return;
    this.pendingExportFormat = format;
    this.api.getComplianceChecklist(doc.generationId, doc.title).subscribe({
      next: checklist => {
        this.complianceItems.set(checklist.items.map(i => ({ ...i })));
        this.showCompliance.set(true);
      },
      error: err => {
        console.error(err);
        this.toast.show('Could not load compliance checklist.');
      }
    });
  }

  protected toggleComplianceItem(id: string): void {
    this.complianceItems.update(items =>
      items.map(i => (i.id === id ? { ...i, checked: !i.checked } : i))
    );
  }

  protected submitComplianceAndExport(): void {
    const doc = this.result();
    if (!doc) return;
    this.complianceBusy.set(true);
    this.api.submitComplianceChecklist({
      generationId: doc.generationId,
      userObjectId: this.teams.user().objectId,
      items: this.complianceItems()
    }).subscribe({
      next: result => {
        if (!result.readyForExport) {
          this.complianceBusy.set(false);
          this.toast.show(result.message);
          return;
        }
        this.showCompliance.set(false);
        this.complianceBusy.set(false);
        this.exportCurrent(this.pendingExportFormat);
      },
      error: err => {
        console.error(err);
        this.complianceBusy.set(false);
        this.toast.show('Compliance checklist submission failed.');
      }
    });
  }

  protected saveVersionSnapshot(): void {
    const doc = this.result();
    if (!doc) return;
    this.api.saveGenerationSnapshot({
      generationId: doc.generationId,
      userObjectId: this.teams.user().objectId,
      displayName: this.teams.user().displayName,
      document: doc
    }).subscribe({
      next: snap => {
        this.snapshotCount.update(n => Math.max(n, snap.versionNumber));
        this.toast.show(`Saved version ${snap.versionNumber} for diff review.`);
      },
      error: err => { console.error(err); this.toast.show('Could not save version snapshot.'); }
    });
  }

  protected loadVersionDiff(): void {
    const doc = this.result();
    if (!doc) return;
    this.diffBusy.set(true);
    this.api.diffGenerationVersions(doc.generationId).subscribe({
      next: diff => {
        this.versionDiff.set(diff);
        this.diffBusy.set(false);
      },
      error: err => {
        console.error(err);
        this.diffBusy.set(false);
        this.toast.show('Save a snapshot first to enable version diff.');
      }
    });
  }

  protected saveToDatabase(): void {
    const doc = this.result();
    if (!doc) return;
    this.persistToDatabase(doc, true);
  }

  protected saveToBdChannel(): void {
    const saved = this.savedRfp();
    if (!saved) {
      const doc = this.result();
      if (!doc) return;
      this.saving.set(true);
      this.api
        .saveRfpDocument({
          document: doc,
          userObjectId: this.teams.user().objectId,
          displayName: this.teams.user().displayName,
          customer: this.customer(),
          tone: this.tone()
        })
        .subscribe({
          next: row => {
            this.savedRfp.set(row);
            this.saving.set(false);
            this.uploadChannel(row.id);
          },
          error: err => {
            console.error(err);
            this.saving.set(false);
            this.toast.show('Could not save to database before channel upload.');
          }
        });
      return;
    }

    this.uploadChannel(saved.id);
  }

  protected viewSection(section: GeneratedSection | StreamSection): void {
    if (!section.content) return;
    this.selectedSection.set({
      order: section.order,
      title: section.title,
      content: section.content,
      sources: section.sources ?? []
    });
    this.logFeedbackQuiet('Edit', section.title);
  }

  protected discard(): void {
    this.abort?.abort();
    this.logFeedbackQuiet('Discard');
    this.result.set(null);
    this.savedRfp.set(null);
    this.streamSections.set([]);
    this.selectedSection.set(null);
    this.streaming.set(false);
    this.generating.set(false);
    this.progressLabel.set('');
  }

  protected refreshHistory(): void {
    this.api.listRfpDocuments().subscribe({
      next: rows => this.history.set(rows),
      error: err => console.error(err)
    });
  }

  protected openHistory(item: RfpDocumentListItem): void {
    this.api.getRfpDocument(item.id).subscribe({
      next: row => this.historyDetail.set(row),
      error: err => {
        console.error(err);
        this.toast.show('Could not load RFP history item.');
      }
    });
  }

  protected exportHistory(item: RfpDocumentListItem): void {
    this.exporting.set(true);
    this.api.exportRfpDocument(item.id, this.teams.user().objectId, 'docx').subscribe({
      next: result => {
        void this.api.downloadExport(result)
          .then(() => {
            this.exporting.set(false);
            this.toast.show(`Downloaded ${result.fileName}`);
            this.refreshHistory();
          })
          .catch(err => {
            console.error(err);
            this.exporting.set(false);
            this.toast.show('Download failed after export.');
          });
      },
      error: err => {
        console.error(err);
        this.exporting.set(false);
        this.toast.show(err?.error?.detail ?? err?.error?.title ?? 'Export failed.');
      }
    });
  }

  protected channelHistory(item: RfpDocumentListItem): void {
    this.uploadChannel(item.id);
  }

  protected closeHistoryDetail(): void {
    this.historyDetail.set(null);
  }

  private uploadChannel(id: string): void {
    this.channelBusy.set(true);
    this.api.saveRfpToChannel(id, this.teams.user().objectId).subscribe({
      next: row => {
        this.channelBusy.set(false);
        this.savedRfp.set(row);
        this.refreshHistory();
        if (row.channelUploadStatus === 'Uploaded') {
          this.toast.show('Uploaded to BD channel RFP folder.');
        } else if (row.channelUploadStatus === 'SkippedNoGraph') {
          this.toast.show('Saved in DB. Channel upload waits for Graph / BdChannelDriveId.');
        } else {
          this.toast.show(row.channelUploadError ?? 'Channel upload status updated.');
        }
      },
      error: err => {
        console.error(err);
        this.channelBusy.set(false);
        this.toast.show('Save to BD channel failed.');
      }
    });
  }

  private persistToDatabase(doc: GeneratedDocument, showToast = false): void {
    this.saving.set(true);
    this.api
      .saveRfpDocument({
        document: doc,
        userObjectId: this.teams.user().objectId,
        displayName: this.teams.user().displayName,
        customer: this.customer(),
        tone: this.tone()
      })
      .subscribe({
        next: row => {
          this.savedRfp.set(row);
          this.saving.set(false);
          this.refreshHistory();
          this.linkSavedToPursuit(row.id, row.generationId, row.title);
          if (showToast) this.toast.show('Saved to RFP history database.');
        },
        error: err => {
          console.error(err);
          this.saving.set(false);
          this.toast.show('Could not save RFP to database.');
        }
      });
  }

  private linkSavedToPursuit(rfpId: string, generationId: string, title: string): void {
    const oppId = this.linkedOpportunityId();
    if (!oppId) return;
    this.api.linkOpportunityDocument({
      opportunityId: oppId,
      rfpDocumentId: rfpId,
      generationId,
      documentType: 'Rfp',
      title
    }).subscribe({
      next: () => this.toast.show('Linked draft to pursuit.'),
      error: err => console.error(err)
    });
  }

  private async runLiveStream(signal: AbortSignal): Promise<void> {
    try {
      const stream = this.api.generateRfpStream(
        {
          title: this.title(),
          customer: this.customer(),
          reuseScope: this.reuseScope(),
          tone: this.tone(),
          userObjectId: this.teams.user().objectId,
          corpusSource: this.corpusSource(),
          focusNotes: this.focusNotes() ?? undefined,
          opportunityId: this.linkedOpportunityId() ?? undefined,
          complianceRegion: this.complianceRegion()
        },
        signal
      );

      for await (const evt of stream) {
        if (signal.aborted) return;
        this.applyStreamEvent(evt);
      }
    } catch (err) {
      if (signal.aborted) return;
      console.error(err);
      this.errorMessage.set(
        'Could not stream from BD Copilot API. Is the API running, and was it restarted after the stream endpoint was added?'
      );
      this.progressLabel.set('');
      this.generating.set(false);
      this.streaming.set(false);
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
        this.progressLabel.set(
          `Ollama writing ${String(order).padStart(2, '0')} · ${evt.title ?? ''}`
        );
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
        this.patchByOrder(order, s => ({
          ...s,
          status: 'generating',
          content: s.content + delta
        }));
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
          this.persistToDatabase(evt.document);
          this.api.logGovernanceAudit({
            generationId: evt.document.generationId,
            userObjectId: this.teams.user().objectId,
            userDisplayName: this.teams.user().displayName,
            eventType: 'Generate',
            resourceType: 'Generation',
            resourceId: evt.document.generationId,
            outcome: 'Success',
            detail: `Generated RFP draft '${evt.document.title}'.`
          }).subscribe({ error: err => console.error(err) });
          this.api.saveGenerationSnapshot({
            generationId: evt.document.generationId,
            userObjectId: this.teams.user().objectId,
            displayName: this.teams.user().displayName,
            document: evt.document
          }).subscribe({
            next: s => this.snapshotCount.set(s.versionNumber),
            error: err => console.error(err)
          });
        }
        this.generating.set(false);
        this.streaming.set(false);
        this.progressLabel.set('Draft ready for review');
        this.toast.show(`Generated ${evt.document?.sections.length ?? 0} sections.`);
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
    this.streamSections.update(list =>
      list.map(s => (s.order === order ? fn(s) : s))
    );
  }

  private exportCurrent(format: 'docx' | 'pptx' | 'zip'): void {
    const doc = this.result();
    if (!doc) return;

    this.exporting.set(true);
    this.api
      .exportGeneration({
        generationId: doc.generationId,
        userObjectId: this.teams.user().objectId,
        format,
        document: { ...doc, status: 'Approved' },
        requireApproved: false
      })
      .subscribe({
        next: result => {
          void this.api.downloadExport(result)
            .then(() => {
              this.exporting.set(false);
              this.result.update(d => (d ? { ...d, status: 'Approved' } : d));
              this.toast.show(`Downloaded ${result.fileName}`);
            })
            .catch(err => {
              console.error(err);
              this.exporting.set(false);
              // Fallback: open URL directly if blob fetch fails (e.g. CORS edge case)
              window.location.href = this.api.resolveDownloadUrl(result.downloadUrl);
              this.toast.show('Opening download…');
            });
        },
        error: err => {
          console.error(err);
          this.exporting.set(false);
          this.toast.show(err?.error?.detail ?? err?.error?.title ?? 'Export failed.');
        }
      });
  }

  private logFeedbackQuiet(action: string, sectionTitle?: string): void {
    const doc = this.result();
    if (!doc) return;

    this.api
      .logFeedback({
        generationId: doc.generationId,
        userObjectId: this.teams.user().objectId,
        action,
        sectionTitle
      })
      .subscribe({ error: err => console.error(err) });
  }
}
