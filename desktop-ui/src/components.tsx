import { useEffect, useRef, type ReactNode } from "react";
import { X, LoaderCircle } from "lucide-react";
export function Button({
  children,
  primary = false,
  ...props
}: React.ButtonHTMLAttributes<HTMLButtonElement> & { primary?: boolean }) {
  return (
    <button
      {...props}
      className={`button ${primary ? "primary" : ""} ${props.className || ""}`}
    >
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
}: {
  title: string;
  children: ReactNode;
  footer?: ReactNode;
  onClose: () => void;
  wide?: boolean;
}) {
  const ref = useRef<HTMLDivElement>(null);
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
        className={`modal ${wide ? "wide" : ""}`}
      >
        <header className="modal-header">
          <span className="mu-mark">MU</span>
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
