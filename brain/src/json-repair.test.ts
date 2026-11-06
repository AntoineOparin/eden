import { describe, expect, it } from "vitest";
import { extractJson, repairPrompt } from "./json-repair";

describe("extractJson", () => {
  it("parses plain JSON", () => {
    expect(extractJson('{"plan":[]}')).toEqual({ plan: [] });
  });
  it("strips qwen <think> blocks and fences", () => {
    const t = '<think>\nhmm, hungry\n</think>\n```json\n{"plan":[{"action":"eat"}]}\n```';
    expect(extractJson(t)).toEqual({ plan: [{ action: "eat" }] });
  });
  it("finds the object inside chatter", () => {
    expect(extractJson('Sure! Here it is: {"plan":[]} Hope that helps.')).toEqual({ plan: [] });
  });
  it("returns undefined when there is nothing to parse", () => {
    expect(extractJson("I cannot do that.")).toBeUndefined();
  });
});

describe("repairPrompt", () => {
  it("lists issues and quotes the previous answer", () => {
    const p = repairPrompt('{"plan":"x"}', [{ path: "plan", message: "expected array" }]);
    expect(p).toContain("- plan: expected array");
    expect(p).toContain('{"plan":"x"}');
  });
});
