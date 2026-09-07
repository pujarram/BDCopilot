import { CUSTOM_ELEMENTS_SCHEMA, Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { ApiService } from '../../core/services/api.service';
import { TeamsService } from '../../core/services/teams.service';
import { ToastService } from '../../core/services/toast.service';
import { Opportunity, RfpDocumentListItem } from '../../core/models/api-models';

@Component({
  selector: 'app-pursuits',
  templateUrl: './pursuits.html',
  imports: [DatePipe, DecimalPipe],
  schemas: [CUSTOM_ELEMENTS_SCHEMA]
})
export class Pursuits implements OnInit {
  private readonly api = inject(ApiService);
  private readonly teams = inject(TeamsService);
  private readonly toast = inject(ToastService);

  protected readonly opportunities = signal<Opportunity[]>([]);
  protected readonly rfps = signal<RfpDocumentListItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly name = signal('');
  protected readonly client = signal('');
  protected readonly dealSize = signal('');
  protected readonly stage = signal('Propose');
  protected readonly owner = signal('');

  ngOnInit(): void {
    this.reload();
  }

  protected reload(): void {
    this.loading.set(true);
    let pending = 2;
    const done = () => {
      pending -= 1;
      if (pending === 0) this.loading.set(false);
    };
    this.api.listOpportunities().subscribe({
      next: rows => { this.opportunities.set(rows); done(); },
      error: err => { console.error(err); done(); }
    });
    this.api.listRfpDocuments().subscribe({
      next: rows => { this.rfps.set(rows); done(); },
      error: err => { console.error(err); done(); }
    });
  }

  protected create(): void {
    if (!this.name().trim() || !this.client().trim()) {
      this.toast.show('Name and client are required.');
      return;
    }
    const size = parseFloat(this.dealSize());
    this.api.createOpportunity({
      name: this.name().trim(),
      client: this.client().trim(),
      dealSize: Number.isFinite(size) ? size : undefined,
      stage: this.stage(),
      ownerDisplayName: this.owner() || undefined,
      ownerUserObjectId: this.teams.user().objectId
    }).subscribe({
      next: () => {
        this.toast.show('Opportunity created.');
        this.name.set('');
        this.client.set('');
        this.dealSize.set('');
        this.reload();
      },
      error: err => { console.error(err); this.toast.show('Create failed.'); }
    });
  }

  protected tagOutcome(rfpId: string, outcome: 'Win' | 'Loss' | 'Open'): void {
    this.api.tagWinLoss({
      rfpDocumentId: rfpId,
      outcome,
      userObjectId: this.teams.user().objectId
    }).subscribe({
      next: () => {
        this.toast.show(`Tagged ${outcome}.`);
        this.reload();
      },
      error: err => { console.error(err); this.toast.show('Tag failed.'); }
    });
  }

  protected linkRfp(oppId: string, rfp: RfpDocumentListItem): void {
    this.api.linkOpportunityDocument({
      opportunityId: oppId,
      rfpDocumentId: rfp.id,
      generationId: rfp.generationId,
      documentType: 'Rfp',
      title: rfp.title
    }).subscribe({
      next: () => {
        this.toast.show('Linked RFP to opportunity.');
        this.reload();
      },
      error: err => { console.error(err); this.toast.show('Link failed.'); }
    });
  }
}
