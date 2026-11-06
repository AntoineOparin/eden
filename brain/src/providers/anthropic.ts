import { asInt } from "./chat-completion";
import { postJson } from "./http";
import type { BrainProvider, Completion, CompletionInput, Embedding } from "./types";
import { ProviderError } from "./types";

// Everything below was checked against the live docs on 2026-11-06, not written from memory:
// - Model ids `claude-haiku-5-5`, `claude-sonnet-5-5`:
//   https://platform.claude.com/docs/en/about-claude/models/overview
// - Header `anthropic-version: 2026-10-21`, and that version rejects `temperature`/`top_p`/`top_k`
//   at any value with a 400: https://platform.claude.com/docs/en/api/migration-2026-10-21
// - Thinking is on by default; Haiku 5.5 turns it off with `{type:"disabled"}`, Sonnet 5.5 rejects
//   "disabled" and its lowest setting is `{type:"between_tools"}`:
//   https://platform.claude.com/docs/en/build-with-claude/thinking
// - JSON output via `output_config.format = {type:"json_schema", schema}`; no minLength/maxLength,
//   minimum/maximum or array bounds beyond minItems 0/1, and objects need additionalProperties
//   false: https://platform.claude.com/docs/en/build-with-claude/structured-outputs
// - Response `content[]` text blocks, `usage.input_tokens/output_tokens`, `stop_reason`:
//   https://platform.claude.com/docs/en/api/messages/create
// - Error body `{type:"error", error:{type, message}}`: https://platform.claude.com/docs/en/api/errors

export const ANTHROPIC_MESSAGES_URL = "https://api.anthropic.com/v1/messages";
export const ANTHROPIC_VERSION = "2026-10-21";
export const DEFAULT_ANTHROPIC_MODEL = "claude-haiku-5-5";

interface MessageLike {
  content?: Array<{ type?: unknown; text?: unknown }>;
  stop_reason?: unknown;
  usage?: {
    input_tokens?: unknown;
    output_tokens?: unknown;
    cache_creation_input_tokens?: unknown;
    cache_read_input_tokens?: unknown;
  };
}

/**
 * Optional adapter: Anthropic Messages API with the player's key (secret ANTHROPIC_API_KEY).
 * The sim wants a quick decision, so thinking is turned as low as each model allows.
 */
export class AnthropicProvider implements BrainProvider {
  readonly id = "anthropic";
  /** Anthropic has no embeddings endpoint; embed() answers 501. */
  readonly embedModel = "none";

  constructor(
    private readonly apiKey: string,
    readonly thinkModel: string = DEFAULT_ANTHROPIC_MODEL,
  ) {
    if (!apiKey) throw new ProviderError("ANTHROPIC_API_KEY is not set", 501, "unsupported");
  }

  async complete(input: CompletionInput): Promise<Completion> {
    // No `temperature`: API version 2026-10-21 rejects it on every model (input.temperature is
    // a Workers AI knob only).
    const body: Record<string, unknown> = {
      model: this.thinkModel,
      max_tokens: input.maxTokens,
      system: input.system,
      messages: [{ role: "user", content: input.user }],
    };
    const thinking = lowestThinking(this.thinkModel);
    if (thinking) body.thinking = thinking;
    if (input.jsonSchema) {
      body.output_config = {
        format: { type: "json_schema", schema: toStructuredOutputSchema(input.jsonSchema) },
      };
    }

    const raw = await postJson(
      `Anthropic ${this.thinkModel}`,
      ANTHROPIC_MESSAGES_URL,
      { "x-api-key": this.apiKey, "anthropic-version": ANTHROPIC_VERSION },
      body,
    );
    const msg = raw as MessageLike;
    if (msg.stop_reason === "refusal")
      throw new ProviderError(`Anthropic ${this.thinkModel} refused the request`);
    const text = (Array.isArray(msg.content) ? msg.content : [])
      .filter((b) => b.type === "text" && typeof b.text === "string")
      .map((b) => b.text as string)
      .join("");
    if (!text.trim()) {
      throw new ProviderError(
        `Anthropic ${this.thinkModel} returned no text (stop_reason ${String(msg.stop_reason)})`,
      );
    }
    // Total input = input_tokens + cache writes + cache reads (we do not cache, but count them).
    const u = msg.usage ?? {};
    return {
      text,
      usage: {
        inputTokens:
          asInt(u.input_tokens) +
          asInt(u.cache_creation_input_tokens) +
          asInt(u.cache_read_input_tokens),
        outputTokens: asInt(u.output_tokens),
        model: this.thinkModel,
      },
    };
  }

  embed(_texts: string[]): Promise<Embedding> {
    return Promise.reject(
      new ProviderError(
        "anthropic has no embeddings endpoint; use workers-ai or openai-compatible for /embed",
        501,
        "unsupported",
      ),
    );
  }
}

/** The lowest thinking setting each model accepts, or undefined to leave the default. */
export function lowestThinking(model: string): { type: string } | undefined {
  if (/haiku-5-5/.test(model)) return { type: "disabled" };
  if (/sonnet-5-5/.test(model)) return { type: "between_tools" };
  return undefined;
}

const UNSUPPORTED_KEYWORDS = new Set([
  "minLength",
  "maxLength",
  "minimum",
  "maximum",
  "exclusiveMinimum",
  "exclusiveMaximum",
  "multipleOf",
  "maxItems",
  "uniqueItems",
]);

/**
 * Copies a JSON Schema without the keywords structured outputs rejects. The bounds still hold:
 * the route validates every answer with Zod.
 */
export function toStructuredOutputSchema(schema: unknown): unknown {
  if (Array.isArray(schema)) return schema.map(toStructuredOutputSchema);
  if (typeof schema !== "object" || schema === null) return schema;
  const out: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(schema)) {
    if (UNSUPPORTED_KEYWORDS.has(k)) continue;
    if (k === "minItems" && typeof v === "number" && v > 1) continue;
    if (k === "enum" || k === "const")
      out[k] = v; // data, not sub-schemas
    else if (k === "properties" && typeof v === "object" && v !== null)
      // Keys here are property names, not keywords: keep them all, clean each sub-schema.
      out[k] = Object.fromEntries(
        Object.entries(v).map(([name, sub]) => [name, toStructuredOutputSchema(sub)]),
      );
    else out[k] = toStructuredOutputSchema(v);
  }
  return out;
}
