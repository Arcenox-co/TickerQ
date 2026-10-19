import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";

/**
 * Markdown renderer for assistant replies, styled for the compact chat
 * bubble. react-markdown renders to React elements (never raw HTML), so
 * model output can't inject markup. Element styles are tuned inline instead
 * of a typography plugin to keep the bundle lean.
 */
export function ChatMarkdown({ content }: { content: string }) {
  return (
    <ReactMarkdown
      remarkPlugins={[remarkGfm]}
      components={{
        p: ({ children }) => <p className="mb-2 last:mb-0 leading-relaxed">{children}</p>,
        strong: ({ children }) => <strong className="font-semibold text-foreground">{children}</strong>,
        em: ({ children }) => <em className="italic">{children}</em>,
        ul: ({ children }) => <ul className="mb-2 last:mb-0 list-disc space-y-1 pl-5">{children}</ul>,
        ol: ({ children }) => <ol className="mb-2 last:mb-0 list-decimal space-y-1 pl-5">{children}</ol>,
        li: ({ children }) => <li className="leading-relaxed">{children}</li>,
        h1: ({ children }) => <p className="mb-1.5 text-[13.5px] font-semibold">{children}</p>,
        h2: ({ children }) => <p className="mb-1.5 text-[13.5px] font-semibold">{children}</p>,
        h3: ({ children }) => <p className="mb-1 font-semibold">{children}</p>,
        h4: ({ children }) => <p className="mb-1 font-semibold">{children}</p>,
        a: ({ href, children }) => (
          <a
            href={href}
            target="_blank"
            rel="noreferrer noopener"
            className="text-primary underline underline-offset-2 hover:opacity-80"
          >
            {children}
          </a>
        ),
        code: ({ className, children }) => {
          // Block code arrives wrapped in <pre>; inline code has no language class.
          const isBlock = /language-/.test(className ?? "");
          return isBlock ? (
            <code className="font-mono text-[11.5px]">{children}</code>
          ) : (
            <code className="rounded bg-surface-0/80 px-1 py-0.5 font-mono text-[11.5px]">
              {children}
            </code>
          );
        },
        pre: ({ children }) => (
          <pre className="mb-2 last:mb-0 overflow-x-auto rounded-lg bg-surface-0/80 p-3 scrollbar-thin">
            {children}
          </pre>
        ),
        table: ({ children }) => (
          <div className="mb-2 last:mb-0 overflow-x-auto scrollbar-thin">
            <table className="w-full border-collapse text-[12px]">{children}</table>
          </div>
        ),
        th: ({ children }) => (
          <th className="border-b border-border px-2 py-1 text-left font-semibold">{children}</th>
        ),
        td: ({ children }) => (
          <td className="border-b border-border/40 px-2 py-1 align-top">{children}</td>
        ),
        blockquote: ({ children }) => (
          <blockquote className="mb-2 last:mb-0 border-l-2 border-border pl-3 text-muted-foreground">
            {children}
          </blockquote>
        ),
        hr: () => <hr className="my-2 border-border" />,
      }}
    >
      {content}
    </ReactMarkdown>
  );
}
