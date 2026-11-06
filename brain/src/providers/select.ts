import type { AppEnv } from "../env";
import { AnthropicProvider } from "./anthropic";
import { OpenAiCompatibleProvider } from "./openai-compatible";
import type { BrainProvider } from "./types";
import { ProviderError } from "./types";
import { WorkersAiProvider } from "./workers-ai";
import type { AiRunner } from "./workers-ai";

export const DEFAULT_THINK_MODEL = "@cf/google/gemma-4-26b-a4b-it";
export const DEFAULT_EMBED_MODEL = "@cf/baai/bge-m3";
export const PROVIDER_IDS = ["workers-ai", "anthropic", "openai-compatible"] as const;
export type ProviderId = (typeof PROVIDER_IDS)[number];

export function isProviderId(v: string | undefined): v is ProviderId {
  return v !== undefined && (PROVIDER_IDS as readonly string[]).includes(v);
}

/** Builds the provider for this request: env default, optionally overridden by X-Eden-Provider. */
export function selectProvider(env: AppEnv, override: string | undefined): BrainProvider {
  const id = override ?? env.BRAIN_PROVIDER ?? "workers-ai";
  if (!isProviderId(id)) throw new ProviderError(`unknown provider "${id}"`, 400, "unsupported");
  const thinkModel = env.THINK_MODEL ?? DEFAULT_THINK_MODEL;
  const embedModel = env.EMBED_MODEL ?? DEFAULT_EMBED_MODEL;
  switch (id) {
    case "workers-ai": {
      if (!env.AI) throw new ProviderError("AI binding missing", 500);
      return new WorkersAiProvider(env.AI as unknown as AiRunner, thinkModel, embedModel);
    }
    case "anthropic":
      // THINK_MODEL / EMBED_MODEL name Workers AI models, so other providers have their own vars.
      return new AnthropicProvider(env.ANTHROPIC_API_KEY ?? "", env.ANTHROPIC_MODEL || undefined);
    case "openai-compatible":
      return new OpenAiCompatibleProvider(
        env.OPENAI_API_KEY ?? "",
        env.OPENAI_BASE_URL ?? "",
        env.OPENAI_MODEL || undefined,
        env.OPENAI_EMBED_MODEL || undefined,
      );
  }
}
