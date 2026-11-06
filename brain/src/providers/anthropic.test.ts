import { afterEach, describe, expect, it, vi } from "vitest";
import { CHAT_OUTPUT_JSON_SCHEMA, THINK_OUTPUT_JSON_SCHEMA } from "../schemas";
import { AnthropicProvider, lowestThinking, toStructuredOutputSchema } from "./anthropic";
import { ProviderError } from "./types";

type FetchFn = (url: string, init: RequestInit) => Promise<Response>;
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
const input = { system: "sys", user: "hi", maxTokens: 100, temperature: 0.7 };

afterEach(() => vi.unstubAllGlobals());

describe("AnthropicProvider", () => {
  it("needs a key", () => {
    expect(() => new AnthropicProvider("")).toThrow(ProviderError);
  });

  it("joins text blocks, skips thinking blocks, counts cache tokens as input", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn<FetchFn>(async () =>
        json({
          content: [
            { type: "thinking", thinking: "", signature: "x" },
            { type: "text", text: '{"reply":' },
            { type: "text", text: '"ok"}' },
          ],
          stop_reason: "end_turn",
          usage: {
            input_tokens: 10,
            cache_creation_input_tokens: 5,
            cache_read_input_tokens: 2,
            output_tokens: 7,
          },
        }),
      ),
    );
    const out = await new AnthropicProvider("k").complete(input);
    expect(out.text).toBe('{"reply":"ok"}');
    expect(out.usage).toEqual({ inputTokens: 17, outputTokens: 7, model: "claude-haiku-5-5" });
  });

  it("sends system separately and omits output_config without a schema", async () => {
    const fetchFn = vi.fn<FetchFn>(async () =>
      json({ content: [{ type: "text", text: "x" }], usage: {} }),
    );
    vi.stubGlobal("fetch", fetchFn);
    await new AnthropicProvider("k").complete(input);
    const body = JSON.parse(fetchFn.mock.calls[0]![1].body as string) as Record<string, unknown>;
    expect(body).toEqual({
      model: "claude-haiku-5-5",
      max_tokens: 100,
      system: "sys",
      messages: [{ role: "user", content: "hi" }],
      thinking: { type: "disabled" },
    });
  });

  it("treats a refusal or an empty answer as a provider error", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn<FetchFn>(async () => json({ content: [], stop_reason: "refusal", usage: {} })),
    );
    await expect(new AnthropicProvider("k").complete(input)).rejects.toMatchObject({
      status: 502,
      message: expect.stringContaining("refused"),
    });
    vi.stubGlobal(
      "fetch",
      vi.fn<FetchFn>(async () => json({ content: [], stop_reason: "max_tokens", usage: {} })),
    );
    await expect(new AnthropicProvider("k").complete(input)).rejects.toMatchObject({
      message: expect.stringContaining("max_tokens"),
    });
  });

  it("embed rejects with 501", async () => {
    await expect(new AnthropicProvider("k").embed(["a"])).rejects.toMatchObject({
      status: 501,
      code: "unsupported",
    });
  });
});

describe("lowestThinking", () => {
  it("disables thinking on Haiku 5.5 and uses between_tools on Sonnet 5.5", () => {
    expect(lowestThinking("claude-haiku-5-5")).toEqual({ type: "disabled" });
    expect(lowestThinking("claude-sonnet-5-5")).toEqual({ type: "between_tools" });
    expect(lowestThinking("claude-opus-5-5")).toBeUndefined();
  });
});

describe("toStructuredOutputSchema", () => {
  it("drops bounds structured outputs rejects and keeps the rest", () => {
    const s = toStructuredOutputSchema(THINK_OUTPUT_JSON_SCHEMA);
    const text = JSON.stringify(s);
    expect(text).not.toMatch(/maxLength|minLength|minimum|maximum|maxItems/);
    expect(s).toMatchObject({
      additionalProperties: false,
      required: ["plan"],
      properties: { plan: { type: "array", items: { required: ["action"] } } },
    });
    expect(text).toContain('"enum":["idle","eat"');
  });
  it("keeps enum values, including null, verbatim", () => {
    const s = toStructuredOutputSchema(CHAT_OUTPUT_JSON_SCHEMA) as {
      properties: { mood: { enum: unknown[] } };
    };
    expect(s.properties.mood.enum).toContain(null);
    expect(s.properties.mood.enum).toContain("awed");
  });
  it("does not treat property names as keywords", () => {
    expect(
      toStructuredOutputSchema({
        type: "object",
        properties: { maximum: { type: "integer", maximum: 3 } },
      }),
    ).toEqual({ type: "object", properties: { maximum: { type: "integer" } } });
  });
});
