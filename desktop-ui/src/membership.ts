import type { Account, State } from "./types";

const advancedFeatures = new Set(["schemes.premium.configure", "schemes.game.view", "games.supported.view"]);

export function canUseMemberFeature(state: State, key: string, now = Date.now()) {
  const member = state.account?.membership;
  if (state.viewAvailable === false || member?.tier !== "pro" ||
      (member.expiresAt && !(Date.parse(member.expiresAt) > now))) return false;
  const feature = state.account?.features?.find(f => f.key === key);
  // Older account servers do not yet include the new software entitlement keys.
  // A deliberate server-side denial still takes precedence; Plus remains separate.
  return feature ? feature.allowed && feature.enabled !== false : advancedFeatures.has(key);
}

export function membershipName(account: Account) {
  if (account.membership.tier !== "pro") return "普通账户";
  if (account.membership.kind === "plus") return "高级 Plus 会员";
  if (account.membership.kind === "lifetime" || account.membership.expiresAt?.startsWith("9999-")) return "终身会员";
  return "高级会员";
}
export function canUseRemoteService(state: State, now = Date.now()) {
  const member = state.account?.membership;
  if (!state.viewAvailable || !state.account?.features.some(f => f.key === "services.remote" && f.allowed && f.enabled !== false)) return false;
  if ((member?.remoteSessionsRemaining || 0) > 0) return true;
  return !!(member?.tier === "pro" && member.kind === "plus"
    && (!member.expiresAt || Date.parse(member.expiresAt) > now)
    && member.plusExpiresAt && Date.parse(member.plusExpiresAt) > now
    );
}
