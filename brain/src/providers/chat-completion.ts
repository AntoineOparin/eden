/** Reader for the OpenAI-style `chat.completion` shape, shared by Workers AI and openai-compatible. */

interface ChatCompletionLike {
  response?: unknown;
  choices?: Array<{
    message?: { content?: unknown; reasoning_content?: unknown; reasoning?: unknown };
  }>;
  usage?: { prompt_tokens?: unknown; completion_tokens?: unknown };
}

export interface ReadCompletion {
  /** "" when the completion carried no usable text. */
  text: string;
  inputTokens: number;
  outputTokens: number;
}

export const asInt = (v: unknown): number =>
  typeof v === "number" && Number.isFinite(v) ? Math.round(v) : 0;

/**
 * Takes the first non-empty of `content`, `reasoning_content`, `reasoning`, legacy `response`.
 * Observed live on Workers AI (2026-11-06): with thinking disabled the reasoning parser can file the
 * whole answer under `reasoning_content` / `reasoning` and leave `content` null. Ollama and Groq
 * use the same fields. The JSON is validated afterwards either way.
 */
export function readChatCompletion(raw: unknown): ReadCompletion {
  const out = (raw ?? {}) as ChatCompletionLike;
  const msg = out.choices?.[0]?.message;
  const content = firstText(msg?.content, msg?.reasoning_content, msg?.reasoning, out.response);
  const text =
    typeof content === "string" ? content : content === undefined ? "" : JSON.stringify(content);
  return {
    text: text === "null" ? "" : text,
    inputTokens: asInt(out.usage?.prompt_tokens),
    outputTokens: asInt(out.usage?.completion_tokens),
  };
}

function firstText(...candidates: unknown[]): unknown {
  for (const c of candidates) {
    if (typeof c === "string" && c.trim().length > 0) return c;
    if (c !== null && c !== undefined && typeof c === "object") return c;
  }
  return undefined;
}

export function messageOf(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}

export function isNumberArray(v: unknown): v is number[] {
  return Array.isArray(v) && v.length > 0 && v.every((x) => typeof x === "number");
}
