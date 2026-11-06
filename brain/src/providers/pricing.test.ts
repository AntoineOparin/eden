import { describe, expect, it } from "vitest";
import { estimateNeurons } from "./pricing";

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
});
