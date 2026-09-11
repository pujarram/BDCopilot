import { Component, OnInit, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map, startWith } from 'rxjs/operators';
import { GeneratePursuitContextService } from '../../core/services/generate-pursuit-context.service';
import { GENERATE_DOC_TYPES } from '../../shared/generate-doc-types';

@Component({
  selector: 'app-generate-workspace',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './generate-workspace.html'
})
export class GenerateWorkspace implements OnInit {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly pursuitCtx = inject(GeneratePursuitContextService);

  protected readonly docTypes = GENERATE_DOC_TYPES;
  protected readonly opportunities = this.pursuitCtx.opportunities;
  protected readonly dynamicsDeals = this.pursuitCtx.dynamicsDeals;
  protected readonly loadingPursuits = this.pursuitCtx.loading;
  protected readonly context = this.pursuitCtx.context;
  protected readonly bannerLabel = this.pursuitCtx.bannerLabel;

  private readonly activePath = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map(() => this.readActiveType(this.router.url)),
      startWith(this.readActiveType(this.router.url))
    ),
    { initialValue: 'rfp' }
  );

  protected readonly activeDescription = computed(() => {
    const type = this.docTypes.find(t => t.path === this.activePath());
    return type?.description ?? this.docTypes[0].description;
  });

  ngOnInit(): void {
    this.pursuitCtx.loadLists();
    this.route.queryParamMap.subscribe(params => {
      const oppId = params.get('opportunityId');
      if (oppId) {
        this.pursuitCtx.selectOpportunityById(oppId);
      }
    });
  }

  protected onPursuitChange(event: Event): void {
    const id = (event.target as HTMLSelectElement).value;
    if (!id) {
      this.pursuitCtx.clear();
      return;
    }
    this.pursuitCtx.selectOpportunityById(id);
  }

  protected applyDynamicsDeal(dealId: string): void {
    const deal = this.dynamicsDeals().find(d => d.opportunityId === dealId);
    if (deal) this.pursuitCtx.selectDynamicsDeal(deal);
  }

  protected clearPursuit(): void {
    this.pursuitCtx.clear();
  }

  private readActiveType(url: string): string {
    const match = url.match(/\/generate\/([^/?#]+)/i);
    const segment = match?.[1]?.toLowerCase() ?? 'rfp';
    return this.docTypes.some(t => t.path === segment) ? segment : 'rfp';
  }
}
