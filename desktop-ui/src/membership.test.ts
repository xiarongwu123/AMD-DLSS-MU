import { expect, it } from "vitest";
import { canUseMemberFeature, canUseRemoteService } from "./membership";
import type { State } from "./types";

const member = { viewAvailable: true, account: { membership: { tier: "pro", kind: "advanced", expiresAt: "2099-01-01T00:00:00Z" }, features: [] } } as unknown as State;
it.each(["advanced", "lifetime", "plus"])("%s 会员兼容旧后台缺失的软件权限字段", kind => {
  const state = { ...member, account: { ...member.account!, membership: { ...member.account!.membership, kind: kind as "advanced" } } };
  for (const feature of ["schemes.premium.configure", "schemes.game.view", "games.supported.view"]) expect(canUseMemberFeature(state, feature)).toBe(true);
  expect(canUseMemberFeature(state, "services.remote")).toBe(false);
  expect(canUseRemoteService(state)).toBe(false);
});
it("过期、普通、断线及后台明确禁用不能借旧字段兼容获得权限", () => {
  const key = "schemes.premium.configure";
  expect(canUseMemberFeature({ ...member, viewAvailable: false }, key)).toBe(false);
  expect(canUseMemberFeature({ ...member, account: { ...member.account!, membership: { tier: "standard", expiresAt: null } } }, key)).toBe(false);
  expect(canUseMemberFeature({ ...member, account: { ...member.account!, membership: { tier: "pro", expiresAt: "2000-01-01T00:00:00Z" } } }, key)).toBe(false);
  expect(canUseMemberFeature({ ...member, account: { ...member.account!, features: [{ key, allowed: false }] } }, key)).toBe(false);
  expect(canUseMemberFeature({ ...member, account: { ...member.account!, features: [{ key, allowed: true, enabled: false }] } }, key)).toBe(false);
});
