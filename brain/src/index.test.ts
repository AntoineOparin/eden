import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { afterEach, describe, expect, it, vi } from "vitest";
import app from "./index";
import type { AppEnv } from "./env";
import { chatResultSchema, embedResultSchema, errorSchema } from "./schemas";

const fixturesDir = fileURLToPath(
  new URL("../../docs/specs/contracts/fixtures/", import.meta.url).href,
);
const fixture = (name: string) => readFileSync(fixturesDir + name, "utf8");

type RunFn = (model: string, input: Record<string, unknown>) => Promise<unknown>;

function makeEnv(run: RunFn): AppEnv {
  return {
    AI: { run } as unknown as Ai,
    EDEN_SHARED_SECRET: "test-secret",
    BRAIN_PROVIDER: "workers-ai",
    THINK_MODEL: "@cf/qwen/qwen3-30b-a3b-fp8",
    EMBED_MODEL: "@cf/baai/bge-m3",
  };
}

const chatCompletion = (content: string, prompt = 800, completion = 100) => ({
  id: "x",
  object: "chat.completion",
  choices: [{ index: 0, message: { role: "assistant", content }, finish_reason: "stop" }],
  usage: {
    prompt_tokens: prompt,
    completion_tokens: completion,
    total_tokens: prompt + completion,
  },
});

const post = (path: string, body: string, env: AppEnv, headers: Record<string, string> = {}) =>
  app.request(
    path,
    {
      method: "POST",
      headers: { "content-type": "application/json", "X-Eden-Key": "test-secret", ...headers },
      body,
    },
    env,
  );

describe("auth", () => {
  it("health needs no key", async () => {
    const res = await app.request("/health", {}, makeEnv(vi.fn()));
    expect(res.status).toBe(200);
    expect(await res.json()).toMatchObject({ ok: true, provider: "workers-ai" });
  });
  it("rejects a missing or wrong key", async () => {
    const env = makeEnv(vi.fn());
    const none = await app.request("/embed", { method: "POST", body: "{}" }, env);
    expect(none.status).toBe(401);
    const wrong = await post("/embed", "{}", env, { "X-Eden-Key": "nope" });
    expect(wrong.status).toBe(401);
    expect(await wrong.json()).toEqual({
      error: { code: "unauthorized", message: expect.any(String) },
    });
  });
  it("rejects an unknown provider override", async () => {
    const res = await post("/embed", fixture("embed.request.ok.json"), makeEnv(vi.fn()), {
      "X-Eden-Provider": "skynet",
    });
    expect(res.status).toBe(400);
  });
});

describe("/think", () => {
  it("400 on an invalid body with issues", async () => {
    const run = vi.fn();
    const res = await post("/think", fixture("think.request.bad.json"), makeEnv(run));
    expect(res.status).toBe(400);
    const body = (await res.json()) as { error: { code: string; details: { issues: unknown[] } } };
    expect(body.error.code).toBe("bad_request");
    expect(body.error.details.issues.length).toBeGreaterThan(0);
    expect(run).not.toHaveBeenCalled();
  });

  it("returns a validated plan and cost headers", async () => {
    const run = vi.fn<RunFn>(async () => chatCompletion(fixture("think.response.ok.json")));
    const res = await post("/think", fixture("think.request.ok.json"), makeEnv(run));
    expect(res.status).toBe(200);
    const body = (await res.json()) as {
      response: { plan: unknown[] };
      usage: { neurons: number; model: string };
    };
    expect(body.response.plan).toHaveLength(2);
    expect(body.usage.model).toBe("@cf/qwen/qwen3-30b-a3b-fp8");
    expect(body.usage.neurons).toBeGreaterThan(0);
    expect(res.headers.get("X-Eden-Neurons-Cost")).toBe(String(body.usage.neurons));
    expect(res.headers.get("X-Eden-Model")).toBe("@cf/qwen/qwen3-30b-a3b-fp8");
    const [model, input] = run.mock.calls[0]!;
    expect(model).toBe("@cf/qwen/qwen3-30b-a3b-fp8");
    expect(input).toMatchObject({
      messages: [{ role: "system" }, { role: "user" }],
      response_format: { type: "json_schema" },
    });
  });

  it("repairs once, then succeeds", async () => {
    const run = vi
      .fn<RunFn>()
      .mockResolvedValueOnce(
        chatCompletion('<think>hmm</think>{"plan":[{"action":"summon_dragon"}]}'),
      )
      .mockResolvedValueOnce(chatCompletion('{"plan":[{"action":"eat"}]}'));
    const res = await post("/think", fixture("think.request.ok.json"), makeEnv(run));
    expect(res.status).toBe(200);
    expect(run).toHaveBeenCalledTimes(2);
    const second = run.mock.calls[1]![1] as { messages: Array<{ content: string }> };
    expect(second.messages[1]!.content).toContain("Problems:");
    const body = (await res.json()) as { usage: { inputTokens: number } };
    expect(body.usage.inputTokens).toBe(1600); // both calls are billed
  });

  it("422 when the repair also fails", async () => {
    const run = vi.fn<RunFn>(async () => chatCompletion("I refuse."));
    const res = await post("/think", fixture("think.request.ok.json"), makeEnv(run));
    expect(res.status).toBe(422);
    expect(run).toHaveBeenCalledTimes(2);
    expect(await res.json()).toMatchObject({ error: { code: "invalid_output" } });
  });

  it("reads the answer from reasoning_content when content is null", async () => {
    const run = vi.fn<RunFn>(async () => ({
      choices: [
        {
          message: {
            role: "assistant",
            content: null,
            reasoning_content: '{"plan":[{"action":"rest"}]}',
          },
        },
      ],
      usage: { prompt_tokens: 10, completion_tokens: 5 },
    }));
    const res = await post("/think", fixture("think.request.ok.json"), makeEnv(run));
    expect(res.status).toBe(200);
    const body = (await res.json()) as { response: { plan: Array<{ action: string }> } };
    expect(body.response.plan[0]!.action).toBe("rest");
  });

  it("accepts the legacy { response } shape and drops inventions when not allowed", async () => {
    const invention = JSON.parse(fixture("concepts.concept.ok.json")) as unknown;
    const run = vi.fn<RunFn>(async () => ({ response: JSON.stringify({ plan: [], invention }) }));
    const res = await post("/think", fixture("think.request.ok.json"), makeEnv(run));
    expect(res.status).toBe(200);
    const body = (await res.json()) as { response: { invention: unknown } };
    expect(body.response.invention).toBeNull();
  });

  it("502 when the provider throws", async () => {
    const run = vi.fn<RunFn>(async () => {
      throw new Error("capacity");
    });
    const res = await post("/think", fixture("think.request.ok.json"), makeEnv(run));
    expect(res.status).toBe(502);
    expect(await res.json()).toMatchObject({ error: { code: "provider_error" } });
  });
});

describe("/embed", () => {
  it("maps data to vectors", async () => {
    const run = vi.fn<RunFn>(async (_m, input) => {
      const texts = input.text as string[];
      return { shape: [texts.length, 4], data: texts.map((_, i) => [i, 0.5, 0.25, 0.125]) };
    });
    const res = await post("/embed", fixture("embed.request.ok.json"), makeEnv(run));
    expect(res.status).toBe(200);
    const body = (await res.json()) as {
      response: { dimensions: number; vectors: number[][] };
      usage: { neurons: number };
    };
    expect(body.response.dimensions).toBe(4);
    expect(body.response.vectors).toHaveLength(2);
    expect(body.usage.neurons).toBeGreaterThan(0);
  });
  it("400 on an empty list", async () => {
    const res = await post("/embed", fixture("embed.request.bad.json"), makeEnv(vi.fn()));
    expect(res.status).toBe(400);
  });
  it("502 on an unexpected provider shape", async () => {
    const res = await post(
      "/embed",
      fixture("embed.request.ok.json"),
      makeEnv(vi.fn<RunFn>(async () => ({ nope: 1 }))),
    );
    expect(res.status).toBe(502);
  });
});

describe("/chat", () => {
  it("400 on an invalid body with issues", async () => {
    const run = vi.fn();
    const res = await post("/chat", fixture("chat.request.bad.json"), makeEnv(run));
    expect(res.status).toBe(400);
    const body = (await res.json()) as { error: { code: string; details: { issues: unknown[] } } };
    expect(body.error.code).toBe("bad_request");
    expect(body.error.details.issues.length).toBeGreaterThan(0);
    expect(run).not.toHaveBeenCalled();
  });

  it("returns a validated reply and cost headers", async () => {
    const run = vi.fn<RunFn>(async () => chatCompletion(fixture("chat.response.ok.json"), 500, 60));
    const res = await post("/chat", fixture("chat.request.ok.json"), makeEnv(run));
    expect(res.status).toBe(200);
    const body = (await res.json()) as {
      response: { reply: string; mood: string; memory: { importance: number } };
      usage: { neurons: number; model: string; inputTokens: number };
    };
    expect(chatResultSchema.safeParse(body).success).toBe(true);
    expect(body.response.mood).toBe("afraid");
    expect(body.usage.inputTokens).toBe(500);
    expect(body.usage.neurons).toBeCloseTo((500 * 4625 + 60 * 30475) / 1e6, 3);
    expect(res.headers.get("X-Eden-Neurons-Cost")).toBe(String(body.usage.neurons));
    expect(res.headers.get("X-Eden-Model")).toBe("@cf/qwen/qwen3-30b-a3b-fp8");
    expect(res.headers.get("X-Eden-Usd-Cost")).toBeNull();
    const [model, input] = run.mock.calls[0]!;
    expect(model).toBe("@cf/qwen/qwen3-30b-a3b-fp8");
    const sent = input as {
      messages: Array<{ role: string; content: string }>;
      response_format: { json_schema: { required: string[] } };
      chat_template_kwargs: unknown;
    };
    expect(sent.messages[0]!.content).toContain("The Voice");
    expect(sent.messages[1]!.content).toContain("Adam: Who speaks?");
    expect(sent.messages[1]!.content).toContain("The Voice: Why do you fear the hill?");
    expect(sent.response_format.json_schema.required).toEqual(["reply"]);
    expect(sent.chat_template_kwargs).toEqual({ enable_thinking: false });
  });

  it("repairs once, then succeeds, billing both calls", async () => {
    const run = vi
      .fn<RunFn>()
      .mockResolvedValueOnce(chatCompletion('```json\n{"reply":"Hm.","mood":"ecstatic"}\n```'))
      .mockResolvedValueOnce(chatCompletion('{"reply":"Hm.","mood":"suspicious"}'));
    const res = await post("/chat", fixture("chat.request.ok.json"), makeEnv(run));
    expect(res.status).toBe(200);
    expect(run).toHaveBeenCalledTimes(2);
    const second = run.mock.calls[1]![1] as { messages: Array<{ content: string }> };
    expect(second.messages[1]!.content).toContain("Problems:");
    expect(second.messages[1]!.content).toContain("mood");
    const body = (await res.json()) as {
      response: { mood: string };
      usage: { inputTokens: number };
    };
    expect(body.response.mood).toBe("suspicious");
    expect(body.usage.inputTokens).toBe(1600);
  });

  it("422 with raw text and usage when the repair also fails", async () => {
    const run = vi.fn<RunFn>(async () => chatCompletion('{"reply":""}'));
    const res = await post("/chat", fixture("chat.request.ok.json"), makeEnv(run));
    expect(res.status).toBe(422);
    expect(run).toHaveBeenCalledTimes(2);
    expect(res.headers.get("X-Eden-Neurons-Cost")).not.toBeNull();
    const body = (await res.json()) as {
      error: { code: string; details: { raw: string; usage: { inputTokens: number } } };
    };
    expect(errorSchema.safeParse(body).success).toBe(true);
    expect(body.error.code).toBe("invalid_output");
    expect(body.error.details.raw).toBe('{"reply":""}');
    expect(body.error.details.usage.inputTokens).toBe(1600);
  });

  it("reads the reply from reasoning_content when content is null", async () => {
    const run = vi.fn<RunFn>(async () => ({
      choices: [
        { message: { content: null, reasoning_content: '<think></think>{"reply":"Who?"}' } },
      ],
      usage: { prompt_tokens: 10, completion_tokens: 5 },
    }));
    const res = await post("/chat", fixture("chat.request.ok.json"), makeEnv(run));
    expect(res.status).toBe(200);
    expect(await res.json()).toMatchObject({ response: { reply: "Who?" } });
  });

  it("502 when the provider throws", async () => {
    const run = vi.fn<RunFn>(async () => {
      throw new Error("capacity");
    });
    const res = await post("/chat", fixture("chat.request.ok.json"), makeEnv(run));
    expect(res.status).toBe(502);
    expect(await res.json()).toMatchObject({ error: { code: "provider_error" } });
  });
});

// ---------- non-default providers, through the routes with a mocked global fetch ----------

type FetchFn = (url: string, init: RequestInit) => Promise<Response>;

const jsonResponse = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });

const anthropicMessage = (text: string, input = 700, output = 90) => ({
  id: "msg_1",
  type: "message",
  role: "assistant",
  model: "claude-haiku-5-5",
  content: [{ type: "text", text }],
  stop_reason: "end_turn",
  usage: { input_tokens: input, output_tokens: output },
});

function stubFetch(impl: FetchFn) {
  const fn = vi.fn<FetchFn>(impl);
  vi.stubGlobal("fetch", fn);
  return fn;
}

const sentBody = (fn: ReturnType<typeof stubFetch>, call = 0) =>
  JSON.parse(fn.mock.calls[call]![1].body as string) as Record<string, unknown>;

describe("provider: anthropic", () => {
  afterEach(() => vi.unstubAllGlobals());
  const env = (): AppEnv => ({ ...makeEnv(vi.fn()), ANTHROPIC_API_KEY: "sk-ant-test" });
  const hdr = { "X-Eden-Provider": "anthropic" };

  it("501 when ANTHROPIC_API_KEY is missing", async () => {
    const fetchFn = stubFetch(async () => jsonResponse({}));
    const res = await post("/chat", fixture("chat.request.ok.json"), makeEnv(vi.fn()), hdr);
    expect(res.status).toBe(501);
    expect(await res.json()).toMatchObject({ error: { code: "unsupported" } });
    expect(fetchFn).not.toHaveBeenCalled();
  });

  it("/chat calls the Messages API and prices in USD", async () => {
    const fetchFn = stubFetch(async () =>
      jsonResponse(anthropicMessage(fixture("chat.response.ok.json"))),
    );
    const ai = vi.fn();
    const e = { ...env(), AI: { run: ai } as unknown as Ai };
    const res = await post("/chat", fixture("chat.request.ok.json"), e, hdr);
    expect(res.status).toBe(200);
    expect(ai).not.toHaveBeenCalled();
    const body = (await res.json()) as { usage: { model: string; neurons: number } };
    expect(body.usage).toMatchObject({ model: "claude-haiku-5-5", neurons: 0 });
    expect(res.headers.get("X-Eden-Model")).toBe("claude-haiku-5-5");
    expect(res.headers.get("X-Eden-Neurons-Cost")).toBe("0");
    expect(Number(res.headers.get("X-Eden-Usd-Cost"))).toBeCloseTo(
      (700 * 0.1 + 90 * 0.5) / 1e6,
      10,
    );
    const [url, init] = fetchFn.mock.calls[0]!;
    expect(url).toBe("https://api.anthropic.com/v1/messages");
    expect(init.headers).toMatchObject({
      "x-api-key": "sk-ant-test",
      "anthropic-version": "2026-10-21",
    });
    const sent = sentBody(fetchFn);
    expect(sent).toMatchObject({ model: "claude-haiku-5-5", thinking: { type: "disabled" } });
    expect(sent).not.toHaveProperty("temperature");
    expect(sent.output_config).toMatchObject({ format: { type: "json_schema" } });
    expect(JSON.stringify(sent.output_config)).not.toMatch(/maxLength|minimum|maximum/);
  });

  it("/think repairs once through Anthropic", async () => {
    const fetchFn = vi.fn<FetchFn>();
    fetchFn
      .mockResolvedValueOnce(jsonResponse(anthropicMessage('{"plan":[{"action":"fly"}]}')))
      .mockResolvedValueOnce(jsonResponse(anthropicMessage('{"plan":[{"action":"rest"}]}')));
    vi.stubGlobal("fetch", fetchFn);
    const res = await post("/think", fixture("think.request.ok.json"), env(), hdr);
    expect(res.status).toBe(200);
    expect(fetchFn).toHaveBeenCalledTimes(2);
    const repair = sentBody(fetchFn, 1) as { messages: Array<{ content: string }> };
    expect(repair.messages[0]!.content).toContain("Problems:");
    expect(await res.json()).toMatchObject({ usage: { inputTokens: 1400, outputTokens: 180 } });
  });

  it("uses ANTHROPIC_MODEL and the lowest thinking setting for Sonnet 5.5", async () => {
    const fetchFn = stubFetch(async () => jsonResponse(anthropicMessage('{"reply":"Yes."}')));
    const res = await post(
      "/chat",
      fixture("chat.request.ok.json"),
      { ...env(), ANTHROPIC_MODEL: "claude-sonnet-5-5" },
      hdr,
    );
    expect(res.status).toBe(200);
    expect(sentBody(fetchFn)).toMatchObject({
      model: "claude-sonnet-5-5",
      thinking: { type: "between_tools" },
    });
    expect(Number(res.headers.get("X-Eden-Usd-Cost"))).toBeCloseTo((700 * 2 + 90 * 10) / 1e6, 10);
  });

  it("maps an upstream error to 502 without leaking the key", async () => {
    stubFetch(async () =>
      jsonResponse(
        { type: "error", error: { type: "authentication_error", message: "invalid x-api-key" } },
        401,
      ),
    );
    const res = await post("/think", fixture("think.request.ok.json"), env(), hdr);
    expect(res.status).toBe(502);
    const text = await res.text();
    expect(text).toContain("invalid x-api-key");
    expect(text).not.toContain("sk-ant-test");
  });

  it("422 when both answers fail validation", async () => {
    stubFetch(async () => jsonResponse(anthropicMessage("I would rather not.")));
    const res = await post("/chat", fixture("chat.request.ok.json"), env(), hdr);
    expect(res.status).toBe(422);
    expect(await res.json()).toMatchObject({
      error: { code: "invalid_output", details: { raw: "I would rather not." } },
    });
  });

  it("/embed is 501 unsupported", async () => {
    const fetchFn = stubFetch(async () => jsonResponse({}));
    const res = await post("/embed", fixture("embed.request.ok.json"), env(), hdr);
    expect(res.status).toBe(501);
    expect(await res.json()).toMatchObject({ error: { code: "unsupported" } });
    expect(fetchFn).not.toHaveBeenCalled();
  });

  it("is selected by BRAIN_PROVIDER without a header", async () => {
    stubFetch(async () => jsonResponse(anthropicMessage('{"reply":"Hello."}')));
    const res = await post("/chat", fixture("chat.request.ok.json"), {
      ...env(),
      BRAIN_PROVIDER: "anthropic",
    });
    expect(res.status).toBe(200);
    expect(res.headers.get("X-Eden-Model")).toBe("claude-haiku-5-5");
  });
});

describe("provider: openai-compatible", () => {
  afterEach(() => vi.unstubAllGlobals());
  const env = (extra: Partial<AppEnv> = {}): AppEnv => ({
    ...makeEnv(vi.fn()),
    OPENAI_API_KEY: "sk-oa-test",
    OPENAI_BASE_URL: "https://api.openai.com/v1/",
    ...extra,
  });
  const hdr = { "X-Eden-Provider": "openai-compatible" };

  it("501 when OPENAI_BASE_URL is missing", async () => {
    const res = await post(
      "/think",
      fixture("think.request.ok.json"),
      { ...makeEnv(vi.fn()), OPENAI_API_KEY: "k" },
      hdr,
    );
    expect(res.status).toBe(501);
  });

  it("/think posts to /chat/completions with reasoning-model params", async () => {
    const fetchFn = stubFetch(async () =>
      jsonResponse(chatCompletion(fixture("think.response.ok.json"), 900, 120)),
    );
    const res = await post("/think", fixture("think.request.ok.json"), env(), hdr);
    expect(res.status).toBe(200);
    const [url, init] = fetchFn.mock.calls[0]!;
    expect(url).toBe("https://api.openai.com/v1/chat/completions");
    expect(init.headers).toMatchObject({ authorization: "Bearer sk-oa-test" });
    const sent = sentBody(fetchFn);
    expect(sent).toMatchObject({
      model: "gpt-5-mini",
      max_completion_tokens: 2600,
      reasoning_effort: "low",
      response_format: { type: "json_schema", json_schema: { name: "eden_output" } },
    });
    expect(sent).not.toHaveProperty("temperature");
    expect(sent).not.toHaveProperty("max_tokens");
    expect(Number(res.headers.get("X-Eden-Usd-Cost"))).toBeCloseTo(
      (900 * 0.25 + 120 * 2) / 1e6,
      10,
    );
  });

  it("/chat on a local server: no key, max_tokens, no USD header", async () => {
    const fetchFn = stubFetch(async () =>
      jsonResponse(chatCompletion(fixture("chat.response.ok.json"))),
    );
    const res = await post(
      "/chat",
      fixture("chat.request.ok.json"),
      env({
        OPENAI_API_KEY: "",
        OPENAI_BASE_URL: "http://localhost:11434/v1",
        OPENAI_MODEL: "llama3.2",
      }),
      hdr,
    );
    expect(res.status).toBe(200);
    const [url, init] = fetchFn.mock.calls[0]!;
    expect(url).toBe("http://localhost:11434/v1/chat/completions");
    expect(init.headers).not.toHaveProperty("authorization");
    expect(sentBody(fetchFn)).toMatchObject({
      model: "llama3.2",
      max_tokens: 400,
      temperature: 0.8,
    });
    expect(res.headers.get("X-Eden-Model")).toBe("llama3.2");
    expect(res.headers.get("X-Eden-Usd-Cost")).toBeNull();
  });

  it("/chat 422 after one repair", async () => {
    const fetchFn = stubFetch(async () => jsonResponse(chatCompletion("nope")));
    const res = await post("/chat", fixture("chat.request.ok.json"), env(), hdr);
    expect(res.status).toBe(422);
    expect(fetchFn).toHaveBeenCalledTimes(2);
  });

  it("/embed maps data[].embedding by index", async () => {
    const fetchFn = stubFetch(async () =>
      jsonResponse({
        object: "list",
        data: [
          { object: "embedding", index: 1, embedding: [0, 1, 0] },
          { object: "embedding", index: 0, embedding: [1, 0, 0] },
        ],
        model: "text-embedding-3-small",
        usage: { prompt_tokens: 12, total_tokens: 12 },
      }),
    );
    const res = await post("/embed", fixture("embed.request.ok.json"), env(), hdr);
    expect(res.status).toBe(200);
    const body = (await res.json()) as unknown;
    expect(embedResultSchema.safeParse(body).success).toBe(true);
    expect(body).toMatchObject({
      response: {
        model: "text-embedding-3-small",
        dimensions: 3,
        vectors: [
          [1, 0, 0],
          [0, 1, 0],
        ],
      },
      usage: { inputTokens: 12, neurons: 0 },
    });
    expect(fetchFn.mock.calls[0]![0]).toBe("https://api.openai.com/v1/embeddings");
    expect(sentBody(fetchFn)).toMatchObject({ model: "text-embedding-3-small" });
    expect(Number(res.headers.get("X-Eden-Usd-Cost"))).toBeCloseTo((12 * 0.02) / 1e6, 12);
  });

  it("/embed 502 on a short data array", async () => {
    stubFetch(async () => jsonResponse({ data: [{ index: 0, embedding: [1] }] }));
    const res = await post("/embed", fixture("embed.request.ok.json"), env(), hdr);
    expect(res.status).toBe(502);
  });

  it("502 on a network failure", async () => {
    stubFetch(async () => {
      throw new TypeError("fetch failed");
    });
    const res = await post("/think", fixture("think.request.ok.json"), env(), hdr);
    expect(res.status).toBe(502);
    expect(await res.json()).toMatchObject({ error: { code: "provider_error" } });
  });
});

describe("default Workers AI model (Gemma 4)", () => {
  const gemma = "@cf/google/gemma-4-26b-a4b-it";
  // Gemma's chat.completion carries a required `refusal` field next to `content`.
  const gemmaCompletion = (content: string) => ({
    object: "chat.completion",
    model: gemma,
    choices: [
      { index: 0, message: { role: "assistant", content, refusal: null }, finish_reason: "stop" },
    ],
    usage: { prompt_tokens: 370, completion_tokens: 140, total_tokens: 510 },
  });
  const envWithoutModel = (run: RunFn): AppEnv => {
    const { THINK_MODEL: _unused, ...rest } = makeEnv(run);
    return rest;
  };

  for (const [route, req, out] of [
    ["/think", "think.request.ok.json", "think.response.ok.json"],
    ["/chat", "chat.request.ok.json", "chat.response.ok.json"],
  ] as const) {
    it(`${route} uses gemma-4 with thinking disabled when THINK_MODEL is unset`, async () => {
      const run = vi.fn<RunFn>(async () => gemmaCompletion(fixture(out)));
      const res = await post(route, fixture(req), envWithoutModel(run));
      expect(res.status).toBe(200);
      const [model, input] = run.mock.calls[0]!;
      expect(model).toBe(gemma);
      expect(input).toMatchObject({ chat_template_kwargs: { enable_thinking: false } });
      expect(res.headers.get("X-Eden-Model")).toBe(gemma);
      expect(Number(res.headers.get("X-Eden-Neurons-Cost"))).toBeCloseTo(
        (370 * 9091 + 140 * 27273) / 1e6,
        3,
      );
    });
  }
});
