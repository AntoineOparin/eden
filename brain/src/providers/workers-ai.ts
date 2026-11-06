import type { BrainProvider, Completion, CompletionInput, Embedding } from "./types";
import { ProviderError } from "./types";

/** Minimal view of the AI binding so the adapter is not tied to the generated per-model overloads. */
export interface AiRunner {
  run(model: string, inputs: Record<string, unknown>): Promise<unknown>;
}

interface ChatCompletionLike {
  response?: unknown;
  choices?: Array<{
    message?: { content?: unknown; reasoning_content?: unknown; reasoning?: unknown };
  }>;
  usage?: { prompt_tokens?: unknown; completion_tokens?: unknown };
}

interface EmbeddingLike {
  data?: unknown;
  shape?: unknown;
  usage?: { prompt_tokens?: unknown };
}

const asInt = (v: unknown): number =>
  typeof v === "number" && Number.isFinite(v) ? Math.round(v) : 0;

/** Default provider: Cloudflare Workers AI through the `AI` binding. Free tier, no key. */
export class WorkersAiProvider implements BrainProvider {
  readonly id = "workers-ai";

  constructor(
    private readonly ai: AiRunner,
    readonly thinkModel: string,
    readonly embedModel: string,
  ) {}

  async complete(input: CompletionInput): Promise<Completion> {
    const payload: Record<string, unknown> = {
      messages: [
        { role: "system", content: input.system },
        { role: "user", content: input.user },
      ],
      max_tokens: input.maxTokens,
      temperature: input.temperature,
    };
    if (input.jsonSchema) {
      payload.response_format = { type: "json_schema", json_schema: input.jsonSchema };
    }
    // Reasoning models (Qwen3, Gemma 4) otherwise spend the whole token budget inside <think>
    // and never emit the JSON. The sim wants a decision, not a chain of thought.
    if (supportsThinkingToggle(this.thinkModel)) {
      payload.chat_template_kwargs = { enable_thinking: false };
    }

    let raw: unknown;
    try {
      raw = await this.ai.run(this.thinkModel, payload);
    } catch (e) {
      throw new ProviderError(`Workers AI ${this.thinkModel} failed: ${messageOf(e)}`);
    }

    const out = (raw ?? {}) as ChatCompletionLike;
    const msg = out.choices?.[0]?.message;
    // Observed live (2026-11-06): with thinking disabled, Workers AI's reasoning parser can file the
    // whole answer under `reasoning_content` / `reasoning` and leave `content` null. Take the first
    // non-empty field; the JSON is validated afterwards either way.
    const content = firstText(msg?.content, msg?.reasoning_content, msg?.reasoning, out.response);
    const text =
      typeof content === "string" ? content : content === undefined ? "" : JSON.stringify(content);
    if (!text || text === "null") {
      console.warn("workers-ai unexpected completion shape", JSON.stringify(raw).slice(0, 1200));
      throw new ProviderError(`Workers AI ${this.thinkModel} returned no content`);
    }

    return {
      text,
      usage: {
        inputTokens: asInt(out.usage?.prompt_tokens),
        outputTokens: asInt(out.usage?.completion_tokens),
        model: this.thinkModel,
      },
    };
  }

  async embed(texts: string[]): Promise<Embedding> {
    let raw: unknown;
    try {
      raw = await this.ai.run(this.embedModel, { text: texts });
    } catch (e) {
      throw new ProviderError(`Workers AI ${this.embedModel} failed: ${messageOf(e)}`);
    }
    const out = (raw ?? {}) as EmbeddingLike;
    const data = out.data;
    if (!Array.isArray(data) || data.length !== texts.length || !data.every(isNumberArray)) {
      throw new ProviderError(`Workers AI ${this.embedModel} returned an unexpected shape`);
    }
    // Embedding usage is not always reported; approximate from input length (≈ 4 chars/token).
    const reported = asInt(out.usage?.prompt_tokens);
    const inputTokens = reported || Math.ceil(texts.reduce((n, t) => n + t.length, 0) / 4);
    return { vectors: data, usage: { inputTokens, outputTokens: 0, model: this.embedModel } };
  }
}

function firstText(...candidates: unknown[]): unknown {
  for (const c of candidates) {
    if (typeof c === "string" && c.trim().length > 0) return c;
    if (c !== null && c !== undefined && typeof c === "object") return c;
  }
  return undefined;
}

/** Models whose chat template accepts `enable_thinking` (Workers AI passes chat_template_kwargs through). */
export function supportsThinkingToggle(model: string): boolean {
  return /qwen3|gemma-4|glm-4/i.test(model);
}

function isNumberArray(v: unknown): v is number[] {
  return Array.isArray(v) && v.length > 0 && v.every((x) => typeof x === "number");
}

function messageOf(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}
