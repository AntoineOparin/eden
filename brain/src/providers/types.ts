export interface ProviderUsage {
  inputTokens: number;
  outputTokens: number;
  model: string;
}

export interface CompletionInput {
  system: string;
  user: string;
  maxTokens: number;
  temperature: number;
  /** Optional JSON Schema the provider may pass as response_format. Output is validated regardless. */
  jsonSchema?: Record<string, unknown>;
}

export interface Completion {
  text: string;
  usage: ProviderUsage;
}

export interface Embedding {
  vectors: number[][];
  usage: ProviderUsage;
}

/** One LLM backend. Stateless; constructed per request from env. Throws ProviderError only. */
export interface BrainProvider {
  readonly id: string;
  readonly thinkModel: string;
  readonly embedModel: string;
  complete(input: CompletionInput): Promise<Completion>;
  embed(texts: string[]): Promise<Embedding>;
}

export class ProviderError extends Error {
  constructor(
    message: string,
    readonly status: number = 502,
    readonly code: "provider_error" | "unsupported" = "provider_error",
  ) {
    super(message);
    this.name = "ProviderError";
  }
}
