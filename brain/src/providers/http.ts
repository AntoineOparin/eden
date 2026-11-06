import { messageOf } from "./chat-completion";
import { ProviderError } from "./types";

/** Upper bound on one upstream call. Waiting does not count against the Worker CPU budget. */
export const UPSTREAM_TIMEOUT_MS = 30_000;

/**
 * POSTs JSON to an upstream LLM API through the global fetch and returns the parsed body.
 * Every failure becomes a ProviderError (502). Messages never include request headers, so API
 * keys cannot leak into the error envelope.
 */
export async function postJson(
  label: string,
  url: string,
  headers: Record<string, string>,
  body: unknown,
): Promise<unknown> {
  let res: Response;
  try {
    res = await fetch(url, {
      method: "POST",
      headers: { "content-type": "application/json", ...headers },
      body: JSON.stringify(body),
      signal: AbortSignal.timeout(UPSTREAM_TIMEOUT_MS),
    });
  } catch (e) {
    throw new ProviderError(`${label} request failed: ${messageOf(e)}`);
  }
  const text = await res.text();
  let json: unknown;
  try {
    json = JSON.parse(text) as unknown;
  } catch {
    json = undefined;
  }
  if (!res.ok) {
    const detail = upstreamMessage(json) ?? text.slice(0, 200);
    throw new ProviderError(`${label} returned HTTP ${res.status}: ${detail}`);
  }
  if (json === undefined) throw new ProviderError(`${label} returned a non-JSON body`);
  return json;
}

/** Both Anthropic (`{ type: "error", error: { type, message } }`) and OpenAI put the text at error.message. */
function upstreamMessage(json: unknown): string | undefined {
  if (typeof json !== "object" || json === null) return undefined;
  const err = (json as { error?: unknown }).error;
  if (typeof err === "string") return err.slice(0, 300);
  if (typeof err !== "object" || err === null) return undefined;
  const msg = (err as { message?: unknown }).message;
  return typeof msg === "string" ? msg.slice(0, 300) : undefined;
}
