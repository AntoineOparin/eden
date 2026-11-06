import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it, vi } from "vitest";
import app from "./index";
import type { AppEnv } from "./env";

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
  it("is 501 until M5", async () => {
    const res = await post("/chat", fixture("chat.request.ok.json"), makeEnv(vi.fn()));
    expect(res.status).toBe(501);
  });
});
