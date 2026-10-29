import type { Account, Order, Plan } from "./types";

// Display catalog is bundled with the client. Account entitlements and CDK
// redemption remain validated by the server.
export const membershipPlans: Plan[] = [
  { id: "mu-pro-monthly", name: "高级会员", durationDays: 30, configurationSessions: 0, priceMinor: 990, currency: "CNY" },
  { id: "mu-pro-lifetime", name: "终身会员", durationDays: 0, configurationSessions: 3, priceMinor: 8880, currency: "CNY" },
  { id: "mu-pro-assisted", name: "高级 Plus 会员", durationDays: 30, configurationSessions: 1, priceMinor: 2990, currency: "CNY" },
  { id: "mu-remote-once", name: "远程指导服务", durationDays: 0, grantsMembership: false, configurationSessions: 1, priceMinor: 3000, currency: "CNY" },
];

export const membershipFeatures: Account["features"] = [
  ...["games.supported.view", "schemes.premium.configure", "schemes.game.view"].map(key => ({ key, enabled: true, minimumTier: "pro", allowed: false })),
  ...["library.manage", "dlss.configure", "optiscaler.configure", "magpie.launch", "game.launch", "game.restore", "configuration.edit", "diagnostics.use", "app.update"].map(key => ({ key, enabled: true, minimumTier: "standard", allowed: true })),
];

export const membershipPayment: NonNullable<Order["payment"]> = {
  instructions: "支付成功后添加微信 xrwCoder，发送付款凭证和所选套餐，获取会员 CDK。",
  qrImageUrl: null,
  channels: [
    { id: "wechat", name: "微信支付", qrImageUrl: "./payments/wechat.jpg" },
    { id: "alipay", name: "支付宝", qrImageUrl: "./payments/alipay.jpg" },
  ],
};
