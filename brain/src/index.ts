import { Hono } from "hono";
import type { Context } from "hono";
import type { ZodType } from "zod";
import type { AppContext, AppEnv } from "./env";
import { extractJson, repairPrompt } from "./json-repair";
import { buildThinkPrompt } from "./prompts";
import { estimateNeurons } from "./providers/pricing";
import { selectProvider } from "./providers/select";
import { ProviderError } from "./providers/types";
import type { BrainProvider, ProviderUsage } from "./providers/types";
import {
  embedRequestSchema,
  THINK_OUTPUT_JSON_SCHEMA,
  thinkRequestSchema,
  thinkResponseSchema,
} from "./schemas";
import type { ErrorCode, Usage } from "./schemas";

const app = new Hono<AppContext>();

// ---------- helpers ----------

function errorResponse(
  c: Context<AppContext>,
  status: 400 | 401 | 422 | 500 | 501 | 502,
  code: ErrorCode,
  message: string,
  details?: unknown,
) {
  return c.json(
    { error: { code, message, ...(details === undefined ? {} : { details }) } },
    status,
  );
}

function constantTimeEqual(a: string, b: string): boolean {
  const ea = new TextEncoder().encode(a);
  const eb = new TextEncoder().encode(b);
  let diff = ea.length ^ eb.length;
  for (let i = 0; i < Math.max(ea.length, eb.length); i++) diff |= (ea[i] ?? 0) ^ (eb[i] ?? 0);
  return diff === 0;
}

async function parseBody<T>(
  c: Context<AppContext>,
  schema: ZodType<T>,
): Promise<{ ok: true; data: T } | { ok: false; res: Response }> {
  let json: unknown;
  try {
    json = await c.req.json();
  } catch {
    return { ok: false, res: errorResponse(c, 400, "bad_request", "body must be JSON") };
  }
  const parsed = schema.safeParse(json);
  if (!parsed.success) {
    const issues = parsed.error.issues.map((i) => ({ path: i.path.join("."), message: i.message }));
    return {
      ok: false,
      res: errorResponse(c, 400, "bad_request", "request failed validation", { issues }),
    };
  }
  return { ok: true, data: parsed.data };
}

function toUsage(u: ProviderUsage): Usage {
  return {
    inputTokens: u.inputTokens,
    outputTokens: u.outputTokens,
    neurons: estimateNeurons(u),
    model: u.model,
  };
}

function withCostHeaders(c: Context<AppContext>, usage: Usage) {
  c.header("X-Eden-Neurons-Cost", usage.neurons.toString());
  c.header("X-Eden-Model", usage.model);
}

// ---------- middleware ----------

app.use("*", async (c, next) => {
  if (c.req.path === "/health") return next();
  const secret = c.env.EDEN_SHARED_SECRET;
  if (!secret)
    return errorResponse(c, 500, "provider_error", "EDEN_SHARED_SECRET is not configured");
  const key = c.req.header("X-Eden-Key") ?? "";
  if (!constantTimeEqual(key, secret))
    return errorResponse(c, 401, "unauthorized", "bad or missing X-Eden-Key");
  try {
    c.set("provider", selectProvider(c.env, c.req.header("X-Eden-Provider")));
  } catch (e) {
    if (e instanceof ProviderError)
      return errorResponse(c, e.status as 400 | 500 | 501, e.code, e.message);
    throw e;
  }
  return next();
});

app.onError((err, c) => {
  if (err instanceof ProviderError)
    return errorResponse(c, err.status as 501 | 502, err.code, err.message);
  console.error("unhandled", err);
  return errorResponse(c, 500, "provider_error", "internal error");
});

// ---------- routes ----------

app.get("/health", (c) => {
  const env: AppEnv = c.env;
  return c.json({
    ok: true,
    provider: env.BRAIN_PROVIDER ?? "workers-ai",
    models: { think: env.THINK_MODEL ?? null, embed: env.EMBED_MODEL ?? null },
  });
});

app.post("/think", async (c) => {
  const body = await parseBody(c, thinkRequestSchema);
  if (!body.ok) return body.res;
  const provider: BrainProvider = c.get("provider");
  const { system, user } = buildThinkPrompt(body.data);
  const base = {
    system,
    maxTokens: 600,
    temperature: 0.7,
    jsonSchema: THINK_OUTPUT_JSON_SCHEMA as unknown as Record<string, unknown>,
  };

  const first = await provider.complete({ ...base, user });
  let usage = first.usage;
  let parsed = thinkResponseSchema.safeParse(extractJson(first.text));
  if (!parsed.success) {
    const issues = parsed.error.issues.map((i) => ({ path: i.path.join("."), message: i.message }));
    const second = await provider.complete({
      ...base,
      user: `${user}\n\n${repairPrompt(first.text, issues)}`,
    });
    usage = {
      inputTokens: usage.inputTokens + second.usage.inputTokens,
      outputTokens: usage.outputTokens + second.usage.outputTokens,
      model: usage.model,
    };
    parsed = thinkResponseSchema.safeParse(extractJson(second.text));
    if (!parsed.success) {
      const u = toUsage(usage);
      withCostHeaders(c, u);
      console.warn("invalid model output", JSON.stringify(second.text.slice(0, 600)));
      return errorResponse(
        c,
        422,
        "invalid_output",
        "model output failed schema after repair retry",
        {
          issues: parsed.error.issues.map((i) => ({ path: i.path.join("."), message: i.message })),
          raw: second.text.slice(0, 400),
          usage: u,
        },
      );
    }
  }
  if (!body.data.allowInvention) parsed.data.invention = null;
  if (parsed.data.plan.length > body.data.maxGoals)
    parsed.data.plan = parsed.data.plan.slice(0, body.data.maxGoals);
  const u = toUsage(usage);
  withCostHeaders(c, u);
  return c.json({ response: parsed.data, usage: u });
});

app.post("/chat", async (c) => {
  // Implemented in M5 (docs/specs/30-brain-worker.md). The schema and prompt builder already exist.
  return errorResponse(c, 501, "unsupported", "/chat arrives with M5");
});

app.post("/embed", async (c) => {
  const body = await parseBody(c, embedRequestSchema);
  if (!body.ok) return body.res;
  const provider: BrainProvider = c.get("provider");
  const { vectors, usage } = await provider.embed(body.data.texts);
  const u = toUsage(usage);
  withCostHeaders(c, u);
  return c.json({
    response: { model: usage.model, dimensions: vectors[0]?.length ?? 0, vectors },
    usage: u,
  });
});

app.notFound((c) =>
  errorResponse(c, 404 as unknown as 400, "not_found", `no route ${c.req.method} ${c.req.path}`),
);

export default app;
