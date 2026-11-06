import type { ProviderUsage } from "./types";

/** Neurons per million tokens, from developers.cloudflare.com/workers-ai/platform/pricing (2026-10-20). */
export const NEURONS_PER_M: Record<string, { input: number; output: number }> = {
  "@cf/qwen/qwen3-30b-a3b-fp8": { input: 4625, output: 30475 },
  "@cf/google/gemma-4-26b-a4b-it": { input: 9091, output: 27273 },
  "@cf/zai-org/glm-4.7-flash": { input: 5500, output: 36400 },
  "@cf/baai/bge-m3": { input: 1075, output: 0 },
};

export const FREE_NEURONS_PER_DAY = 10_000;

/** Estimated Neurons for one call. Unknown models (e.g. direct Anthropic) cost 0 Neurons; they bill elsewhere. */
export function estimateNeurons(usage: ProviderUsage): number {
  const rate = NEURONS_PER_M[usage.model];
  if (!rate) return 0;
  const n = (usage.inputTokens * rate.input + usage.outputTokens * rate.output) / 1_000_000;
  return Math.round(n * 1000) / 1000;
}

/**
 * USD per million tokens for providers billed to the player's own key (Neurons stay 0 for them).
 * `longContext` applies to a whole call whose input is over `threshold` tokens.
 */
export interface UsdRate {
  input: number;
  output: number;
  longContext?: { threshold: number; input: number; output: number };
}

export const USD_PER_M: Record<string, UsdRate> = {
  // https://platform.claude.com/docs/en/about-claude/pricing, "Model pricing" (checked 2026-11-06):
  // "Claude Haiku 5.5 (for prompts up to 100,000 tokens) | $0.10 / MTok | ... | $0.50 / MTok"
  // "Claude Haiku 5.5 (for prompts over 100,000 tokens)  | $0.50 / MTok | ... | $2.50 / MTok"
  "claude-haiku-5-5": {
    input: 0.1,
    output: 0.5,
    longContext: { threshold: 100_000, input: 0.5, output: 2.5 },
  },
  // Same page: "Claude Sonnet 5.5 | $2 / MTok | ... | $10 / MTok" (flat across the 1M context).
  "claude-sonnet-5-5": { input: 2, output: 10 },
  // https://developers.openai.com/api/docs/pricing, Standard tier (checked 2026-11-06):
  // "gpt-5-mini | $0.25 | $2.00" and "text-embedding-3-small | $0.02 | -".
  "gpt-5-mini": { input: 0.25, output: 2 },
  "text-embedding-3-small": { input: 0.02, output: 0 },
};

/** Estimated USD for one upstream call, or undefined when the model has no price row (Workers AI, local servers). */
export function estimateUsd(usage: ProviderUsage): number | undefined {
  const rate = USD_PER_M[usage.model];
  if (!rate) return undefined;
  const r =
    rate.longContext && usage.inputTokens > rate.longContext.threshold ? rate.longContext : rate;
  const usd = (usage.inputTokens * r.input + usage.outputTokens * r.output) / 1_000_000;
  return Math.round(usd * 1e8) / 1e8;
}
