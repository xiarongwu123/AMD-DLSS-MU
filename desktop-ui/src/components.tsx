import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { X, LoaderCircle } from "lucide-react";
import ElectricBorder from "./reactbits/ElectricBorder";
export type CardOrigin = { left: number; top: number; width: number; height: number; cover: string | null; title: string };
export function Button({
  children,
  primary = false,
  charge = false,
  ...props
}: React.ButtonHTMLAttributes<HTMLButtonElement> & { primary?: boolean; charge?: boolean }) {
  const [energized, setEnergized] = useState(false);
  const [prepared, setPrepared] = useState(false);
  const arm = () => {
    if (!charge || props.disabled || !document.querySelector('.app-shell[data-effects="on"]')) return;
    setPrepared(true); setEnergized(true);
  };
  return (
    <button
      {...props}
      className={`button ${primary ? "primary" : ""} ${charge ? "charge-button" : ""} ${energized ? "energized" : ""} ${props.className || ""}`}
      onPointerEnter={(event) => { arm(); props.onPointerEnter?.(event); }}
      onPointerLeave={(event) => { setEnergized(false); props.onPointerLeave?.(event); }}
      onFocus={(event) => { arm(); props.onFocus?.(event); }}
      onBlur={(event) => { setEnergized(false); props.onBlur?.(event); }}
      onClick={(event) => { setEnergized(false); props.onClick?.(event); }}
    >
      {prepared && <span className="charge-electric" aria-hidden="true"><ElectricBorder
        active={energized && !props.disabled} color="#c8ff6b" glowColor="#adff18"
        chaos={0.3} spread={7} thickness={1.4} speed={1.7} glow={0.25} arcs={2}
        strands={2} intensity={0.7} borderRadius={14} className="charge-border" /></span>}
      {charge && <span className="charge-energy" aria-hidden="true"><i /><i /><i /><i /></span>}
      {children}
    </button>
  );
}
export function Modal({
  title,
  children,
  footer,
  onClose,
  wide = false,
  cardOrigin,
  className = "",
  headerArt,
  backdrop,
}: {
  title: string;
  children: ReactNode;
  footer?: ReactNode;
  onClose: () => void;
  wide?: boolean;
  cardOrigin?: CardOrigin | null;
  className?: string;
  headerArt?: ReactNode;
  backdrop?: ReactNode;
}) {
  const ref = useRef<HTMLDivElement>(null);
  useLayoutEffect(() => {
    const root = ref.current;
    if (!root || !cardOrigin || !root.animate) return;
    const end = root.getBoundingClientRect();
    root.style.animation = "none";
    const x = cardOrigin.left + cardOrigin.width / 2 - end.left - end.width / 2;
    const y = cardOrigin.top + cardOrigin.height / 2 - end.top - end.height / 2;
    const animation = root.animate([
      { transform: `translate(${x}px, ${y}px) scale(${cardOrigin.width / end.width}, ${cardOrigin.height / end.height})`, borderRadius: "20px" },
      { transform: "translate(0, 0) scale(1, 1)", borderRadius: "22px" },
    ], { duration: 620, easing: "cubic-bezier(.22,1,.36,1)" });
    return () => animation.cancel();
  }, [cardOrigin]);
  const closeRef = useRef(onClose);
  closeRef.current = onClose;
  useEffect(() => {
    const previous = document.activeElement as HTMLElement;
    const root = ref.current!;
    root.focus();
    const handler = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        closeRef.current();
      }
      if (e.key === "Tab") {
        const nodes = Array.from(
          root.querySelectorAll<HTMLElement>(
            'button:not(:disabled),input:not(:disabled),select:not(:disabled),textarea:not(:disabled),[tabindex="0"]',
          ),
        );
        if (!nodes.length) {
          e.preventDefault();
          return;
        }
        const first = nodes[0],
          last = nodes[nodes.length - 1];
        if (
          e.shiftKey &&
          (document.activeElement === first || document.activeElement === root)
        ) {
          e.preventDefault();
          last.focus();
        } else if (
          !e.shiftKey &&
          (document.activeElement === last || document.activeElement === root)
        ) {
          e.preventDefault();
          first.focus();
        }
      }
    };
    root.addEventListener("keydown", handler);
    return () => {
      root.removeEventListener("keydown", handler);
      previous?.focus();
    };
  }, []);
  return (
    <div className="scrim">
      <div
        ref={ref}
        tabIndex={-1}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className={`modal ${wide ? "wide" : ""} ${cardOrigin ? "card-modal" : ""} ${className}`}
        onPointerMove={backdrop ? (event) => {
          const bounds = event.currentTarget.getBoundingClientRect();
          event.currentTarget.style.setProperty("--scene-dx", `${((event.clientX - bounds.left) / bounds.width - .5) * 16}px`);
          event.currentTarget.style.setProperty("--scene-dy", `${((event.clientY - bounds.top) / bounds.height - .5) * 10}px`);
          event.currentTarget.style.setProperty("--scene-x", `${(event.clientX - bounds.left) / bounds.width * 100}%`);
          event.currentTarget.style.setProperty("--scene-y", `${(event.clientY - bounds.top) / bounds.height * 100}%`);
        } : undefined}
        data-card-morph={cardOrigin ? "true" : undefined}
      >
        {backdrop}
        {cardOrigin && <div className="card-morph-cover" aria-hidden="true">
          {cardOrigin.cover && <img src={cardOrigin.cover} alt="" />}
          <strong>{cardOrigin.title}</strong>
        </div>}
        <header className="modal-header">
          {headerArt}
          {cardOrigin?.cover ? <img className="modal-game-cover" src={cardOrigin.cover} alt="" /> : <span className="mu-mark">MU</span>}
          <h2>{title}</h2>
          <button
            className="icon-button close"
            aria-label="关闭"
            onClick={onClose}
          >
            <X size={22} />
          </button>
        </header>
        <div className="modal-body">{children}</div>
        {footer && <footer className="modal-footer">{footer}</footer>}
      </div>
    </div>
  );
}
export function Loading({ text = "正在读取…" }: { text?: string }) {
  return (
    <div className="loading" role="status">
      <LoaderCircle className="spin" size={22} />
      <span>{text}</span>
      <span className="scan-lattice" aria-hidden="true">{Array.from({ length: 12 }, (_, i) => <i key={i} style={{ animationDelay: `${i * 80}ms` }} />)}</span>
      <div className="progress indeterminate">
        <i />
      </div>
    </div>
  );
}
export function Empty({
  title,
  children,
}: {
  title: string;
  children?: ReactNode;
}) {
  return (
    <div className="empty">
      <h3>{title}</h3>
      {children}
    </div>
  );
}
export function Progress({
  percent,
  unknown = false,
}: {
  percent: number;
  unknown?: boolean;
}) {
  return (
    <div
      role="progressbar"
      aria-label="操作进度"
      aria-valuenow={unknown ? undefined : percent}
      aria-valuemin={0}
      aria-valuemax={100}
      className={`progress ${unknown ? "indeterminate" : ""}`}
    >
      <i style={unknown ? undefined : { width: `${percent}%` }} />
    </div>
  );
}
