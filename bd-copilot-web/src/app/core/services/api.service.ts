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
  ProposalGenerationRequest,
  ReindexRequest,
  RfpDocument,
  RfpDocumentListItem,
  RfpGenerationRequest,
  RfpStreamEvent,
  AdminLoginResponse,
  SearchResultItem,
  SyncHealthStatus,
  TeamTokenCostRow,
  CorpusSource
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

  approveGeneration(body: ApproveGenerationRequest): Observable<GeneratedDocument> {
    return this.http.post<GeneratedDocument>(`${this.baseUrl}/generate/approve`, body);
  }

  logFeedback(body: GenerationFeedbackRequest): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/generate/feedback`, body);
  }

  exportGeneration(body: ExportGenerationRequest): Observable<ExportResult> {
    return this.http.post<ExportResult>(`${this.baseUrl}/generate/export`, body);
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
