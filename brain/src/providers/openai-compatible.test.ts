import { afterEach, describe, expect, it, vi } from "vitest";
import { isOpenAiReasoningModel, OpenAiCompatibleProvider } from "./openai-compatible";
import { ProviderError } from "./types";

type FetchFn = (url: string, init: RequestInit) => Promise<Response>;
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
const input = { system: "sys", user: "hi", maxTokens: 100, temperature: 0.5 };

afterEach(() => vi.unstubAllGlobals());

describe("OpenAiCompatibleProvider", () => {
  it("needs an http(s) base URL", () => {
    expect(() => new OpenAiCompatibleProvider("k", "")).toThrow(ProviderError);
    expect(() => new OpenAiCompatibleProvider("k", "api.openai.com")).toThrow(ProviderError);
  });

  it("falls back to reasoning_content like Workers AI", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn<FetchFn>(async () =>
        json({
          choices: [{ message: { content: "", reasoning_content: '{"reply":"x"}' } }],
          usage: { prompt_tokens: 3, completion_tokens: 4 },
        }),
      ),
    );
    const out = await new OpenAiCompatibleProvider("", "http://x/v1", "qwen3").complete(input);
    expect(out).toEqual({
      text: '{"reply":"x"}',
      usage: { inputTokens: 3, outputTokens: 4, model: "qwen3" },
    });
  });

  it("502 on an empty completion and on HTTP errors with the upstream message", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn<FetchFn>(async () => json({ choices: [{ message: { content: null } }] })),
    );
    await expect(
      new OpenAiCompatibleProvider("k", "http://x/v1", "m").complete(input),
    ).rejects.toMatchObject({ status: 502 });
    vi.stubGlobal(
      "fetch",
      vi.fn<FetchFn>(async () => json({ error: { message: "model not found" } }, 404)),
    );
    await expect(
      new OpenAiCompatibleProvider("k", "http://x/v1", "m").complete(input),
    ).rejects.toMatchObject({ status: 502, message: expect.stringContaining("model not found") });
  });

  it("502 on a non-JSON body", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn<FetchFn>(async () => new Response("<html>", { status: 200 })),
    );
    await expect(
      new OpenAiCompatibleProvider("k", "http://x/v1", "m").embed(["a"]),
    ).rejects.toMatchObject({ status: 502 });
  });

  it("estimates embed tokens when usage is missing", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn<FetchFn>(async () => json({ data: [{ embedding: [0.5] }] })),
    );
    const out = await new OpenAiCompatibleProvider("k", "http://x/v1").embed(["12345678"]);
    expect(out.usage).toEqual({ inputTokens: 2, outputTokens: 0, model: "text-embedding-3-small" });
  });
});

describe("isOpenAiReasoningModel", () => {
  it("matches o-series and GPT-5, bare or vendor-prefixed", () => {
    expect(isOpenAiReasoningModel("gpt-5-mini")).toBe(true);
    expect(isOpenAiReasoningModel("openai/gpt-5-nano")).toBe(true);
    expect(isOpenAiReasoningModel("o4-mini")).toBe(true);
    expect(isOpenAiReasoningModel("gpt-4o-mini")).toBe(false);
    expect(isOpenAiReasoningModel("llama3.2")).toBe(false);
  });
});
