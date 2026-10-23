const PRESETS = [
  { label: "Every minute", value: "* * * * *" },
  { label: "Every 5 minutes", value: "*/5 * * * *" },
  { label: "Every hour", value: "0 * * * *" },
  { label: "Every day at midnight", value: "0 0 * * *" },
  { label: "Every Monday 9am", value: "0 9 * * 1" },
];

export function CronSchedulePresets({ onSelect }: { onSelect: (expression: string) => void }) {
  return (
    <select
      aria-label="Cron schedule presets"
      value=""
      onChange={(event) => {
        if (event.target.value) onSelect(event.target.value);
      }}
      className="h-5 w-24 max-w-full shrink-0 rounded-md border border-border bg-surface-2 px-2 text-[10px] leading-none text-foreground/80 hover:border-primary/50 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
    >
      <option value="">Presets…</option>
      {PRESETS.map((preset) => (
        <option key={preset.value} value={preset.value}>{preset.label}</option>
      ))}
    </select>
  );
}
