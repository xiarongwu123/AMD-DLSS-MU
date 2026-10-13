import { useEffect, useState, type ReactNode } from "react";
import SpotlightCard from "./reactbits/SpotlightCard";
import ShinyText from "./reactbits/ShinyText";

export function useVisualEffects() {
  const [enabled, setEnabled] = useState(() => {
    try { return localStorage.getItem("mu-visual-effects") !== "off"; }
    catch { return true; }
  });
  const [reduced, setReduced] = useState(() =>
    window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? true);
  useEffect(() => {
    const media = window.matchMedia?.("(prefers-reduced-motion: reduce)");
    if (!media) return;
    const update = () => setReduced(media.matches);
    media.addEventListener("change", update);
    return () => media.removeEventListener("change", update);
  }, []);
  const change = (value: boolean) => {
    setEnabled(value);
    try { localStorage.setItem("mu-visual-effects", value ? "on" : "off"); } catch { /* optional */ }
  };
  return { enabled, active: enabled && !reduced, change };
}

export function InteractiveSurface({ active, dark, children, className = "" }: {
  active: boolean; dark: boolean; children: ReactNode; className?: string;
}) {
  return active ? <SpotlightCard className={className} theme={dark ? "dark" : "light"}
    spotlightColor={dark ? "#adff18" : "#3e800f"} intensity={0.12} borderGlow={0.5}
    spotlightSize={260} proximity={35} flare={false}>{children}</SpotlightCard>
    : <div className={className}>{children}</div>;
}

export function Shine({ active, children }: { active: boolean; children: ReactNode }) {
  return active ? <ShinyText color="#adff18" shineColor="#f4ffd9" speed={1.8} delay={3.5}
    shineWidth={18} glow={0} followPointer>{children}</ShinyText> : <span>{children}</span>;
}
