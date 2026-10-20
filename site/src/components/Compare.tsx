import { useCallback, useRef, useState, type CSSProperties, type PointerEvent } from "react";
import { asset, type Backdrop } from "../content";

// The before/after comparer, the gesture the app itself is built around: the result is drawn
// over the original and clipped at the divider, which follows a drag anywhere on the image.
// A range input lies over the whole frame, invisible, so the keyboard (arrows, Home, End) and a
// screen reader move the same divider a pointer does, and the page works before hydration as a
// still pair at 50%.
export function Compare({
  before,
  after,
  beforeLabel,
  afterLabel,
  width,
  height,
  label,
  hint,
  backdrop,
  eager = false,
}: {
  before: string;
  after: string;
  beforeLabel: string;
  afterLabel: string;
  width: number;
  height: number;
  label: string;
  hint?: string;
  backdrop?: Backdrop;
  eager?: boolean;
}) {
  const [position, setPosition] = useState(50);
  const [touched, setTouched] = useState(false);
  const frame = useRef<HTMLDivElement>(null);
  const dragging = useRef(false);

  const follow = useCallback((clientX: number) => {
    const box = frame.current?.getBoundingClientRect();
    if (!box || box.width === 0) return;
    const pct = ((clientX - box.left) / box.width) * 100;
    setPosition(Math.min(100, Math.max(0, pct)));
    setTouched(true);
  }, []);

  const down = (e: PointerEvent<HTMLDivElement>) => {
    dragging.current = true;
    e.currentTarget.setPointerCapture(e.pointerId);
    follow(e.clientX);
  };
  const move = (e: PointerEvent<HTMLDivElement>) => {
    if (dragging.current) follow(e.clientX);
  };
  const up = () => {
    dragging.current = false;
  };

  const style = { "--pos": `${position}%`, aspectRatio: `${width} / ${height}` } as CSSProperties;

  return (
    <div
      ref={frame}
      className={`compare${touched ? " touched" : ""}`}
      style={style}
      onPointerDown={down}
      onPointerMove={move}
      onPointerUp={up}
      onPointerCancel={up}
    >
      <img className="compare-img" src={asset(before)} alt={beforeLabel} width={width} height={height} loading={eager ? "eager" : "lazy"} draggable={false} />
      {/* The result's layer carries the backdrop, so a cut-out's transparent pixels show the
          chosen ground rather than the original photo underneath. */}
      <div className={`compare-after${backdrop ? ` with-backdrop backdrop-${backdrop}` : ""}`}>
        <img className="compare-img" src={asset(after)} alt={afterLabel} width={width} height={height} loading={eager ? "eager" : "lazy"} draggable={false} />
      </div>
      <span className="compare-tag compare-tag-before">{beforeLabel}</span>
      <span className="compare-tag compare-tag-after">{afterLabel}</span>
      <div className="compare-line" aria-hidden="true">
        <span className="compare-knob">
          <svg viewBox="0 0 24 24" width="22" height="22">
            <path d="M9 6 L3 12 L9 18 M15 6 L21 12 L15 18" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
        </span>
      </div>
      {hint && <span className="compare-hint" aria-hidden="true">{hint}</span>}
      <input
        className="compare-range"
        type="range"
        min={0}
        max={100}
        step={0.5}
        value={position}
        aria-label={label}
        aria-valuetext={`${Math.round(position)}% ${afterLabel}`}
        onChange={(e) => {
          setPosition(Number(e.target.value));
          setTouched(true);
        }}
      />
    </div>
  );
}
