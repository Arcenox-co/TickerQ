/** Parse one SSE event. Server errors must reach the chat error UI. */
export function parseAssistantEvent(json: string): { type: string; data: string } {
  const event = JSON.parse(json) as { type: string; data: string };
  if (!event || typeof event.type !== "string" || typeof event.data !== "string")
    throw new Error("Invalid assistant response.");
  if (event.type === "error") throw new Error(event.data || "Assistant request failed.");
  return event;
}
