import { asInt, isNumberArray, readChatCompletion } from "./chat-completion";
import { postJson } from "./http";
import type { BrainProvider, Completion, CompletionInput, Embedding } from "./types";
import { ProviderError } from "./types";

export const DEFAULT_OPENAI_MODEL = "gpt-5-mini";
export const DEFAULT_OPENAI_EMBED_MODEL = "text-embedding-3-small";
/** Extra completion budget for reasoning models, whose hidden reasoning counts toward the cap. */
export const REASONING_HEADROOM_TOKENS = 2000;

interface EmbeddingsLike {
  data?: Array<{ embedding?: unknown; index?: unknown }>;
  usage?: { prompt_tokens?: unknown };
}

/**
 * Optional adapter: any server with /chat/completions and /embeddings (OpenAI, Groq, OpenRouter,
 * Ollama, LM Studio). OPENAI_BASE_URL is the API root including the version segment, e.g.
 * https://api.openai.com/v1 or http://localhost:11434/v1. OPENAI_API_KEY may be empty for local
 * servers.
 */
export class OpenAiCompatibleProvider implements BrainProvider {
  readonly id = "openai-compatible";
  private readonly root: string;

  constructor(
    private readonly apiKey: string,
    baseUrl: string,
    readonly thinkModel: string = DEFAULT_OPENAI_MODEL,
    readonly embedModel: string = DEFAULT_OPENAI_EMBED_MODEL,
  ) {
    if (!baseUrl) throw new ProviderError("OPENAI_BASE_URL is not set", 501, "unsupported");
    if (!/^https?:\/\//i.test(baseUrl))
      throw new ProviderError("OPENAI_BASE_URL must start with http(s)://", 501, "unsupported");
    this.root = baseUrl.replace(/\/+$/, "");
  }

  private headers(): Record<string, string> {
    return this.apiKey ? { authorization: `Bearer ${this.apiKey}` } : {};
  }

  async complete(input: CompletionInput): Promise<Completion> {
    const body: Record<string, unknown> = {
      model: this.thinkModel,
      messages: [
        { role: "system", content: input.system },
        { role: "user", content: input.user },
      ],
    };
    if (isOpenAiReasoningModel(this.thinkModel)) {
      // OpenAI reasoning models: `max_tokens` is deprecated and not accepted by o-series models;
      // `max_completion_tokens` includes reasoning tokens. Sampling stays at the model default.
      // (developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create, 2026-11-06)
      body.max_completion_tokens = input.maxTokens + REASONING_HEADROOM_TOKENS;
      body.reasoning_effort = "low";
    } else {
      // Most compatible servers (Ollama, LM Studio, Groq) still speak `max_tokens`.
      body.max_tokens = input.maxTokens;
      body.temperature = input.temperature;
    }
    if (input.jsonSchema) {
      body.response_format = {
        type: "json_schema",
        json_schema: { name: "eden_output", schema: input.jsonSchema, strict: false },
      };
    }

    const raw = await postJson(
      `openai-compatible ${this.thinkModel}`,
      `${this.root}/chat/completions`,
      this.headers(),
      body,
    );
    const { text, inputTokens, outputTokens } = readChatCompletion(raw);
    if (!text) throw new ProviderError(`openai-compatible ${this.thinkModel} returned no content`);
    return { text, usage: { inputTokens, outputTokens, model: this.thinkModel } };
  }

  async embed(texts: string[]): Promise<Embedding> {
    const raw = await postJson(
      `openai-compatible ${this.embedModel}`,
      `${this.root}/embeddings`,
      this.headers(),
      { model: this.embedModel, input: texts },
    );
    const out = (raw ?? {}) as EmbeddingsLike;
    const data = Array.isArray(out.data) ? out.data : [];
    // The API returns one item per input with its `index`; order by it rather than trusting position.
    const vectors = data
      .map((d, i) => ({ v: d.embedding, at: typeof d.index === "number" ? d.index : i }))
      .sort((a, b) => a.at - b.at)
      .map((d) => d.v);
    if (vectors.length !== texts.length || !vectors.every(isNumberArray)) {
      throw new ProviderError(`openai-compatible ${this.embedModel} returned an unexpected shape`);
    }
    const reported = asInt(out.usage?.prompt_tokens);
    const inputTokens = reported || Math.ceil(texts.reduce((n, t) => n + t.length, 0) / 4);
    return { vectors, usage: { inputTokens, outputTokens: 0, model: this.embedModel } };
  }
}

/** OpenAI o-series and GPT-5 family, bare or vendor-prefixed (e.g. OpenRouter's "openai/gpt-5-mini"). */
export function isOpenAiReasoningModel(model: string): boolean {
  return /(^|\/)(o\d|gpt-5)/i.test(model);
}
