import type { BrainProvider, Completion, CompletionInput, Embedding } from "./types";
import { ProviderError } from "./types";

/**
 * Optional adapter: any /v1/chat/completions + /v1/embeddings server (OpenAI, Groq, OpenRouter,
 * Ollama, LM Studio). Stub until M5.
 */
export class OpenAiCompatibleProvider implements BrainProvider {
  readonly id = "openai-compatible";

  constructor(
    private readonly apiKey: string,
    private readonly baseUrl: string,
    readonly thinkModel: string,
    readonly embedModel: string,
  ) {
    if (!baseUrl) throw new ProviderError("OPENAI_BASE_URL is not set", 501, "unsupported");
  }

  complete(_input: CompletionInput): Promise<Completion> {
    return Promise.reject(
      new ProviderError("openai-compatible provider not implemented yet", 501, "unsupported"),
    );
  }

  embed(_texts: string[]): Promise<Embedding> {
    return Promise.reject(
      new ProviderError("openai-compatible provider not implemented yet", 501, "unsupported"),
    );
  }
}
