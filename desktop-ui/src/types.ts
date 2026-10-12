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
}
export interface CommunityPresetDetail {
  item: CommunityPreset;
  settings: PresetSetting[];
  imageData: string | null;
}
export interface CommunityPresetList {
  items: CommunityPreset[];
  total: number;
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
  cover: string | null;
  state: string;
  mode: number | null;
  steamAppId: string | null;
}
export interface Account {
  id: string;
  email: string;
  membership: { tier: string; expiresAt: string | null };
  features: { key: string; allowed: boolean }[];
  paymentsEnabled: boolean;
}
export interface Download {
  id: string;
  title: string;
  state: string;
  percent: number;
  size: number;
  detail: string;
  paused: boolean;
}
export interface State {
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
  id: string;
  name: string;
  durationDays: number;
  priceMinor: number;
  currency: string;
}
export interface Order {
  id: string;
  planName: string;
  amountMinor: number;
  currency: string;
  status: string;
  payment: { instructions: string; qrImageUrl: string | null };
}
export interface Prompt {
  id: string;
  title: string;
  text: string;
  confirm: boolean;
}
