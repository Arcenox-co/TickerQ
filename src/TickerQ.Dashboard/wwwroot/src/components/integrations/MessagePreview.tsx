import type { ReactNode } from "react";

type Node = Record<string, unknown>;
const object = (value: unknown): Node =>
  value && typeof value === "object" && !Array.isArray(value)
    ? (value as Node)
    : {};
const list = (value: unknown): unknown[] => (Array.isArray(value) ? value : []);
const text = (value: unknown) => (typeof value === "string" ? value : "");

// Local text-only rendering: never execute HTML, load images, or follow payload URLs.
export function MessagePreview({
  provider,
  payload,
}: {
  provider: string;
  payload: string;
}) {
  const root = object(JSON.parse(payload));
  if (provider !== "slack" && provider !== "teams")
    return (
      <pre className="max-h-80 overflow-auto p-4 font-mono text-[11px] leading-relaxed">
        {payload}
      </pre>
    );
  function actions(items: unknown[]) {
    return (
      <div className="flex flex-wrap gap-2 pt-1">
        {items.map((item, i) => {
          const value = object(item);
          const label = text(value.title) || text(object(value.text).text);
          return label ? (
            <span
              key={i}
              className="rounded-md border border-border bg-surface-1 px-3 py-1.5 text-xs font-medium"
            >
              {label}
            </span>
          ) : null;
        })}
      </div>
    );
  }
  function fields(items: unknown[], teams = false) {
    return (
      <dl className="grid grid-cols-2 gap-x-5 gap-y-3">
        {items.map((item, i) => {
          const value = object(item);
          const content = text(value.text).split("\n");
          return (
            <div key={i} className="min-w-0">
              <dt className="text-[10px] font-medium uppercase tracking-wider text-muted-foreground">
                {teams ? text(value.title) : content.shift()}
              </dt>
              <dd className="mt-1 whitespace-pre-wrap break-words text-xs">
                {teams ? text(value.value) : content.join("\n")}
              </dd>
            </div>
          );
        })}
      </dl>
    );
  }
  let content: ReactNode;
  if (provider === "slack") {
    const blocks = [
      ...list(root.blocks),
      ...list(root.attachments).flatMap((item) => list(object(item).blocks)),
    ];
    content = blocks.length ? (
      blocks.map((item, i) => {
        const block = object(item);
        const value = text(object(block.text).text);
        switch (block.type) {
          case "header":
            return (
              <h4 key={i} className="text-base font-semibold leading-snug">
                {value}
              </h4>
            );
          case "section":
            return (
              <div key={i} className="space-y-3">
                {value && (
                  <p className="whitespace-pre-wrap break-words text-xs leading-relaxed">
                    {value}
                  </p>
                )}
                {list(block.fields).length > 0 && fields(list(block.fields))}
              </div>
            );
          case "context":
            return (
              <p
                key={i}
                className="break-words border-t border-border pt-3 text-[10px] leading-relaxed text-muted-foreground"
              >
                {list(block.elements)
                  .map((e) => text(object(e).text))
                  .join(" · ")}
              </p>
            );
          case "divider":
            return <hr key={i} className="border-border" />;
          case "actions":
            return <div key={i}>{actions(list(block.elements))}</div>;
          default:
            return null;
        }
      })
    ) : (
      <p className="whitespace-pre-wrap break-words text-xs">
        {text(root.text)}
      </p>
    );
  } else {
    content = list(root.attachments).map((item, i) => {
      const card = object(object(item).content);
      return (
        <div key={i} className="space-y-4">
          {list(card.body).map((entry, j) => {
            const block = object(entry);
            if (block.type === "FactSet")
              return <div key={j}>{fields(list(block.facts), true)}</div>;
            if (block.type !== "TextBlock") return null;
            const color =
              block.color === "Good"
                ? "text-status-healthy"
                : block.color === "Attention"
                  ? "text-status-error"
                  : block.color === "Warning"
                    ? "text-status-warning"
                    : "";
            return (
              <p
                key={j}
                className={`whitespace-pre-wrap break-words leading-relaxed ${block.weight === "Bolder" ? "text-base font-semibold" : block.isSubtle ? "text-[10px] text-muted-foreground" : "text-xs"} ${block.separator ? "border-t border-border pt-3" : ""} ${color}`}
              >
                {text(block.text)}
              </p>
            );
          })}
          {actions(list(card.actions))}
        </div>
      );
    });
  }
  return (
    <div className="space-y-4 p-4" aria-label="Message layout preview">
      {content}
    </div>
  );
}
