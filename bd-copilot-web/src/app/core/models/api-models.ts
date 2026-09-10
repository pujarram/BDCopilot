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
  includeChannelLiveSearch?: boolean;
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

export type CorpusSource = 'Local' | 'Online' | 'Planner' | 'Battlecards' | 'All';

export interface RfpGenerationRequest {
  title: string;
  customer: string;
  reuseScope: 'All' | 'Last12Months';
  tone: string;
  userObjectId: string;
  corpusSource?: CorpusSource;
  focusNotes?: string;
  language?: string;
  complianceRegion?: string;
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
  outcome?: string;
  opportunityId?: string | null;
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
  language?: string;
  complianceRegion?: string;
}

export interface ProposalGenerationRequest {
  solution: string;
  includeDeck: boolean;
  includeArchitectureDiagram: boolean;
  userObjectId: string;
  corpusSource?: CorpusSource;
  language?: string;
  complianceRegion?: string;
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
  lastIndexedAt?: string | null;
  isStale?: boolean;
  monthsSinceModified?: number;
  staleAfterMonths?: number;
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
  estimatedHoursOpen: number;
  estimatedFte: number;
}

export interface PlannerStalledAlert {
  taskId: string;
  graphTaskId: string;
  title: string;
  assignedUsers?: string | null;
  percentComplete: number;
  stalledDays: number;
  lastProgressDate?: string | null;
  message: string;
}

export interface PlannerCapacityCell {
  assignee: string;
  weekStart: string;
  estimatedHours: number;
  fteLoad: number;
  openTasks: number;
  heatLevel: string;
}

export interface PlannerCapacityHeatmap {
  assignees: string[];
  weekStarts: string[];
  cells: PlannerCapacityCell[];
  weeklyCapacityHours: number;
  guidance: string;
}

export interface DeliveryCrossLink {
  linkId?: string | null;
  taskId: string;
  taskTitle: string;
  owner?: string | null;
  isDelayed: boolean;
  documentFileName: string;
  locator?: string | null;
  excerpt?: string | null;
  score: number;
  linkKind: string;
  rationale: string;
  upvotes?: number;
  downvotes?: number;
}

export interface CrossLinkFeedbackRequest {
  linkId: string;
  userObjectId: string;
  action: 'Upvote' | 'Downvote' | 'Dismiss' | 'Pin';
}

export interface CrossLinkFeedbackResult {
  linkId: string;
  upvotes: number;
  downvotes: number;
  adjustedScore: number;
}

export interface DeliveryCrossLinkResponse {
  links: DeliveryCrossLink[];
  delayedTaskCount: number;
  rfpClauseHitCount: number;
  summary: string;
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
  whyExplanation?: string;
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

export interface ScoreFactor {
  factor: string;
  impactPoints: number;
  direction: string;
  detail: string;
}

export interface HealthScoreExplanation {
  healthScore: number;
  riskLevel: string;
  factors: ScoreFactor[];
  narrative: string;
}

export interface ProjectManagerInsight {
  healthScore: number;
  riskLevel: string;
  summary: string;
  generatedAt: string;
  delayPredictions: DelayPredictionItem[];
  modulesAtRisk: ModuleAtRiskItem[];
  staffingRecommendations: StaffingRecommendation[];
  healthExplanation?: HealthScoreExplanation | null;
}

export interface PipelineStaffingGap {
  assignee: string;
  opportunityName: string;
  client: string;
  stage: string;
  deadline?: string | null;
  currentFteLoad: number;
  gapLevel: string;
  rationale: string;
}

export interface PipelineOpportunityRow {
  opportunityId: string;
  name: string;
  client: string;
  stage: string;
  ownerDisplayName?: string | null;
  deadline?: string | null;
  daysToDeadline: number;
  hasStaffingGap: boolean;
}

export interface PipelineCapacityView {
  gaps: PipelineStaffingGap[];
  openPursuits: PipelineOpportunityRow[];
  summary: string;
  guidance: string;
}

export interface ComplianceChecklistItem {
  id: string;
  label: string;
  category: string;
  required: boolean;
  checked: boolean;
  notes?: string | null;
}

export interface ExportComplianceChecklist {
  generationId: string;
  documentTitle: string;
  items: ComplianceChecklistItem[];
  allRequiredComplete: boolean;
  guidance: string;
}

export interface ComplianceChecklistResult {
  generationId: string;
  readyForExport: boolean;
  requiredChecked: number;
  requiredTotal: number;
  message: string;
}

export interface GenerationSnapshot {
  id: string;
  generationId: string;
  versionNumber: number;
  documentTitle: string;
  sectionsJson: string;
  createdByUserObjectId: string;
  createdByDisplayName?: string | null;
  createdAt: string;
  changeSummary?: string | null;
}

export interface SectionDiffItem {
  title: string;
  changeKind: string;
  beforeExcerpt?: string | null;
  afterExcerpt?: string | null;
  linesAdded: number;
  linesRemoved: number;
}

export interface GenerationVersionDiff {
  generationId: string;
  fromVersion: number;
  toVersion: number;
  sections: SectionDiffItem[];
  summary: string;
}

export interface GovernanceAuditEvent {
  id?: string;
  generationId?: string | null;
  userObjectId: string;
  userDisplayName?: string | null;
  eventType: string;
  resourceType: string;
  resourceId?: string | null;
  outcome?: string | null;
  detail?: string | null;
  complianceFramework?: string;
  createdAt?: string;
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
  matchedTaskId?: string | null;
  matchedTaskTitle?: string | null;
  ownerDisplayName?: string | null;
  linkKind?: string;
  rationale?: string | null;
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

export interface DashboardAiProvider {
  provider: string;
  chatModel: string;
  embeddingModel: string;
}

export interface ProductionPosture {
  entraConfigured: boolean;
  enforceAcl: boolean;
  requireAuthOnApi: boolean;
  graphConfigured: boolean;
  plannerLiveConfigured: boolean;
  applicationInsightsConfigured: boolean;
  teamsBotEnabled: boolean;
  aiProvider: string;
}

export interface DashboardQuickLink {
  label: string;
  path: string;
  description: string;
}

export interface DashboardSummary {
  syncHealth?: SyncHealthStatus | null;
  plannerHealth?: PlannerHealthSummary | null;
  projectInsights?: ProjectManagerInsight | null;
  documentsIndexed: number;
  accessDenialsLast24Hours: number;
  tokensLast24Hours: number;
  requestsLast24Hours: number;
  aiProvider: DashboardAiProvider;
  productionPosture: ProductionPosture;
  quickLinks: DashboardQuickLink[];
  generatedAt: string;
}

export interface AdminTelemetrySummary {
  applicationInsightsConfigured: boolean;
  accessDenialsLastHour: number;
  accessDenialsLast24Hours: number;
  tokensLast24Hours: number;
  requestsLast24Hours: number;
  avgLatencyMsLast24Hours: number;
  p95LatencyMsLast24Hours: number;
  topTokenConsumers: TeamTokenCostRow[];
  syncHealth?: SyncHealthStatus | null;
  plannerLive?: PlannerLiveStatus | null;
  guidance: string;
  appInsightsKustoHint?: string;
}

export interface PlannerLiveStatus {
  enabled: boolean;
  seedDemoData: boolean;
  graphConfigured: boolean;
  groupIdCount: number;
  planIdCount: number;
  isLiveConfigured: boolean;
  guidance: string;
}

export interface TenantCutoverItem {
  id: string;
  label: string;
  complete: boolean;
  status: string;
  detail: string;
  action?: string | null;
  goLiveRequired?: boolean;
}

export interface CustomerTenantProfile {
  tenantId?: string | null;
  plannerGroupIds: string[];
  plannerPlanIds: string[];
  pilotSitePath?: string | null;
  pilotSiteId?: string | null;
  sharePointSiteIds: string[];
  syncFolderPaths: string[];
}

export interface TenantCutoverStatus {
  readyForProduction: boolean;
  goLiveReady: boolean;
  completedCount: number;
  totalCount: number;
  pendingItemIds: string[];
  customerCode?: string | null;
  customerDisplayName?: string | null;
  customerProfilePath?: string | null;
  sharePointConfigured: boolean;
  sharePointSiteCount: number;
  keyVaultConfigured: boolean;
  keyVaultUri?: string | null;
  workbookImportPath: string;
  workbookImportScript: string;
  cutoverScript?: string;
  validateScript?: string;
  items: TenantCutoverItem[];
  plannerLive?: PlannerLiveStatus | null;
  tenantProfile?: CustomerTenantProfile | null;
  accessDenialsLast24Hours: number;
  accessAuditsLast24Hours: number;
  aclVerificationGuidance: string;
  goLiveGuidance: string;
}

export interface AuthConfig {
  entraEnabled: boolean;
  tenantId?: string | null;
  clientId?: string | null;
  apiClientId?: string | null;
  apiScope?: string | null;
  authority?: string | null;
  redirectUri?: string | null;
  spaFallsBackToApiClient?: boolean;
  setupHints?: string[];
  enforceAcl: boolean;
  requireAuthOnApi: boolean;
  allowPilotAdminLogin?: boolean;
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

export interface CapacityImportResult {
  rowsProcessed: number;
  overridesUpserted: number;
  taskHoursUpdated: number;
  skipped: number;
  warnings: string[];
  summary: string;
}

export interface PartnerOnboardingProfile {
  code: string;
  displayName: string;
  tenantId: string;
  subscriptionId?: string;
  resourceGroup?: string;
  keyVaultName?: string;
  appServiceName?: string;
  plannerGroupIds: string[];
  pilotSitePath?: string;
  graphAppId?: string;
  apiAppId?: string;
  spaAppId?: string;
  teamsManifestBaseUrl?: string;
  cutoverCompletedCount?: number;
  cutoverTotalCount?: number;
  goLiveReady?: boolean;
}

export interface ComplianceRegionPack {
  label: string;
  promptSuffix: string;
  exportLanguage: string;
}

export interface CompliancePackSettings {
  defaultRegion: string;
  regions: Record<string, ComplianceRegionPack>;
}

export interface OpportunityDocumentLink {
  id: string;
  opportunityId: string;
  generationId?: string | null;
  rfpDocumentId?: string | null;
  historyDocumentId?: string | null;
  documentType: string;
  title?: string | null;
  linkedAt: string;
}

export interface Opportunity {
  id: string;
  name: string;
  client: string;
  dealSize?: number | null;
  stage: string;
  ownerDisplayName?: string | null;
  ownerUserObjectId?: string | null;
  deadline?: string | null;
  outcome: string;
  outcomeNotes?: string | null;
  dynamicsOpportunityId?: string | null;
  notes?: string | null;
  createdAt: string;
  updatedAt?: string | null;
  linkedDocuments: OpportunityDocumentLink[];
}

export interface CreateOpportunityRequest {
  name: string;
  client: string;
  dealSize?: number | null;
  stage?: string;
  ownerDisplayName?: string;
  ownerUserObjectId?: string;
  deadline?: string | null;
  notes?: string;
  dynamicsOpportunityId?: string;
}

export interface TagWinLossRequest {
  rfpDocumentId: string;
  outcome: 'Win' | 'Loss' | 'Open';
  notes?: string;
  opportunityId?: string;
  userObjectId: string;
}

export interface GenerationApproval {
  id: string;
  generationId: string;
  documentTitle: string;
  role: string;
  status: string;
  reviewerUserObjectId?: string | null;
  reviewerDisplayName?: string | null;
  notes?: string | null;
  createdAt: string;
  decidedAt?: string | null;
}

export interface MultiApprovalStatus {
  generationId: string;
  documentTitle: string;
  reviews: GenerationApproval[];
  isFullyApproved: boolean;
  isRejected: boolean;
  overallStatus: string;
}

export interface CompetitivePositioningRequest {
  competitor: string;
  ourSolution: string;
  customerContext?: string;
  userObjectId: string;
  language?: string;
  complianceRegion?: string;
  opportunityId?: string;
  competitorB?: string;
  sourceType?: 'text' | 'transcript' | 'file' | 'corpus';
  rawIntelligence?: string;
  documentIds?: string[];
}

export interface PublishBattleCardResult {
  documentId: string;
  fileName: string;
  title: string;
  corpusSource: string;
  message: string;
}

export interface DynamicsDealContext {
  opportunityId: string;
  name: string;
  client: string;
  estimatedValue?: number | null;
  stage: string;
  owner?: string | null;
  closeDate?: string | null;
  focusNotes: string;
  fromDemoSeed: boolean;
}

export interface MonthlyGenerationPoint {
  month: string;
  count: number;
}

export interface WinRateTrendPoint {
  month: string;
  wins: number;
  losses: number;
  winRatePercent: number;
}

export interface RoiDashboardSummary {
  minutesSavedPerDocument: number;
  hoursSavedTotal: number;
  generationsThisMonth: number;
  generationsLast30Days: number;
  generationsByMonth: MonthlyGenerationPoint[];
  winRatePercent: number;
  wins: number;
  losses: number;
  openOutcomes: number;
  winRateTrend: WinRateTrendPoint[];
  tokensLast30Days: number;
  requestsLast30Days: number;
  guidance: string;
}

export interface OperationUsageRow {
  operation: string;
  requestCount: number;
  totalTokens: number;
}

export interface CustomerUsageSummary {
  tokensLast30Days: number;
  requestsLast30Days: number;
  generationsLast30Days: number;
  estimatedCostUsd: number;
  costPer1kTokensUsd: number;
  topConsumers: TeamTokenCostRow[];
  byOperation: OperationUsageRow[];
  guidance: string;
}
