import type { BrainProvider, Completion, CompletionInput, Embedding } from "./types";
import { ProviderError } from "./types";

/**
 * Optional adapter: Anthropic Messages API with the player's key routed through the Worker.
 * Stub until M5 (docs/specs/20-mind.md / 30-brain-worker.md). Model ids and pricing are to be
 * checked against the live docs when implemented, not from memory.
 */
export class AnthropicProvider implements BrainProvider {
  readonly id = "anthropic";
  readonly embedModel = "none";

  constructor(
    private readonly apiKey: string,
    readonly thinkModel: string = "claude-haiku-5-5",
  ) {
    if (!apiKey) throw new ProviderError("ANTHROPIC_API_KEY is not set", 501, "unsupported");
  }

  complete(_input: CompletionInput): Promise<Completion> {
    return Promise.reject(
      new ProviderError("anthropic provider not implemented yet", 501, "unsupported"),
    );
  }

  embed(_texts: string[]): Promise<Embedding> {
    return Promise.reject(
      new ProviderError("anthropic has no embeddings endpoint", 501, "unsupported"),
    );
  }
}
