// Mirrors BDCopilot.Core.Models on the backend. Kept as one file, deliberately dumb (no
// classes/behaviour) — these are wire DTOs, not view state.

export interface Citation {
  documentId: string;
  fileName: string;
  locator?: string | null;
  sharePointUrl: string;
  snippet?: string | null;
}

export interface ChatTurn {
  role: 'user' | 'assistant';
  content: string;
}

export interface ChatRequest {
  message: string;
  history: ChatTurn[];
  userObjectId: string;
}

export interface ChatResponse {
  answer: string;
  citations: Citation[];
  aiProvider: string;
  model: string;
}

export interface GeneratedSection {
  order: number;
  title: string;
  content: string;
  sources: Citation[];
}

export interface GeneratedDocument {
  generationId: string;
  title: string;
  sections: GeneratedSection[];
  status: string;
  createdAt: string;
}

export type CorpusSource = 'Local' | 'Online' | 'All';

export interface RfpGenerationRequest {
  title: string;
  customer: string;
  reuseScope: 'All' | 'Last12Months';
  tone: string;
  userObjectId: string;
  corpusSource?: CorpusSource;
  focusNotes?: string;
}

/** Live SSE payload from POST /api/generate/rfp/stream */
export interface RfpStreamEvent {
  type: 'outline' | 'section-start' | 'token' | 'section-done' | 'complete' | 'error';
  generationId?: string;
  documentTitle?: string;
  order?: number;
  title?: string;
  delta?: string;
  content?: string;
  sources?: Citation[];
  sectionTitles?: string[];
  document?: GeneratedDocument;
  message?: string;
}

export interface RfpDocument {
  id: string;
  generationId: string;
  title: string;
  customer: string;
  tone: string;
  status: string;
  createdByUserObjectId: string;
  createdByDisplayName?: string | null;
  createdAt: string;
  updatedAt?: string | null;
  executiveSummary?: string | null;
  understandingOfRequirements?: string | null;
  proposedSolutionArchitecture?: string | null;
  securityCompliance?: string | null;
  deliveryTimelineTeam?: string | null;
  commercialsPricing?: string | null;
  channelSharePointUrl?: string | null;
  channelUploadStatus?: string | null;
  channelUploadError?: string | null;
}

export interface RfpDocumentListItem {
  id: string;
  generationId: string;
  title: string;
  customer: string;
  status: string;
  createdByUserObjectId: string;
  createdByDisplayName?: string | null;
  createdAt: string;
  channelUploadStatus?: string | null;
  channelSharePointUrl?: string | null;
}

/** Persisted business-case / proposal (and generic) generation history row. */
export interface GeneratedDocumentHistory {
  id: string;
  generationId: string;
  documentType: string;
  title: string;
  status: string;
  metadataJson?: string | null;
  sectionsJson?: string | null;
  /** Present when the API deserializes sections server-side. */
  sections?: GeneratedSection[] | null;
  createdByUserObjectId: string;
  createdByDisplayName?: string | null;
  createdAt: string;
  updatedAt?: string | null;
  channelSharePointUrl?: string | null;
  channelUploadStatus?: string | null;
  channelUploadError?: string | null;
}

export interface GeneratedDocumentHistoryListItem {
  id: string;
  generationId: string;
  documentType: string;
  title: string;
  status: string;
  createdByUserObjectId: string;
  createdByDisplayName?: string | null;
  createdAt: string;
  channelUploadStatus?: string | null;
  channelSharePointUrl?: string | null;
  summaryLabel?: string | null;
}

export interface AdminLoginResponse {
  token: string;
  username: string;
  displayName: string;
  role: string;
  expiresAt: string;
}

export interface BusinessCaseGenerationRequest {
  initiative: string;
  audience: string;
  userObjectId: string;
  corpusSource?: CorpusSource;
  focusNotes?: string;
}

export interface ProposalGenerationRequest {
  solution: string;
  includeDeck: boolean;
  includeArchitectureDiagram: boolean;
  userObjectId: string;
  corpusSource?: CorpusSource;
}

export interface SearchResultItem {
  source: Citation;
  excerpt: string;
  score: number;
}

export interface DocumentListItem {
  documentId: string;
  fileName: string;
  fileType: string;
  teamsChannel: string;
  modifiedDate: string;
  accessibleToCaller: boolean;
  indexStatus: string;
  corpusSource?: CorpusSource;
}

export interface LocalDocsSyncResult {
  rootPath: string;
  rootExists: boolean;
  filesFound: number;
  indexed: number;
  skippedUnchanged: number;
  failed: number;
  errors: string[];
  statusMessage: string;
}

export interface KnowledgeSearchRequest {
  query: string;
  userObjectId: string;
  topK?: number;
  corpusSource?: CorpusSource;
}

export interface KnowledgeSearchResponse {
  query: string;
  answer: string;
  citations: Citation[];
  results: SearchResultItem[];
  aiProvider: string;
  model: string;
}

export interface AiProviderStatus {
  provider: string;
  chatModel: string;
  embeddingModel: string;
}

export interface SyncHealthStatus {
  lastSuccessAt?: string | null;
  lastAttemptAt?: string | null;
  lastError?: string | null;
  documentsIndexed: number;
  sitesConfigured: number;
  isHealthy: boolean;
  statusMessage: string;
  sites: SyncSiteHealthItem[];
}

export interface SyncSiteHealthItem {
  siteId: string;
  siteDisplayName?: string | null;
  lastSuccessAt?: string | null;
  lastError?: string | null;
  documentsIndexed: number;
  hasDeltaLink: boolean;
}

export interface ExportResult {
  fileName: string;
  contentType: string;
  downloadUrl: string;
  status: string;
}

export interface TeamTokenCostRow {
  teamId?: string | null;
  initiative?: string | null;
  totalTokens: number;
  requestCount: number;
}

export interface AccessAuditRecord {
  id: string;
  userObjectId: string;
  documentId: string;
  allowed: boolean;
  reason: string;
  createdAt: string;
}

export interface ApproveGenerationRequest {
  generationId: string;
  userObjectId: string;
  document: GeneratedDocument;
}

export interface GenerationFeedbackRequest {
  generationId: string;
  userObjectId: string;
  action: string;
  sectionTitle?: string | null;
  notes?: string | null;
}

export interface ExportGenerationRequest {
  generationId: string;
  userObjectId: string;
  format: string;
  document: GeneratedDocument;
  requireApproved?: boolean;
}

export interface ReindexRequest {
  siteId?: string | null;
}
