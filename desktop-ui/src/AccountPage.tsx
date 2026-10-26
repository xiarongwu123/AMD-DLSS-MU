import { useState, useEffect } from "react";
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
  const [available,setAvailable]=useState(false);
  const [plans, setPlans] = useState<Plan[] | null>(null),
    [orders, setOrders] = useState<Order[]>([]),
    [order, setOrder] = useState<Order | null>(null),
    [plan, setPlan] = useState(initialPlanId),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false);
  const refresh = async () => {
    setBusy(true);
    setError("");
    try {
      const p = await rpc<{ items: Plan[]; paymentsEnabled: boolean }>("plans");
      setPlans(p.items);
      setAvailable(p.paymentsEnabled);
      setPlan(v=>p.items.some(item=>item.id===v) ? v : p.items[0]?.id || "");
      if (!p.paymentsEnabled) setError("人工收款暂未开放，请联系微信 xrwCoder。");
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
      title="开通高级会员"
      wide
      className="purchase-modal"
      onClose={onClose}
      footer={
        <>
          <Button onClick={refresh} disabled={busy}>
            刷新状态
          </Button>
          <Button
            primary
            charge={!order}
            disabled={
              busy ||
              (!order && (!plan || !available)) ||
              (!!order && !["pending","review_requested","paid"].includes(order.status)) ||
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
              : "确认套餐 · 创建付款申请"}
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
          {order.payment && order.status==="pending" && <PaymentCodes payment={order.payment} amountMinor={order.amountMinor}/>}

          <p className="muted">{order.status === "paid" ? "管理员已确认到账，点击刷新状态可更新会员信息。" : order.status==="review_requested" ? "已提交付款声明，请勿重复付款，等待管理员核对到账。" : "付款时备注订单号；提交付款声明不会自动开通。"}</p>
          <p>{order.grantsMembership===false ? "单次远程指导 · 不附带会员" : order.durationDays === 0 ? "终身会员 · 终身有效" : order.durationDays ? `${order.configurationSessions ? "高级 Plus 会员" : "高级会员"} · ${order.durationDays} 天` : "期限以订单为准"}</p>
          {order.grantsMembership!==false && order.durationDays===0 && <div className="purchase-service">
            <strong>终身专属权益</strong>
            <p>终身享用所有付费软件功能，包含专属功能定制与专属优化。</p>
            <p>{order.status==="paid" ? "添加微信 xrwCoder，发送订单号和你的定制或优化需求，由我人工提供服务。" : "核对到账后，添加微信 xrwCoder 提交定制或优化需求。"}</p>
            <Button onClick={()=>navigator.clipboard.writeText("xrwCoder").then(()=>notify("微信号已复制")).catch(()=>setError("复制失败，微信号：xrwCoder"))}>复制联系微信</Button>
          </div>}
          {(order.configurationSessions || 0)>0 && <div className="purchase-service">
            <strong>{order.configurationSessions} 次远程指导</strong>
            <p>已用 {order.configurationSessionsUsed ?? (order.configurationServiceStatus === "completed" ? order.configurationSessions : 0)} 次 · 剩余 {Math.max(0,(order.configurationSessions || 0)-(order.configurationSessionsUsed ?? (order.configurationServiceStatus === "completed" ? order.configurationSessions || 0 : 0)))} 次</p>
            <p>{order.configurationServiceStatus === "completed" ? "本订单远程指导已全部完成" : order.status === "paid" ? "已可预约：添加微信 xrwCoder，发送订单号预约指导。" : "核对到账后，添加微信 xrwCoder 预约指导。"}</p>
            <Button onClick={()=>navigator.clipboard.writeText("xrwCoder").then(()=>notify("微信号已复制")).catch(()=>setError("复制失败，微信号：xrwCoder"))}>复制联系微信</Button>
          </div>}
          <Button disabled={busy} onClick={()=>setOrder(null)}>返回套餐选择</Button>
        </>
      ) : (
        <>
          <PlanWall plans={plans || []} selected={plan} onSelect={setPlan} features={state.account?.features || []} disabled={busy}/>
          {!plans?.length && !busy && <p>暂无可购买的套餐，请联系微信 xrwCoder。</p>}
          <p className="purchase-manual-note">人工收款 · 核对到账后开通 · 不自动续费或扣款</p>
          {orders
            .filter((o) => ["pending", "review_requested"].includes(o.status) || (o.status === "paid" && ((o.configurationSessions || 0)>0 || o.durationDays===0)))
            .map((o) => (
              <Button key={o.id} disabled={busy} onClick={async()=>{
                setBusy(true);setError("");
                try { setOrder(await rpc<Order>("order",{id:o.id})); }
                catch(e){setError((e as Error).message);}
                finally{setBusy(false);}
              }}>
                {o.status==="paid" ? "查看权益" : "继续处理"} {o.planName} · {o.id.slice(-8)}
              </Button>
            ))}
        </>
      )}
      {error && <p className="error">{error}</p>}
    </Modal>
  );
}
