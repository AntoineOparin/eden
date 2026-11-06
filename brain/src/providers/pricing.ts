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
