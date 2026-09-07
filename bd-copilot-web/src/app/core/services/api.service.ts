import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AccessAuditRecord,
  AiProviderStatus,
  ApproveGenerationRequest,
  BusinessCaseGenerationRequest,
  ChatRequest,
  ChatResponse,
  DocumentListItem,
  ExportGenerationRequest,
  ExportResult,
  GeneratedDocument,
  GeneratedDocumentHistory,
  GeneratedDocumentHistoryListItem,
  GenerationFeedbackRequest,
  LocalDocsSyncResult,
  KnowledgeSearchRequest,
  KnowledgeSearchResponse,
  PlannerHealthSummary,
  PlannerSyncResult,
  PlannerTaskListItem,
  PlannerWorkloadRow,
  ProjectManagerInsight,
  ProposalGenerationRequest,
  ReindexRequest,
  RfpDocument,
  RfpDocumentListItem,
  RfpGenerationRequest,
  StakeholderWeeklyReport,
  UnifiedIntelligenceRequest,
  UnifiedIntelligenceResponse,
  PlannerBurndownPoint,
  PlannerCapacityHeatmap,
  PlannerStalledAlert,
  DeliveryCrossLinkResponse,
  CrossLinkFeedbackRequest,
  CrossLinkFeedbackResult,
  CapacityImportResult,
  PartnerOnboardingProfile,
  CompliancePackSettings,
  Opportunity,
  CreateOpportunityRequest,
  TagWinLossRequest,
  MultiApprovalStatus,
  CompetitivePositioningRequest,
  DynamicsDealContext,
  RoiDashboardSummary,
  CustomerUsageSummary,
  RfpStreamEvent,
  AdminLoginResponse,
  AdminTelemetrySummary,
  AuthConfig,
  DashboardSummary,
  TenantCutoverStatus,
  SearchResultItem,
  SyncHealthStatus,
  TeamTokenCostRow,
  CorpusSource,
  ComplianceChecklistItem,
  ExportComplianceChecklist,
  ComplianceChecklistResult,
  GenerationSnapshot,
  GenerationVersionDiff,
  GovernanceAuditEvent,
  PipelineCapacityView
} from '../models/api-models';

/**
 * Thin wrapper over BDCopilot.Api. Every call site elsewhere in the app goes through this
 * service rather than injecting HttpClient directly, so the base URL, error shape and
 * (eventually) auth header all live in exactly one place.
 */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiBaseUrl;

  chat(request: ChatRequest): Observable<ChatResponse> {
    return this.http.post<ChatResponse>(`${this.baseUrl}/chat`, request);
  }

  generateRfp(request: RfpGenerationRequest): Observable<GeneratedDocument> {
    return this.http.post<GeneratedDocument>(`${this.baseUrl}/generate/rfp`, request);
  }

  /**
   * Live RFP generation via SSE. Tokens arrive as Ollama writes each section —
   * not after the full document is finished.
   */
  async *generateRfpStream(
    request: RfpGenerationRequest,
    signal?: AbortSignal
  ): AsyncGenerator<RfpStreamEvent, void, unknown> {
    yield* this.readSseStream(
      `${this.baseUrl}/generate/rfp/stream`,
      request,
      signal,
      'RFP stream'
    );
  }

  generateBusinessCase(request: BusinessCaseGenerationRequest): Observable<GeneratedDocument> {
    return this.http.post<GeneratedDocument>(`${this.baseUrl}/generate/business-case`, request);
  }

  /**
   * Live business-case generation via SSE (same event shape as RFP stream).
   */
  async *generateBusinessCaseStream(
    request: BusinessCaseGenerationRequest,
    signal?: AbortSignal
  ): AsyncGenerator<RfpStreamEvent, void, unknown> {
    yield* this.readSseStream(
      `${this.baseUrl}/generate/business-case/stream`,
      request,
      signal,
      'Business case stream'
    );
  }

  generateProposal(request: ProposalGenerationRequest): Observable<GeneratedDocument> {
    return this.http.post<GeneratedDocument>(`${this.baseUrl}/generate/proposal`, request);
  }

  /**
   * Live proposal generation via SSE (same event shape as RFP stream).
   */
  async *generateProposalStream(
    request: ProposalGenerationRequest,
    signal?: AbortSignal
  ): AsyncGenerator<RfpStreamEvent, void, unknown> {
    yield* this.readSseStream(
      `${this.baseUrl}/generate/proposal/stream`,
      request,
      signal,
      'Proposal stream'
    );
  }

  private async *readSseStream(
    url: string,
    body: unknown,
    signal: AbortSignal | undefined,
    label: string
  ): AsyncGenerator<RfpStreamEvent, void, unknown> {
    const response = await fetch(url, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Accept: 'text/event-stream'
      },
      body: JSON.stringify(body),
      signal
    });

    if (!response.ok || !response.body) {
      throw new Error(`${label} failed (HTTP ${response.status})`);
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      const parts = buffer.split('\n\n');
      buffer = parts.pop() ?? '';

      for (const part of parts) {
        const dataLine = part
          .split('\n')
          .find(line => line.startsWith('data: '));
        if (!dataLine) continue;

        const json = dataLine.slice(6).trim();
        if (!json) continue;

        yield JSON.parse(json) as RfpStreamEvent;
      }
    }
  }

  saveDocumentHistory(
    documentType: string,
    body: {
      documentType?: string;
      document: GeneratedDocument;
      userObjectId: string;
      displayName?: string;
      metadata?: Record<string, string>;
    }
  ): Observable<GeneratedDocumentHistory> {
    return this.http.post<GeneratedDocumentHistory>(
      `${this.baseUrl}/history/${documentType}/save`,
      { ...body, documentType }
    );
  }

  listDocumentHistory(
    documentType: string,
    userObjectId?: string
  ): Observable<GeneratedDocumentHistoryListItem[]> {
    const params = userObjectId ? { userObjectId } : undefined;
    return this.http.get<GeneratedDocumentHistoryListItem[]>(
      `${this.baseUrl}/history/${documentType}`,
      { params }
    );
  }

  getDocumentHistory(documentType: string, id: string): Observable<GeneratedDocumentHistory> {
    return this.http.get<GeneratedDocumentHistory>(`${this.baseUrl}/history/${documentType}/${id}`);
  }

  exportDocumentHistory(
    documentType: string,
    id: string,
    userObjectId: string,
    format = 'docx'
  ): Observable<ExportResult> {
    return this.http.post<ExportResult>(
      `${this.baseUrl}/history/${documentType}/${id}/export`,
      {},
      { params: { userObjectId, format } }
    );
  }

  saveDocumentHistoryToChannel(
    documentType: string,
    id: string,
    userObjectId: string
  ): Observable<GeneratedDocumentHistory> {
    return this.http.post<GeneratedDocumentHistory>(
      `${this.baseUrl}/history/${documentType}/${id}/save-to-channel`,
      { historyId: id, userObjectId }
    );
  }

  search(
    query: string,
    userObjectId: string,
    topK = 8,
    corpusSource: CorpusSource = 'Online'
  ): Observable<SearchResultItem[]> {
    const params = { q: query, userObjectId, topK: String(topK), corpusSource };
    return this.http.get<SearchResultItem[]>(`${this.baseUrl}/search`, { params });
  }

  searchAsk(request: KnowledgeSearchRequest): Observable<KnowledgeSearchResponse> {
    return this.http.post<KnowledgeSearchResponse>(`${this.baseUrl}/search/ask`, request);
  }

  listDocuments(
    userObjectId: string,
    corpusSource: CorpusSource | 'All' = 'All'
  ): Observable<DocumentListItem[]> {
    return this.http.get<DocumentListItem[]>(`${this.baseUrl}/documents`, {
      params: { userObjectId, corpusSource }
    });
  }

  /**
   * URL that opens a cited document in a new tab:
   * Local → API streams the file; Online → API redirects to SharePoint.
   */
  documentOpenUrl(documentId: string, userObjectId: string): string {
    const params = new URLSearchParams({ userObjectId });
    return `${this.baseUrl}/documents/${documentId}/open?${params.toString()}`;
  }

  /** Open SharePoint or local indexed file in a new browser tab/window. */
  openDocument(documentId: string, userObjectId: string): void {
    const url = this.documentOpenUrl(documentId, userObjectId);
    window.open(url, '_blank', 'noopener,noreferrer');
  }

  getAiProviderStatus(): Observable<AiProviderStatus> {
    return this.http.get<AiProviderStatus>(`${this.baseUrl}/settings/ai-provider`);
  }

  getSyncHealth(): Observable<SyncHealthStatus> {
    return this.http.get<SyncHealthStatus>(`${this.baseUrl}/sync/health`);
  }

  seedDocuments(): Observable<SyncHealthStatus> {
    return this.http.post<SyncHealthStatus>(`${this.baseUrl}/sync/seed`, {});
  }

  runSync(): Observable<SyncHealthStatus> {
    return this.http.post<SyncHealthStatus>(`${this.baseUrl}/sync/run`, {});
  }

  syncLocalDocs(): Observable<LocalDocsSyncResult> {
    return this.http.post<LocalDocsSyncResult>(`${this.baseUrl}/sync/local-docs`, {});
  }

  getPlannerHealth(): Observable<PlannerHealthSummary> {
    return this.http.get<PlannerHealthSummary>(`${this.baseUrl}/planner/health`);
  }

  listPlannerTasks(opts?: {
    planId?: string;
    delayedOnly?: boolean;
    assignee?: string;
    dueFrom?: string;
    dueTo?: string;
  }): Observable<PlannerTaskListItem[]> {
    const params: Record<string, string> = {};
    if (opts?.planId) params['planId'] = opts.planId;
    if (opts?.delayedOnly) params['delayedOnly'] = 'true';
    if (opts?.assignee) params['assignee'] = opts.assignee;
    if (opts?.dueFrom) params['dueFrom'] = opts.dueFrom;
    if (opts?.dueTo) params['dueTo'] = opts.dueTo;
    return this.http.get<PlannerTaskListItem[]>(`${this.baseUrl}/planner/tasks`, { params });
  }

  getPlannerWorkload(): Observable<PlannerWorkloadRow[]> {
    return this.http.get<PlannerWorkloadRow[]>(`${this.baseUrl}/planner/workload`);
  }

  getPlannerInsights(): Observable<ProjectManagerInsight> {
    return this.http.get<ProjectManagerInsight>(`${this.baseUrl}/planner/insights`);
  }

  getPlannerStakeholderReport(useLlm = true): Observable<StakeholderWeeklyReport> {
    return this.http.get<StakeholderWeeklyReport>(`${this.baseUrl}/planner/report`, {
      params: { useLlm: String(useLlm) }
    });
  }

  plannerReportDocxUrl(useLlm = true): string {
    return `${this.baseUrl}/planner/report.docx?useLlm=${useLlm}`;
  }

  getPlannerBurndown(days = 30): Observable<PlannerBurndownPoint[]> {
    return this.http.get<PlannerBurndownPoint[]>(`${this.baseUrl}/planner/burndown`, {
      params: { days: String(days) }
    });
  }

  queryUnifiedIntelligence(body: UnifiedIntelligenceRequest): Observable<UnifiedIntelligenceResponse> {
    return this.http.post<UnifiedIntelligenceResponse>(`${this.baseUrl}/planner/unified`, body);
  }

  syncPlanner(): Observable<PlannerSyncResult> {
    return this.http.post<PlannerSyncResult>(`${this.baseUrl}/planner/sync`, {});
  }

  getPlannerCapacity(weeks = 6): Observable<PlannerCapacityHeatmap> {
    return this.http.get<PlannerCapacityHeatmap>(`${this.baseUrl}/planner/capacity`, {
      params: { weeks: weeks.toString() }
    });
  }

  getPlannerStalledAlerts(days = 7): Observable<PlannerStalledAlert[]> {
    return this.http.get<PlannerStalledAlert[]>(`${this.baseUrl}/planner/alerts/stalled`, {
      params: { days: days.toString() }
    });
  }

  getDeliveryCrossLinks(userObjectId: string, maxTasks = 8, refresh = false): Observable<DeliveryCrossLinkResponse> {
    return this.http.get<DeliveryCrossLinkResponse>(`${this.baseUrl}/planner/cross-links`, {
      params: { userObjectId, maxTasks: maxTasks.toString(), refresh: String(refresh) }
    });
  }

  submitCrossLinkFeedback(body: CrossLinkFeedbackRequest): Observable<CrossLinkFeedbackResult> {
    return this.http.post<CrossLinkFeedbackResult>(`${this.baseUrl}/planner/cross-links/feedback`, body);
  }

  importCapacityCsv(file: File): Observable<CapacityImportResult> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<CapacityImportResult>(`${this.baseUrl}/admin/capacity/import`, form);
  }

  getOnboardingProfile(): Observable<PartnerOnboardingProfile> {
    return this.http.get<PartnerOnboardingProfile>(`${this.baseUrl}/admin/onboarding`);
  }

  saveOnboardingProfile(profile: PartnerOnboardingProfile): Observable<PartnerOnboardingProfile> {
    return this.http.post<PartnerOnboardingProfile>(`${this.baseUrl}/admin/onboarding`, profile);
  }

  downloadTeamsManifest(): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/admin/onboarding/teams-manifest`, { responseType: 'blob' });
  }

  getCompliancePacks(): Observable<CompliancePackSettings> {
    return this.http.get<CompliancePackSettings>(`${this.baseUrl}/generate/compliance-packs`);
  }

  listOpportunities(): Observable<Opportunity[]> {
    return this.http.get<Opportunity[]>(`${this.baseUrl}/opportunities`);
  }

  createOpportunity(body: CreateOpportunityRequest): Observable<Opportunity> {
    return this.http.post<Opportunity>(`${this.baseUrl}/opportunities`, body);
  }

  updateOpportunity(id: string, body: Partial<CreateOpportunityRequest> & { outcome?: string; outcomeNotes?: string }): Observable<Opportunity> {
    return this.http.put<Opportunity>(`${this.baseUrl}/opportunities/${id}`, body);
  }

  linkOpportunityDocument(body: {
    opportunityId: string;
    generationId?: string;
    rfpDocumentId?: string;
    documentType?: string;
    title?: string;
  }): Observable<unknown> {
    return this.http.post(`${this.baseUrl}/opportunities/link-document`, body);
  }

  tagWinLoss(body: TagWinLossRequest): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/opportunities/win-loss`, body);
  }

  startMultiApproval(body: { generationId: string; documentTitle: string; userObjectId: string }): Observable<MultiApprovalStatus> {
    return this.http.post<MultiApprovalStatus>(`${this.baseUrl}/generate/approvals/start`, body);
  }

  decideApproval(body: {
    generationId: string;
    role: string;
    status: string;
    userObjectId: string;
    displayName?: string;
    notes?: string;
  }): Observable<MultiApprovalStatus> {
    return this.http.post<MultiApprovalStatus>(`${this.baseUrl}/generate/approvals/decide`, body);
  }

  getMultiApproval(generationId: string): Observable<MultiApprovalStatus> {
    return this.http.get<MultiApprovalStatus>(`${this.baseUrl}/generate/approvals/${generationId}`);
  }

  generateCompetitive(request: CompetitivePositioningRequest): Observable<GeneratedDocument> {
    return this.http.post<GeneratedDocument>(`${this.baseUrl}/generate/competitive`, request);
  }

  async *generateCompetitiveStream(
    request: CompetitivePositioningRequest,
    signal?: AbortSignal
  ): AsyncGenerator<RfpStreamEvent, void, unknown> {
    yield* this.readSseStream(`${this.baseUrl}/generate/competitive/stream`, request, signal, 'Competitive stream');
  }

  listDynamicsDeals(): Observable<DynamicsDealContext[]> {
    return this.http.get<DynamicsDealContext[]>(`${this.baseUrl}/dynamics/deals`);
  }

  getDynamicsDealContext(opportunityId?: string, client?: string): Observable<DynamicsDealContext> {
    const params: Record<string, string> = {};
    if (opportunityId) params['opportunityId'] = opportunityId;
    if (client) params['client'] = client;
    return this.http.get<DynamicsDealContext>(`${this.baseUrl}/dynamics/deal-context`, { params });
  }

  getRoiDashboard(): Observable<RoiDashboardSummary> {
    return this.http.get<RoiDashboardSummary>(`${this.baseUrl}/analytics/roi`);
  }

  getCustomerUsage(): Observable<CustomerUsageSummary> {
    return this.http.get<CustomerUsageSummary>(`${this.baseUrl}/analytics/usage`);
  }

  approveGeneration(body: ApproveGenerationRequest): Observable<GeneratedDocument> {
    return this.http.post<GeneratedDocument>(`${this.baseUrl}/generate/approve`, body);
  }

  logFeedback(body: GenerationFeedbackRequest): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/generate/feedback`, body);
  }

  exportGeneration(body: ExportGenerationRequest): Observable<ExportResult> {
    return this.http.post<ExportResult>(`${this.baseUrl}/generate/export`, body);
  }

  getComplianceChecklist(generationId: string, documentTitle?: string): Observable<ExportComplianceChecklist> {
    const params = documentTitle ? { documentTitle } : undefined;
    return this.http.get<ExportComplianceChecklist>(`${this.baseUrl}/governance/checklist/${generationId}`, { params });
  }

  submitComplianceChecklist(body: {
    generationId: string;
    userObjectId: string;
    items: ComplianceChecklistItem[];
  }): Observable<ComplianceChecklistResult> {
    return this.http.post<ComplianceChecklistResult>(`${this.baseUrl}/governance/checklist`, body);
  }

  saveGenerationSnapshot(body: {
    generationId: string;
    userObjectId: string;
    displayName?: string;
    document: GeneratedDocument;
  }): Observable<GenerationSnapshot> {
    return this.http.post<GenerationSnapshot>(`${this.baseUrl}/governance/snapshots`, body);
  }

  listGenerationSnapshots(generationId: string): Observable<GenerationSnapshot[]> {
    return this.http.get<GenerationSnapshot[]>(`${this.baseUrl}/governance/snapshots/${generationId}`);
  }

  diffGenerationVersions(generationId: string, fromVersion?: number, toVersion?: number): Observable<GenerationVersionDiff> {
    const params: Record<string, string> = {};
    if (fromVersion != null) params['fromVersion'] = String(fromVersion);
    if (toVersion != null) params['toVersion'] = String(toVersion);
    return this.http.get<GenerationVersionDiff>(`${this.baseUrl}/governance/diff/${generationId}`, { params });
  }

  logGovernanceAudit(evt: GovernanceAuditEvent): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/governance/audit`, evt);
  }

  getPipelineCapacity(): Observable<PipelineCapacityView> {
    return this.http.get<PipelineCapacityView>(`${this.baseUrl}/planner/pipeline-capacity`);
  }

  /** Resolve relative local-export paths like /api/generate/download/... against the API host. */
  resolveDownloadUrl(url: string): string {
    if (/^https?:\/\//i.test(url)) return url;
    const origin = this.baseUrl.replace(/\/api\/?$/, '');
    return `${origin}${url.startsWith('/') ? url : '/' + url}`;
  }

  /** Fetch the export URL and trigger a browser file download (avoids popup blockers). */
  async downloadExport(result: ExportResult): Promise<void> {
    const url = this.resolveDownloadUrl(result.downloadUrl);
    const response = await fetch(url);
    if (!response.ok) {
      throw new Error(`Download failed (HTTP ${response.status})`);
    }
    const blob = await response.blob();
    const objectUrl = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = objectUrl;
    anchor.download = result.fileName || 'export.docx';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(objectUrl);
  }

  saveRfpDocument(body: {
    document: GeneratedDocument;
    userObjectId: string;
    displayName?: string;
    customer?: string;
    tone?: string;
  }): Observable<RfpDocument> {
    return this.http.post<RfpDocument>(`${this.baseUrl}/rfp/save`, body);
  }

  listRfpDocuments(userObjectId?: string): Observable<RfpDocumentListItem[]> {
    const params = userObjectId ? { userObjectId } : undefined;
    return this.http.get<RfpDocumentListItem[]>(`${this.baseUrl}/rfp`, { params });
  }

  getRfpDocument(id: string): Observable<RfpDocument> {
    return this.http.get<RfpDocument>(`${this.baseUrl}/rfp/${id}`);
  }

  exportRfpDocument(id: string, userObjectId: string, format = 'docx'): Observable<ExportResult> {
    return this.http.post<ExportResult>(`${this.baseUrl}/rfp/${id}/export`, {}, {
      params: { userObjectId, format }
    });
  }

  saveRfpToChannel(id: string, userObjectId: string): Observable<RfpDocument> {
    return this.http.post<RfpDocument>(`${this.baseUrl}/rfp/${id}/save-to-channel`, {
      rfpDocumentId: id,
      userObjectId
    });
  }

  adminLogin(username: string, password: string): Observable<AdminLoginResponse> {
    return this.http.post<AdminLoginResponse>(`${this.baseUrl}/auth/login`, { username, password });
  }

  getAuthConfig(): Observable<AuthConfig> {
    return this.http.get<AuthConfig>(`${this.baseUrl}/auth/config`);
  }

  getDashboardSummary(): Observable<DashboardSummary> {
    return this.http.get<DashboardSummary>(`${this.baseUrl}/dashboard/summary`);
  }

  getAdminTelemetry(): Observable<AdminTelemetrySummary> {
    return this.http.get<AdminTelemetrySummary>(`${this.baseUrl}/admin/telemetry`);
  }

  getCutoverStatus(): Observable<TenantCutoverStatus> {
    return this.http.get<TenantCutoverStatus>(`${this.baseUrl}/admin/cutover-status`);
  }

  getTokenCosts(): Observable<TeamTokenCostRow[]> {
    return this.http.get<TeamTokenCostRow[]>(`${this.baseUrl}/admin/token-costs`);
  }

  getAccessAudits(limit = 50): Observable<AccessAuditRecord[]> {
    return this.http.get<AccessAuditRecord[]>(`${this.baseUrl}/admin/access-audits`, {
      params: { limit: String(limit) }
    });
  }

  reindex(body?: ReindexRequest): Observable<SyncHealthStatus> {
    return this.http.post<SyncHealthStatus>(`${this.baseUrl}/admin/reindex`, body ?? {});
  }

  runGoldenEval(userObjectId?: string): Observable<unknown> {
    const params = userObjectId ? { userObjectId } : undefined;
    return this.http.post(`${this.baseUrl}/eval/golden`, {}, { params });
  }
}
