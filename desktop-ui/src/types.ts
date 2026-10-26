export type Page =
  | "home"
  | "library"
  | "magpie"
  | "wishes"
  | "downloads"
  | "help"
  | "settings"
  | "account"
  | "about";
export type Goal = "quality" | "performance" | "custom";
export interface PresetSetting {
  file: string;
  section: string;
  key: string;
  value: string;
}
export interface PresetAttachmentInfo { name: string; size: number; sha256: string }
export interface CommunityPreset {
  id: string;
  gameKey: string;
  gameName: string;
  mode: number;
  goal: Goal;
  vulkan: boolean;
  componentVersion: string;
  title: string;
  description: string;
  author: string;
  createdAt: number;
  hasImage: boolean;
  isMine: boolean;
  settingCount: number;
  scope?: "game" | "universal";
  resourceUrl?: string | null;
  resourceCode?: string | null;
  attachment?: PresetAttachmentInfo | null;
  thumbnailData?: string | null;
}
export interface CommunityPresetDetail {
  item: CommunityPreset;
  settings: PresetSetting[];
  imageData: string | null;
}
export interface CommunityPresetList {
  items: CommunityPreset[];
  total: number;
  page?: number;
  pageSize?: number;
}
export interface CommunityPresetSnapshot {
  mode: number;
  componentVersion: string;
  vulkan: boolean;
  gameName: string;
  settings: PresetSetting[];
}
export interface Game {
  id: string;
  title: string;
  searchTerms?: string[];
  cover: string | null;
  state: string;
  mode: number | null;
  steamAppId: string | null;
}
export interface Account {
  id: string;
  email: string;
  membership: { tier: string; expiresAt: string | null; kind?: "standard" | "advanced" | "lifetime" | "plus"; plusExpiresAt?: string | null; remoteSessionsRemaining?: number | null };
  features: { key: string; allowed: boolean; enabled?: boolean; minimumTier?: string }[];
  paymentsEnabled: boolean;
}
export interface Download {
  independent?: boolean;
  id: string;
  title: string;
  state: string;
  percent: number;
  size: number;
  detail: string;
  paused: boolean;
}
export interface State {
  magpiePreparing?: boolean;
  version: string;
  page: Page;
  games: Game[];
  account: Account | null;
  online: boolean;
  reconnecting?: boolean;
  viewAvailable: boolean;
  busy: boolean;
  scanning: boolean;
  status: string;
  accountFeedback: string;
  settings: {
    dark: boolean;
    english: boolean;
    autoUpdate: boolean;
    presentMon: string;
  };
  downloads: Download[];
  magpieStatus: string;
}
export interface Recommendation {
  is32Bit?: boolean;
  graphicsApi?: string;
  gpu: { name: string; vramMb: number | null };
  width: number;
  height: number;
  recommendation: {
    qualityMode: number;
    qualityReason: string;
    performanceMode: number;
    performanceReason: string;
    notices: string[];
    availability: number;
  };
  hipSetupPending: boolean;
}
export interface NeuralOptions {
  backend: string; graphicsApi: string; proxy: string; passes: number | null;
  modelScale: number; temporal: boolean; historyStrength: number; applyModel: boolean; dynamicResolution: number;
  nrInline?: boolean | null; earlyAnyModuleWrap?: boolean | null; allowEnhancedBarriers?: boolean | null;
  lmxxfFastMode?: boolean | null; lmxxfTierSnap?: boolean | null;
}
export interface NeuralReceipt {
  backend: string; passes: number; proxy: string; graphicsApi: string;
  packageVersion?: string;
  statusLabel: string; modeLabel: string; options: NeuralOptions;
}
export interface NeuralPlanResponse {
  plan: NeuralReceipt & { notices: string[] };
  missingResources: string[];
  resourcePreparation?: string[];
  installed: NeuralReceipt | null;
}
export interface Check {
  title: string;
  detail: string;
  problem: boolean;
  unknown: boolean;
}
export interface Wish {
  id: string;
  title: string;
  content: string;
  status: string;
  author: string;
  createdAt: number;
  votes: number;
  comments: number;
  voted: boolean;
  isMine: boolean;
  isDemo: boolean;
  hasImage: boolean;
}
export interface WishList {
  items: Wish[];
  total: number;
  page: number;
  pageSize: number;
  counts: Record<string, number>;
}
export interface WishDetail {
  wish: Wish;
  imageData: string | null;
  comments: {
    id: string;
    author: string;
    content: string;
    createdAt: number;
  }[];
}
export interface Plan {
  configurationSessions?: number;
  grantsMembership?: boolean;
  id: string;
  name: string;
  durationDays: number;
  priceMinor: number;
  currency: string;
}
export interface Order {
  durationDays?: number;
  configurationSessions?: number;
  configurationServiceStatus?: string;
  configurationSessionsUsed?: number | null;
  grantsMembership?: boolean;
  id: string;
  planName: string;
  amountMinor: number;
  currency: string;
  status: string;
  payment: { instructions: string; qrImageUrl: string | null; channels?: {id:string;name:string;qrImageUrl:string}[] } | null;
}
export interface Prompt {
  confirmLabel?: string | null;
  id: string;
  title: string;
  text: string;
  confirm: boolean;
}

export interface SupportedGame { title: string; canonicalName: string; steamAppId: string | null; aliases: string[]; }
export interface SupportedCatalog { asOf: string; games: SupportedGame[]; }
