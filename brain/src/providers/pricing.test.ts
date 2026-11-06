import { describe, expect, it } from "vitest";
import { estimateNeurons, estimateUsd, NEURONS_PER_M, USD_PER_M } from "./pricing";

describe("estimateNeurons", () => {
  it("prices a typical qwen3 thought under 10 neurons", () => {
    const n = estimateNeurons({
      inputTokens: 900,
      outputTokens: 120,
      model: "@cf/qwen/qwen3-30b-a3b-fp8",
    });
    expect(n).toBeCloseTo((900 * 4625 + 120 * 30475) / 1e6, 2);
    expect(n).toBeLessThan(10);
  });
  it("prices embeddings by input only", () => {
    expect(
      estimateNeurons({ inputTokens: 1000, outputTokens: 0, model: "@cf/baai/bge-m3" }),
    ).toBeCloseTo(1.075, 3);
  });
  it("returns 0 for models billed elsewhere", () => {
    expect(
      estimateNeurons({ inputTokens: 1000, outputTokens: 1000, model: "claude-haiku-5-5" }),
    ).toBe(0);
  });
  it("prices a typical gemma-4 (default) thought under 10 neurons", () => {
    const n = estimateNeurons({
      inputTokens: 370,
      outputTokens: 140,
      model: "@cf/google/gemma-4-26b-a4b-it",
    });
    expect(n).toBeCloseTo((370 * 9091 + 140 * 27273) / 1e6, 2);
    expect(n).toBeLessThan(10);
  });
});

describe("estimateUsd", () => {
  it("prices Haiku 5.5 at $0.10 / $0.50 per MTok up to 100k input tokens", () => {
    expect(
      estimateUsd({ inputTokens: 1_000_000, outputTokens: 1_000_000, model: "claude-haiku-5-5" }),
    ).toBeCloseTo(0.5 + 2.5, 6); // over the threshold: the long-context row
    expect(
      estimateUsd({ inputTokens: 100_000, outputTokens: 1_000_000, model: "claude-haiku-5-5" }),
    ).toBeCloseTo(0.01 + 0.5, 6);
  });
  it("prices Sonnet 5.5 flat at $2 / $10 per MTok", () => {
    expect(
      estimateUsd({ inputTokens: 500_000, outputTokens: 100_000, model: "claude-sonnet-5-5" }),
    ).toBeCloseTo(1 + 1, 6);
  });
  it("is undefined for Workers AI and unknown models", () => {
    expect(
      estimateUsd({ inputTokens: 1, outputTokens: 1, model: "@cf/qwen/qwen3-30b-a3b-fp8" }),
    ).toBeUndefined();
    expect(estimateUsd({ inputTokens: 1, outputTokens: 1, model: "llama3.2" })).toBeUndefined();
  });
  it("never prices a model in both units", () => {
    for (const model of Object.keys(USD_PER_M)) expect(NEURONS_PER_M[model]).toBeUndefined();
  });
});
