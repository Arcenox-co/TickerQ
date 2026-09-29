import { useEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";

/**
 * Right-docked side panel with a draggable left edge to resize. Renders via a
 * portal with a click-to-close backdrop. Shared by the time-ticker detail panel
 * and the cron-occurrence detail panel.
 */
export function ResizableSidePanel({
  open,
  initialWidth = 640,
  minWidth = 400,
  maxWidth = 1200,
  onClose,
  children,
}: {
  open: boolean;
  initialWidth?: number;
  minWidth?: number;
  maxWidth?: number;
  onClose: () => void;
  children: ReactNode;
}) {
  const [width, setWidth] = useState(initialWidth);
  const dragging = useRef(false);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  function startResize(e: React.MouseEvent) {
    e.preventDefault();
    dragging.current = true;
    const startX = e.clientX;
    const startWidth = width;
    document.body.style.cursor = "col-resize";
    document.body.style.userSelect = "none";
    const onMove = (ev: MouseEvent) => {
      if (!dragging.current) return;
      const next = startWidth + (startX - ev.clientX);
      setWidth(Math.min(maxWidth, Math.max(minWidth, next)));
    };
    const onUp = () => {
      dragging.current = false;
      document.body.style.cursor = "";
      document.body.style.userSelect = "";
      window.removeEventListener("mousemove", onMove);
      window.removeEventListener("mouseup", onUp);
    };
    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup", onUp);
  }

  if (!open) return null;

  return createPortal(
    <div className="fixed inset-0 z-40 flex justify-end">
      <div
        className="absolute inset-0 bg-background/40 backdrop-blur-[1px]"
        onClick={onClose}
      />
      <div
        className="relative h-full bg-surface-0 border-l border-border shadow-2xl flex flex-col overflow-hidden"
        style={{ width }}
      >
        <div
          onMouseDown={startResize}
          className="absolute left-0 top-0 h-full w-1.5 cursor-col-resize bg-transparent hover:bg-primary/30 transition-colors z-10"
          aria-hidden
        />
        <div className="flex-1 overflow-y-auto scrollbar-thin">{children}</div>
      </div>
    </div>,
    document.body
  );
}
