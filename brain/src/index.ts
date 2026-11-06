import { Hono } from "hono";
import type { Context } from "hono";
import type { ZodType } from "zod";
import type { AppContext, AppEnv } from "./env";
import { extractJson, repairPrompt } from "./json-repair";
import type { Issue } from "./json-repair";
import { buildChatPrompt, buildThinkPrompt } from "./prompts";
import { estimateNeurons, estimateUsd } from "./providers/pricing";
import { selectProvider } from "./providers/select";
import { ProviderError } from "./providers/types";
import type { BrainProvider, CompletionInput, ProviderUsage } from "./providers/types";
import {
  CHAT_OUTPUT_JSON_SCHEMA,
  chatRequestSchema,
  chatResponseSchema,
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

const issuesOf = (e: { issues: ReadonlyArray<{ path: PropertyKey[]; message: string }> }) =>
  e.issues.map((i): Issue => ({ path: i.path.map(String).join("."), message: i.message }));

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
    return {
      ok: false,
      res: errorResponse(c, 400, "bad_request", "request failed validation", {
        issues: issuesOf(parsed.error),
      }),
    };
  }
  return { ok: true, data: parsed.data };
}

/** Wire usage for one route call: tokens summed over every upstream call (all of them are billed). */
function toUsage(calls: ProviderUsage[]): Usage {
  const sum: ProviderUsage = {
    inputTokens: calls.reduce((n, u) => n + u.inputTokens, 0),
    outputTokens: calls.reduce((n, u) => n + u.outputTokens, 0),
    model: calls[0]?.model ?? "unknown",
  };
  return { ...sum, neurons: estimateNeurons(sum) };
}

/**
 * Cost headers. X-Eden-Usd-Cost is an addition to the spec's list: it is only sent for models with
 * a USD price row (providers billed to the player's own key). It is priced per upstream call
 * because some rates depend on each call's prompt length.
 */
function withCostHeaders(c: Context<AppContext>, usage: Usage, calls: ProviderUsage[]) {
  c.header("X-Eden-Neurons-Cost", usage.neurons.toString());
  c.header("X-Eden-Model", usage.model);
  let usd = 0;
  for (const call of calls) {
    const v = estimateUsd(call);
    if (v === undefined) return;
    usd += v;
  }
  if (calls.length > 0) c.header("X-Eden-Usd-Cost", (Math.round(usd * 1e8) / 1e8).toString());
}

interface Failed {
  issues: Issue[];
  raw: string;
  calls: ProviderUsage[];
}

type Validated<T> = ({ ok: true; data: T } | ({ ok: false } & Failed)) & {
  calls: ProviderUsage[];
};

/** One completion parsed through `schema`, with exactly one repair retry (AGENTS.md, "Validation"). */
async function completeValidated<T>(
  provider: BrainProvider,
  input: CompletionInput,
  schema: ZodType<T>,
): Promise<Validated<T>> {
  const first = await provider.complete(input);
  const calls = [first.usage];
  const parsed = schema.safeParse(extractJson(first.text));
  if (parsed.success) return { ok: true, data: parsed.data, calls };
  const second = await provider.complete({
    ...input,
    user: `${input.user}\n\n${repairPrompt(first.text, issuesOf(parsed.error))}`,
  });
  calls.push(second.usage);
  const retried = schema.safeParse(extractJson(second.text));
  if (retried.success) return { ok: true, data: retried.data, calls };
  return { ok: false, issues: issuesOf(retried.error), raw: second.text, calls };
}

function invalidOutput(c: Context<AppContext>, failed: Failed) {
  const u = toUsage(failed.calls);
  withCostHeaders(c, u, failed.calls);
  console.warn("invalid model output", JSON.stringify(failed.raw.slice(0, 600)));
  return errorResponse(c, 422, "invalid_output", "model output failed schema after repair retry", {
    issues: failed.issues,
    raw: failed.raw.slice(0, 400),
    usage: u,
  });
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
  const { system, user } = buildThinkPrompt(body.data);
  const result = await completeValidated(
    c.get("provider"),
    {
      system,
      user,
      maxTokens: 600,
      temperature: 0.7,
      jsonSchema: THINK_OUTPUT_JSON_SCHEMA as unknown as Record<string, unknown>,
    },
    thinkResponseSchema,
  );
  if (!result.ok) return invalidOutput(c, result);
  const response = result.data;
  if (!body.data.allowInvention) response.invention = null;
  if (response.plan.length > body.data.maxGoals)
    response.plan = response.plan.slice(0, body.data.maxGoals);
  const u = toUsage(result.calls);
  withCostHeaders(c, u, result.calls);
  return c.json({ response, usage: u });
});

app.post("/chat", async (c) => {
  const body = await parseBody(c, chatRequestSchema);
  if (!body.ok) return body.res;
  const { system, user } = buildChatPrompt(body.data);
  const result = await completeValidated(
    c.get("provider"),
    {
      system,
      user,
      // A reply is at most 600 chars plus an optional memory line: ≈ 250 tokens of JSON.
      maxTokens: 400,
      temperature: 0.8,
      jsonSchema: CHAT_OUTPUT_JSON_SCHEMA as unknown as Record<string, unknown>,
    },
    chatResponseSchema,
  );
  if (!result.ok) return invalidOutput(c, result);
  const u = toUsage(result.calls);
  withCostHeaders(c, u, result.calls);
  return c.json({ response: result.data, usage: u });
});

app.post("/embed", async (c) => {
  const body = await parseBody(c, embedRequestSchema);
  if (!body.ok) return body.res;
  const provider: BrainProvider = c.get("provider");
  const { vectors, usage } = await provider.embed(body.data.texts);
  const u = toUsage([usage]);
  withCostHeaders(c, u, [usage]);
  return c.json({
    response: { model: usage.model, dimensions: vectors[0]?.length ?? 0, vectors },
    usage: u,
  });
});

app.notFound((c) =>
  errorResponse(c, 404 as unknown as 400, "not_found", `no route ${c.req.method} ${c.req.path}`),
);

export default app;
