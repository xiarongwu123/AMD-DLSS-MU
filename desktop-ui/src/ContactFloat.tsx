import { useEffect, useRef, useState } from "react";
import { Check, Copy, Headphones, MessageCircle, Send, ShieldCheck, Sparkles, X } from "lucide-react";
import { knowledgeVersion, reviewedAt, searchSupport, supportSummary, type SupportArticle } from "./support/search";
import "./contact-float.css";

const wechat = "xrwCoder";
const suggestions = ["API_CONFIRM_REQUIRED", "缺少模型文件", "喜鹊目标窗口的权限高于 Magpie", "会员有效但方案没解锁"];
type Exchange = { id: number; question: string; articles: SupportArticle[] };
export function ContactFloat() {
  const [open, setOpen] = useState(false);
  const [contact, setContact] = useState(false);
  const [feedback, setFeedback] = useState("");
  const [input, setInput] = useState("");
  const [messages, setMessages] = useState<Exchange[]>([]);
  const root = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const editor = useRef<HTMLTextAreaElement>(null);
  const history = useRef<HTMLDivElement>(null);
  const serial = useRef(0);
  useEffect(() => {
    if (!open) return;
    editor.current?.focus();
    const outside = (event: PointerEvent) => {
      if (!root.current?.contains(event.target as Node)) setOpen(false);
    };
    const escape = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.stopPropagation();
        setOpen(false);
        trigger.current?.focus();
      }
    };
    document.addEventListener("pointerdown", outside);
    document.addEventListener("keydown", escape, true);
    return () => {
      document.removeEventListener("pointerdown", outside);
      document.removeEventListener("keydown", escape, true);
    };
  }, [open]);
  useEffect(() => {
    if (open && history.current) history.current.scrollTop = history.current.scrollHeight;
  }, [messages, open, contact]);
  async function copy(value: string, success: string) {
    try {
      await navigator.clipboard.writeText(value);
      setFeedback(success);
    } catch {
      setFeedback("复制失败，请选中文字手动复制");
    }
  }
  function ask(value: string) {
    const question = value.trim().slice(0, 4000);
    if (!question) return;
    setMessages(old => [...old.slice(-11), { id: ++serial.current, question, articles: searchSupport(question) }]);
    setInput("");
    setFeedback("");
    editor.current?.focus();
  }
  const last = messages.at(-1);
  return <div className="contact-float" ref={root}>
    {open && <section className="support-panel" id="mu-support-panel" role="dialog" aria-labelledby="mu-support-title">
      <header className="support-header">
        <div className="support-heading"><span className="support-avatar"><Sparkles size={22} /></span><div><strong id="mu-support-title">MU 智能客服</strong><small>报错排查 · 本地知识库</small></div></div>
        <button className="icon-button" aria-label="关闭智能客服" onClick={() => { setOpen(false); trigger.current?.focus(); }}><X size={19} /></button>
      </header>
      <nav className="support-tabs" aria-label="客服方式">
        <button aria-pressed={!contact} onClick={() => { setContact(false); setFeedback(""); }}><MessageCircle size={16}/>报错排查</button>
        <button aria-pressed={contact} onClick={() => { setContact(true); setFeedback(""); }}><Headphones size={16}/>联系作者</button>
      </nav>
      {contact ? <div className="support-contact">
        <h3>还没解决？把问题发给我</h3>
        <p>请附上 MU 版本、显卡型号、游戏名称和完整报错。远程指导按账户服务权益提供。</p>
        <img src="/artwork/wechat-xrwCoder.jpg" alt="添加作者微信的二维码" />
        <p className="contact-wechat">微信号 <strong>{wechat}</strong></p>
        <button className="button primary" onClick={() => void copy(wechat, "微信号已复制")}><Copy size={17}/>复制微信号</button>
        {last && <button className="button" onClick={() => void copy(supportSummary(last.question, last.articles), "排查记录已复制")}><Copy size={16}/>复制排查记录</button>}
      </div> : <>
        <div className="support-history" ref={history}>
          <div className="support-welcome"><span className="support-eyebrow">遇到问题，先来这里</span><h3>把报错交给我。</h3><p>粘贴完整报错，或描述在哪一步遇到问题。我会查找对应原因和处理步骤。</p><div className="support-suggestions">{suggestions.map(q => <button key={q} onClick={() => ask(q)}>{q}</button>)}</div></div>
          <p className="support-privacy"><ShieldCheck size={14}/>本地检索，不上传输入内容或自动读取日志。</p>
          {messages.map(message => <div className="support-exchange" key={message.id}>
            <p className="support-question">{message.question}</p>
            <div className="support-answer">
              <span className="support-answer-label"><Sparkles size={14}/>{message.articles.length ? "找到相关排查建议" : "还需要一些信息"}</span>
              {message.articles.length ? message.articles.map(article => <article key={article.id}>
                <h4>{article.title}</h4><p>{article.answer}</p>
                <ol>{article.steps.map((step, i) => <li key={i}>{step}</li>)}</ol>
                <p className="support-escalation">仍未解决：{article.escalate_when}</p>
                <details><summary>知识来源 · {article.id}</summary><p>{article.version} · {article.source}</p></details>
              </article>) : <><p>当前知识库没有足够匹配的记录，暂时无法确定原因。请补充完整错误文字、MU 版本、显卡型号、游戏名和所选方案，再次查询。</p><p>例如：RX 6750 XT，配置游戏时提示 [ARCH_INCOMPATIBLE]。</p></>}
              <div className="support-answer-actions"><button onClick={() => void copy(supportSummary(message.question, message.articles), "排查记录已复制")}><Copy size={14}/>复制建议</button><button onClick={() => setContact(true)}>未解决，联系作者 →</button></div>
            </div>
          </div>)}
        </div>
        <form className="support-composer" onSubmit={event => { event.preventDefault(); ask(input); }}>
          <label className="support-input-label" htmlFor="mu-support-input">错误码或问题描述</label>
          <div className="support-input-row"><textarea id="mu-support-input" ref={editor} value={input} maxLength={4000} rows={2} placeholder="粘贴报错，例如 API_CONFIRM_REQUIRED…" onChange={event => setInput(event.target.value)} onKeyDown={event => {
            if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing && event.keyCode !== 229) { event.preventDefault(); ask(input); }
          }}/><button type="submit" aria-label="查询解决方案" disabled={!input.trim()}><Send size={19}/></button></div>
          <div className="support-composer-meta"><span>Enter 查询 · Shift + Enter 换行</span>{messages.length > 0 && <button type="button" onClick={() => { setMessages([]); setFeedback(""); }}>清空对话</button>}</div>
        </form>
      </>}
      <footer className="support-footer"><span>知识库匹配建议，需结合实际环境核对 · 基线 {knowledgeVersion}</span><span title={`知识库复核日期：${reviewedAt}`}>{reviewedAt}</span></footer>
      <p className="support-feedback" role="status">{feedback && <><Check size={14}/>{feedback}</>}</p>
    </section>}
    <button ref={trigger} className="contact-trigger" aria-expanded={open} aria-controls="mu-support-panel"
      onClick={() => { setOpen(!open); setFeedback(""); }}><MessageCircle size={21}/><span>遇到问题？智能客服</span><span className="support-online" aria-hidden="true"/></button>
  </div>;
}
