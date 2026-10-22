import { useEffect, useState } from "react";
import { FilePenLine, LayoutTemplate, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { SavedTickerDefinitionKind } from "@/services/api-types";

export interface SaveDefinitionValues {
  name: string;
  description: string;
  includeRequestData: boolean;
  executionTime: string;
}

export function SaveDefinitionDialog({
  open,
  kind,
  initialName = "",
  initialDescription = "",
  initialIncludeRequestData = true,
  initialExecutionTime = "",
  editing = false,
  pending = false,
  validateName,
  onOpenChange,
  onSave,
}: {
  open: boolean;
  kind: SavedTickerDefinitionKind;
  initialName?: string;
  initialDescription?: string;
  initialIncludeRequestData?: boolean;
  initialExecutionTime?: string;
  editing?: boolean;
  pending?: boolean;
  validateName?: (name: string) => string | null;
  onOpenChange: (open: boolean) => void;
  onSave: (values: SaveDefinitionValues) => void | Promise<unknown>;
}) {
  const [name, setName] = useState(initialName);
  const [description, setDescription] = useState(initialDescription);
  const [includeRequestData, setIncludeRequestData] = useState(initialIncludeRequestData);
  const [executionTime, setExecutionTime] = useState(initialExecutionTime);

  useEffect(() => {
    if (!open) return;
    setName(initialName);
    setDescription(initialDescription);
    setIncludeRequestData(initialIncludeRequestData);
    setExecutionTime(initialExecutionTime);
  }, [open, initialName, initialDescription, initialIncludeRequestData, initialExecutionTime]);

  const isDraft = kind === "Draft";
  const nameError = name.trim() ? validateName?.(name) ?? null : null;
  return (
    <Dialog open={open} onOpenChange={pending ? undefined : onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <div className="mb-1 flex h-9 w-9 items-center justify-center rounded-lg bg-primary/10 text-primary">
            {isDraft ? <FilePenLine className="h-4 w-4" /> : <LayoutTemplate className="h-4 w-4" />}
          </div>
          <DialogTitle>{isDraft ? "Save time-ticker draft" : editing ? "Edit template" : "Save as template"}</DialogTitle>
          <DialogDescription>
            {isDraft
              ? "Keep this configuration without scheduling it. You can edit or activate it later."
              : editing
                ? "Update this reusable workflow for future schedules. Existing jobs created from it are unchanged."
                : "Reuse this workflow when creating future schedules."}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4 py-1">
          <div className="space-y-1.5">
            <Label htmlFor="saved-definition-name" className="text-xs">Name</Label>
            <Input
              id="saved-definition-name"
              value={name}
              maxLength={200}
              autoFocus
              placeholder={isDraft ? "April invoice run" : "Invoice processing workflow"}
              aria-invalid={!!nameError}
              onChange={event => setName(event.target.value)}
            />
            {nameError && <p className="text-[11px] text-status-error">{nameError}</p>}
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="saved-definition-description" className="text-xs">Description</Label>
            <Input
              id="saved-definition-description"
              value={description}
              maxLength={2000}
              placeholder="Optional context for your team"
              onChange={event => setDescription(event.target.value)}
            />
          </div>
          {isDraft && (
            <div className="space-y-1.5">
              <Label htmlFor="saved-definition-execution-time" className="text-xs">Scheduled for</Label>
              <Input
                id="saved-definition-execution-time"
                type="datetime-local"
                step={1}
                value={executionTime}
                onChange={event => setExecutionTime(event.target.value)}
              />
              <p className="text-[11px] leading-4 text-muted-foreground">
                Copied from the job form. You can correct it before saving; leave it empty to run when the draft is activated.
              </p>
            </div>
          )}
          <label className="flex cursor-pointer items-start gap-2 rounded-lg border border-border bg-surface-0/40 p-3">
            <Checkbox
              checked={includeRequestData}
              onCheckedChange={checked => setIncludeRequestData(checked === true)}
              className="mt-0.5"
            />
            <span>
              <span className="block text-xs font-medium">Include request payloads</span>
              <span className="mt-0.5 block text-[11px] leading-4 text-muted-foreground">
                Turn this off when payloads can contain customer data or secrets. The job structure and settings are still saved.
              </span>
            </span>
          </label>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={pending}>Cancel</Button>
          <Button
            variant="gradient"
            disabled={pending || !name.trim() || !!nameError}
            onClick={() => void onSave({
              name: name.trim(),
              description: description.trim(),
              includeRequestData,
              executionTime,
            })}
          >
            {pending && <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />}
            {isDraft ? "Save draft" : editing ? "Save changes" : "Save template"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
