import { useState, useEffect, useId } from "react";
import { Eye, EyeOff, Mail, LockKeyhole, Check } from "lucide-react";
import { Button, Modal, Loading } from "./components";
import { rpc } from "./bridge";
import { PaymentCodes } from "./PaymentCodes";
import { PlanWall } from "./PlanWall";
import { membershipName } from "./membership";
import "./purchase.css";
import type { State, Plan, Order } from "./types";
export function AccountPage({
  state,
  notify,
  requestPurchase = false,
  requestPlan = "",
  onPurchaseHandled,
}: {
  state: State;
  notify: (s: string) => void;
  requestPurchase?: boolean;
  requestPlan?: string;
  onPurchaseHandled?: ()=>void;
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
    [purchaseInitialPlan, setPurchaseInitialPlan] = useState(""),
    [cooldown, setCooldown] = useState(0);
  useEffect(() => {
    if (!cooldown) return;
    const t = setTimeout(() => setCooldown((c) => c - 1), 1000);
    return () => clearTimeout(t);
  }, [cooldown]);
  const signed = !!state.account;
  useEffect(()=>{
    if (requestPurchase && signed) {
      setPurchaseInitialPlan(requestPlan);
      setPurchase(true);
      onPurchaseHandled?.();
    }
  },[requestPurchase,requestPlan,signed,onPurchaseHandled]);
  const run = async (command: string, args: Record<string, unknown>) => {
    setBusy(true);
    setError("");
    try {
      if (command === "login") window.dispatchEvent(new Event("mu-login-attempt"));
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
      if (command === "login") window.dispatchEvent(new Event("mu-login-cancel"));
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
                  {membershipName(state.account!)}
                </strong>
                <p>
                  {state.account!.membership.expiresAt?.startsWith("9999-")
                    ? "终身有效"
                    : state.account!.membership.expiresAt
                    ? `有效期至 ${new Date(state.account!.membership.expiresAt).toLocaleDateString()}`
                    : "已登录 MU"}
                </p>
                {state.account!.membership.kind === "plus" && state.account!.membership.plusExpiresAt &&
                  <p>Plus 权益至 {new Date(state.account!.membership.plusExpiresAt!).toLocaleDateString()} · 含 1 次一对一配置服务</p>}
                {typeof state.account!.membership.remoteSessionsRemaining === "number" &&
                  <p>远程指导剩余 {state.account!.membership.remoteSessionsRemaining} 次</p>}
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
                开通 / 续费高级会员
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
                每个兑换码限用一次，按对应套餐开通会员或服务权益。
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
        <PurchaseDialog state={state} initialPlanId={purchaseInitialPlan} onClose={() => {setPurchase(false);setPurchaseInitialPlan("");}} notify={notify} />
      )}
    </div>
  );
}
export function PurchaseDialog({
  state,
  initialPlanId = "",
  onClose,
  notify,
}: {
  state: State;
  initialPlanId?: string;
  onClose: () => void;
  notify: (s: string) => void;
}) {
  const cdkFormId = useId();
  const [plans, setPlans] = useState<Plan[] | null>(null);
  const [plan, setPlan] = useState(initialPlanId);
  const [payment, setPayment] = useState<Order["payment"]>(null);
  const [matching, setMatching] = useState(false);
  const [redeeming, setRedeeming] = useState(false);
  const [cdk, setCdk] = useState("");
  const [error, setError] = useState("");
  const [success, setSuccess] = useState(false);
  const [busy, setBusy] = useState(false);
  const refresh = async () => {
    setBusy(true); setError("");
    try {
      const data = await rpc<{ items: Plan[]; cdkPlanMatching?: boolean; payment?: Order["payment"] }>("plans");
      setPlans(data.items); setMatching(data.cdkPlanMatching === true); setPayment(data.payment ?? null);
      setPlan(value => data.items.some(p => p.id === value) ? value : data.items[0]?.id ?? "");
      await rpc("refreshAccount");
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  };
  useEffect(() => { void refresh(); }, []);
  const selected = plans?.find(p => p.id === plan);
  async function redeem() {
    if (busy || success || !matching || !selected || !cdk.trim()) return;
    setBusy(true); setError("");
    try {
      await rpc("redeemMembership", { code: cdk.trim(), planId: selected!.id });
      // Redemption response already refreshes the native account. Never submit a
      // used code again if the optional subsequent heartbeat fails.
      setSuccess(true); setCdk(""); notify("CDK 校验成功，会员及服务权益已更新。");
      try { await rpc("refreshAccount"); } catch { setError("兑换已成功，账户刷新暂未完成，请点击刷新状态；无需再次兑换。"); }
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }
  return <Modal title="开通高级会员" wide className="purchase-modal" onClose={() => { if (!busy) onClose(); }} footer={<>
    <Button onClick={refresh} disabled={busy}>刷新状态</Button>
    {success ? <Button primary onClick={onClose}>完成</Button> : redeeming
      ? <Button primary type="submit" form={cdkFormId} disabled={busy || !cdk.trim() || !selected || !matching}>{busy ? "正在校验…" : "校验 CDK · 兑换权益"}</Button>
      : <Button primary disabled={busy || !selected || !matching} onClick={() => { setRedeeming(true); setError(""); }}>支付完成 校验CDK</Button>}
  </>}>
    {!plans && !error ? <Loading/> : <>
      {!redeeming && !success && <>
        <PlanWall plans={plans || []} selected={plan} onSelect={value => { setPlan(value); setError(""); }} features={state.account?.features || []} disabled={busy}/>
        {selected && payment && <PaymentCodes payment={payment} amountMinor={selected.priceMinor} cdk/>}
        {selected && !payment && <p className="muted">当前未提供在线收款码，请联系微信 xrwCoder 获取付款方式；已有 CDK 可直接校验。</p>}
        <p className="purchase-manual-note">支付成功后添加微信 <strong>xrwCoder</strong>，发送付款凭证和所选套餐，获取会员 CDK。付款不会自动开通，不自动续费或扣款。</p>
      </>}
      {redeeming && !success && <form id={cdkFormId} className="purchase-cdk" onSubmit={event => { event.preventDefault(); void redeem(); }}>
        <h3>{selected?.name} · CDK 兑换</h3>
        <p>支付成功后添加 xrwCoder 获取会员 CDK。兑换码需与所选套餐匹配；验证成功后自动刷新会员及服务权益。</p>
        <label htmlFor="purchase-cdk">会员 CDK</label>
        <input id="purchase-cdk" autoFocus autoComplete="off" maxLength={80} value={cdk} disabled={busy} onChange={e => setCdk(e.target.value)} placeholder="粘贴与此套餐对应的 CDK"/>
        <Button type="button" disabled={busy} onClick={() => { setRedeeming(false); setCdk(""); setError(""); }}>返回套餐选择</Button>
      </form>}
      {success && <div className="purchase-service"><Check/><h3>兑换成功，权益已更新</h3><p>{selected?.name}已兑换。请在账户页面查看会员有效期与剩余远程指导次数。</p></div>}
      {!matching && plans && <p role="alert">当前后台尚不支持套餐 CDK 校验，请先联系管理员更新后台。已有会员身份不受影响，请勿重复付款。</p>}
      <Button disabled={busy} onClick={() => navigator.clipboard.writeText("xrwCoder").then(() => notify("微信号已复制")).catch(() => setError("复制失败，请手动添加微信 xrwCoder。"))}>复制微信 xrwCoder</Button>
    </>}
    {error && <p role="alert" className="error">{error}</p>}
  </Modal>;
}
