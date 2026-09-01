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

export interface PlannerHealthSummary {
  totalTasks: number;
  completed: number;
  inProgress: number;
  notStarted: number;
  delayed: number;
  completionPercent: number;
  riskLevel: string;
  statusMessage: string;
  lastSyncAt?: string | null;
  planCount: number;
}

export interface PlannerTaskListItem {
  id: string;
  graphTaskId: string;
  title: string;
  planTitle?: string | null;
  bucketName?: string | null;
  startDate?: string | null;
  dueDate?: string | null;
  percentComplete: number;
  status: string;
  assignedUsers?: string | null;
  isDelayed: boolean;
}

export interface PlannerSyncResult {
  plansUpserted: number;
  bucketsUpserted: number;
  tasksUpserted: number;
  usedDemoSeed: boolean;
  statusMessage: string;
  errors: string[];
}

export interface PlannerWorkloadRow {
  assignee: string;
  totalTasks: number;
  completed: number;
  inProgress: number;
  delayed: number;
  avgPercentComplete: number;
}

export interface DelayPredictionItem {
  taskId: string;
  title: string;
  assignee?: string | null;
  bucketName?: string | null;
  dueDate?: string | null;
  percentComplete: number;
  isAlreadyDelayed: boolean;
  predictedSlipDays: number;
  riskLevel: string;
  rationale: string;
}

export interface ModuleAtRiskItem {
  moduleName: string;
  taskCount: number;
  delayedCount: number;
  incompleteCount: number;
  avgPercentComplete: number;
  riskLevel: string;
  recommendation: string;
}

export interface StaffingRecommendation {
  focus: string;
  priority: string;
  detail: string;
}

export interface ProjectManagerInsight {
  healthScore: number;
  riskLevel: string;
  summary: string;
  generatedAt: string;
  delayPredictions: DelayPredictionItem[];
  modulesAtRisk: ModuleAtRiskItem[];
  staffingRecommendations: StaffingRecommendation[];
}

export interface StakeholderWeeklyReport {
  title: string;
  markdownBody: string;
  healthScore: number;
  riskLevel: string;
  generatedAt: string;
  usedLlmNarrative: boolean;
  aiProvider?: string | null;
  model?: string | null;
}

export interface PlannerBurndownPoint {
  date: string;
  totalTasks: number;
  completed: number;
  inProgress: number;
  notStarted: number;
  delayed: number;
  completionPercent: number;
  healthScore: number;
}

export interface UnifiedRelatedDocument {
  fileName: string;
  locator?: string | null;
  excerpt?: string | null;
  score: number;
  matchedTaskTitle?: string | null;
}

export interface UnifiedIntelligenceRequest {
  query?: string | null;
  userObjectId: string;
  includeDelayedTasks?: boolean;
  includeSharePoint?: boolean;
  useLlmSummary?: boolean;
}

export interface UnifiedIntelligenceResponse {
  query?: string | null;
  healthScore: number;
  riskLevel: string;
  managementSummary: string;
  delayedTasks: PlannerTaskListItem[];
  relatedDocuments: UnifiedRelatedDocument[];
  staffingRecommendations: StaffingRecommendation[];
  usedLlmSummary: boolean;
  aiProvider?: string | null;
  model?: string | null;
  generatedAt: string;
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
