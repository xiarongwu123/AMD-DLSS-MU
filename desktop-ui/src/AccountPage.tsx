import { useState, useEffect } from "react";
import { Eye, EyeOff, Mail, LockKeyhole, Check } from "lucide-react";
import { Button, Modal, Loading } from "./components";
import { rpc } from "./bridge";
import type { State, Plan, Order } from "./types";
export function AccountPage({
  state,
  notify,
}: {
  state: State;
  notify: (s: string) => void;
}) {
  const [mode, setMode] = useState("login"),
    [email, setEmail] = useState(""),
    [password, setPassword] = useState(""),
    [confirmation, setConfirmation] = useState(""),
    [current, setCurrent] = useState(""),
    [code, setCode] = useState(""),
    [keep, setKeep] = useState(true),
    [agreed, setAgreed] = useState(false),
    [visible, setVisible] = useState(false),
    [busy, setBusy] = useState(false),
    [error, setError] = useState(""),
    [purchase, setPurchase] = useState(false),
    [cdk, setCdk] = useState(""),
    [cooldown, setCooldown] = useState(0);
  useEffect(() => {
    if (!cooldown) return;
    const t = setTimeout(() => setCooldown((c) => c - 1), 1000);
    return () => clearTimeout(t);
  }, [cooldown]);
  const signed = !!state.account;
  const run = async (command: string, args: Record<string, unknown>) => {
    setBusy(true);
    setError("");
    try {
      const result = await rpc<{ retryAfterSeconds: number }>(command, args);
      if (command === "sendCode") {
        setCooldown(result.retryAfterSeconds);
        notify("验证码已发送");
      } else if (command === "redeemMembership") {
        setCdk("");
        notify("兑换成功，会员状态已更新。");
      } else if (command === "resetPassword" || command === "changePassword") {
        setMode(signed ? "profile" : "login");
        setPassword("");
        setCurrent("");
        notify("密码已更新");
      }
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (password.length < 8) {
      setError("密码至少需要 8 位字符。");
      return;
    }
    if (mode === "register" && !agreed) {
      setError("请先阅读并同意用户协议与隐私政策。");
      return;
    }
    if (mode !== "login" && password !== confirmation) {
      setError("两次输入的密码不一致。");
      return;
    }
    await run(
      mode === "login"
        ? "login"
        : mode === "register"
          ? "register"
          : mode === "reset"
            ? "resetPassword"
            : "changePassword",
      { email: email.trim(), password, current, code, keep },
    );
  };
  return (
    <div className="account-layout">
      <section className="account-art">
        <div>
          <h1>
            MU 通行证
            <br />
            连接你的游戏体验
          </h1>
          <p>账户、配置和会员状态，统一管理。</p>
        </div>
      </section>
      <section className="account-form panel">
        {signed && mode !== "password" ? (
          <>
            <span className="eyebrow">MU ACCOUNT</span>
            <h2>我的账户</h2>
            <p className="account-email">{state.account!.email}</p>
            <div className="membership">
              <Check />
              <div>
                <strong>
                  {state.account!.membership.tier === "pro"
                    ? "MU PRO"
                    : "普通账户"}
                </strong>
                <p>
                  {state.account!.membership.expiresAt
                    ? `有效期至 ${new Date(state.account!.membership.expiresAt).toLocaleDateString()}`
                    : "已登录 MU"}
                </p>
              </div>
            </div>
            <p className="muted">
              {state.online
                ? "已联网验证"
                : state.reconnecting
                  ? "账户连接暂时波动，正在重试…"
                  : "连接中断：重新联网验证后可使用功能。"}
            </p>
            <Button
              primary
              disabled={busy}
              onClick={() => run("enterHome", {})}
            >
              {state.online ? "进入首页 →" : "重新连接账户服务"}
            </Button>
            <Button disabled={busy} onClick={() => run("refreshAccount", {})}>
              刷新会员状态
            </Button>
            {state.account!.paymentsEnabled && (
              <Button onClick={() => setPurchase(true)}>
                申请 Pro · 人工核对到账
              </Button>
            )}
            <form
              onSubmit={(e) => {
                e.preventDefault();
                if (!busy && !state.busy && cdk.trim())
                  void run("redeemMembership", { code: cdk.trim() });
              }}
            >
              <label>
                CDK 兑换会员
                <input
                  value={cdk}
                  onChange={(e) => setCdk(e.target.value)}
                  placeholder="输入会员兑换码"
                  maxLength={80}
                  required
                  autoComplete="off"
                  spellCheck={false}
                />
              </label>
              <p className="muted">
                每个兑换码限用一次，会员时长将叠加至当前有效期。
              </p>
              <Button
                type="submit"
                disabled={busy || state.busy || !cdk.trim()}
              >
                {busy ? "正在处理…" : "兑换会员"}
              </Button>
            </form>
            <Button
              onClick={() => {
                setMode("password");
                setPassword("");
                setConfirmation("");
              }}
            >
              修改密码
            </Button>
            <Button
              disabled={busy || state.busy}
              onClick={() => run("logout", {})}
            >
              退出登录
            </Button>
          </>
        ) : (
          <>
            {!signed && (
              <div className="tabs">
                {[
                  ["login", "登录"],
                  ["register", "注册"],
                ].map(([key, title]) => (
                  <button
                    key={key}
                    className={mode === key ? "active" : ""}
                    onClick={() => {
                      setMode(key);
                      setError("");
                    }}
                  >
                    {title}
                  </button>
                ))}
              </div>
            )}
            <h2>
              {mode === "register"
                ? "创建 MU 账户"
                : mode === "reset"
                  ? "找回密码"
                  : mode === "password"
                    ? "修改密码"
                    : "欢迎回来"}
            </h2>
            <form onSubmit={submit}>
              {mode !== "password" && (
                <label>
                  邮箱
                  <div className="input-icon">
                    <Mail size={18} />
                    <input
                      type="email"
                      autoComplete="email"
                      required
                      maxLength={254}
                      value={email}
                      onChange={(e) => setEmail(e.target.value)}
                      placeholder="name@example.com"
                    />
                  </div>
                </label>
              )}
              {(mode === "register" || mode === "reset") && (
                <label>
                  邮箱验证码
                  <div className="code-input">
                    <input
                      required
                      value={code}
                      maxLength={6}
                      onChange={(e) => setCode(e.target.value)}
                      placeholder="6 位验证码"
                    />
                    <Button
                      type="button"
                      disabled={busy || cooldown > 0 || !email}
                      onClick={() =>
                        run("sendCode", {
                          email,
                          purpose: mode === "reset" ? "reset" : "register",
                        })
                      }
                    >
                      {cooldown ? `${cooldown}s 后重发` : "获取验证码"}
                    </Button>
                  </div>
                </label>
              )}
              {mode === "password" && (
                <label>
                  当前密码
                  <input
                    type="password"
                    autoComplete="current-password"
                    required
                    value={current}
                    onChange={(e) => setCurrent(e.target.value)}
                  />
                </label>
              )}
              <label>
                {mode === "login" ? "密码" : "新密码"}
                <div className="input-icon">
                  <LockKeyhole size={18} />
                  <input
                    autoComplete={
                      mode === "login" ? "current-password" : "new-password"
                    }
                    required
                    minLength={8}
                    maxLength={128}
                    type={visible ? "text" : "password"}
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    placeholder="至少 8 位字符"
                  />
                  <button
                    type="button"
                    className="icon-button"
                    aria-label={visible ? "隐藏密码" : "显示密码"}
                    onClick={() => setVisible((v) => !v)}
                  >
                    {visible ? <EyeOff size={18} /> : <Eye size={18} />}
                  </button>
                </div>
              </label>
              {mode !== "login" && (
                <label>
                  确认新密码
                  <input
                    type="password"
                    required
                    value={confirmation}
                    onChange={(e) => setConfirmation(e.target.value)}
                  />
                </label>
              )}
              {mode === "login" && (
                <label className="check-label">
                  <input
                    type="checkbox"
                    checked={keep}
                    onChange={(e) => setKeep(e.target.checked)}
                  />
                  记住登录
                </label>
              )}
              {mode === "register" && (
                <label className="check-label">
                  <input
                    type="checkbox"
                    checked={agreed}
                    onChange={(e) => setAgreed(e.target.checked)}
                  />
                  <span>
                    同意{" "}
                    <button
                      type="button"
                      className="text-button"
                      onClick={() =>
                        rpc("legal", { kind: "terms" }).catch((e) =>
                          setError(e.message),
                        )
                      }
                    >
                      用户协议
                    </button>{" "}
                    与{" "}
                    <button
                      type="button"
                      className="text-button"
                      onClick={() =>
                        rpc("legal", { kind: "privacy" }).catch((e) =>
                          setError(e.message),
                        )
                      }
                    >
                      隐私政策
                    </button>
                  </span>
                </label>
              )}
              <Button type="submit" primary disabled={busy}>
                {busy
                  ? "正在处理…"
                  : mode === "login"
                    ? "登录"
                    : mode === "register"
                      ? "创建账户"
                      : "保存密码"}
              </Button>
              {mode === "login" && (
                <button
                  type="button"
                  className="text-button"
                  onClick={() => {
                    setMode("reset");
                    setPassword("");
                  }}
                >
                  忘记密码？
                </button>
              )}
              {(mode === "reset" || mode === "password") && (
                <Button
                  type="button"
                  onClick={() => setMode(signed ? "profile" : "login")}
                >
                  返回
                </Button>
              )}
            </form>
          </>
        )}
        {error && (
          <p role="alert" className="error">
            {error}
          </p>
        )}
        <p className="muted">{state.accountFeedback}</p>
      </section>
      {purchase && (
        <PurchaseDialog onClose={() => setPurchase(false)} notify={notify} />
      )}
    </div>
  );
}
function PurchaseDialog({
  onClose,
  notify,
}: {
  onClose: () => void;
  notify: (s: string) => void;
}) {
  const [plans, setPlans] = useState<Plan[] | null>(null),
    [orders, setOrders] = useState<Order[]>([]),
    [order, setOrder] = useState<Order | null>(null),
    [plan, setPlan] = useState(""),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false);
  const refresh = async () => {
    setBusy(true);
    try {
      const p = await rpc<{ items: Plan[]; paymentsEnabled: boolean }>("plans");
      if (!p.paymentsEnabled) throw new Error("人工收款暂未开放。");
      setPlans(p.items);
      setPlan((v) => v || p.items[0]?.id || "");
      const o = await rpc<{ items: Order[] }>("orders");
      setOrders(o.items);
      if (order) setOrder(await rpc<Order>("order", { id: order.id }));
      await rpc("refreshAccount");
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  useEffect(() => {
    void refresh();
  }, []);
  const act = async (command: string, args: Record<string, unknown>) => {
    setBusy(true);
    setError("");
    try {
      const o = await rpc<Order>(command, args);
      if (command === "createOrder") setOrder(o);
      else {
        notify("已提交，管理员核对到账后开通。");
        if (order) setOrder(await rpc<Order>("order", { id: order.id }));
      }
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  return (
    <Modal
      title="开通 Pro"
      onClose={onClose}
      footer={
        <>
          <Button onClick={refresh} disabled={busy}>
            刷新状态
          </Button>
          <Button
            primary
            disabled={
              busy ||
              (!order && !plan) ||
              order?.status === "paid" ||
              order?.status === "review_requested"
            }
            onClick={() =>
              order
                ? act("reviewOrder", { id: order.id })
                : act("createOrder", { planId: plan })
            }
          >
            {order
              ? order.status === "paid"
                ? "已开通"
                : order.status === "review_requested"
                  ? "待核对到账"
                  : "我已付款 · 提交核对"
              : "创建付款申请"}
          </Button>
        </>
      }
    >
      {!plans && !error ? (
        <Loading />
      ) : order ? (
        <>
          <h3>{order.planName}</h3>
          <p className="price">¥{(order.amountMinor / 100).toFixed(2)}</p>
          <p>订单号：{order.id}</p>
          <Button
            onClick={() =>
              navigator.clipboard
                .writeText(order.id)
                .then(() => notify("订单号已复制"))
                .catch(() => setError("复制失败，可选中订单号手动复制。"))
            }
          >
            复制订单号
          </Button>
          <p className="preserve">{order.payment?.instructions}</p>
          {order.payment?.qrImageUrl && (
            <img
              className="payment-qr"
              src={order.payment.qrImageUrl}
              alt="收款二维码"
            />
          )}
          <p className="muted">付款时备注订单号；提交付款声明不会自动开通。</p>
        </>
      ) : (
        <>
          <label>
            套餐
            <select value={plan} onChange={(e) => setPlan(e.target.value)}>
              {plans?.map((p) => (
                <option value={p.id} key={p.id}>
                  {p.name} · {p.durationDays} 天 · ¥
                  {(p.priceMinor / 100).toFixed(2)}
                </option>
              ))}
            </select>
          </label>
          {orders
            .filter((o) => ["pending", "review_requested"].includes(o.status))
            .map((o) => (
              <Button key={o.id} onClick={() => setOrder(o)}>
                继续处理 {o.planName} · {o.id.slice(-8)}
              </Button>
            ))}
        </>
      )}
      {error && <p className="error">{error}</p>}
    </Modal>
  );
}
