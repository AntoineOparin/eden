# 30 — brain/: Cloudflare Worker

**Milestone:** skeleton in the M1/M2 parallel phase, complete for M5. Lives in `brain/`.

## Purpose

A stateless HTTPS façade over LLM providers so the game needs no API key by default. Hono +
Zod on Cloudflare Workers, Workers AI free tier as the default provider. Validates every input
and every model output. Reports per-request cost so the client can budget.

## Routes

All JSON. Auth: header `X-Eden-Key` must equal secret `EDEN_SHARED_SECRET` (401 otherwise).
Optional `X-Eden-Provider: workers-ai | anthropic | openai-compatible` overrides env
`BRAIN_PROVIDER`; non-default providers need their key set with `wrangler secret put`.

| Route | Request | 200 body | Notes |
|---|---|---|---|
| `GET /health` | — | `{ ok, provider, models }` | no auth |
| `POST /think` | `think.schema.json#/$defs/request` | `#/$defs/result` | one repair retry on invalid output |
| `POST /chat` | `chat.schema.json#/$defs/request` | `#/$defs/result` | one repair retry on invalid output |
| `POST /embed` | `embed.schema.json#/$defs/request` | `#/$defs/result` | bge-m3, ≤ 64 texts; 501 on `anthropic` (no embeddings API) |

Errors use `common.schema.json#/$defs/error`: 400 `bad_request`, 401 `unauthorized`,
422 `invalid_output`, 502 `provider_error`, 501 `unsupported`.

Response headers on every 2xx: `X-Eden-Neurons-Cost` (float, this request's estimate) and
`X-Eden-Model`. The Worker is stateless, so "remaining budget" is tracked by the client from
these per-request costs (deviation from the plan's "remaining estimate" header, by design).
Providers billed to the player's own key (`anthropic`, `openai-compatible` with a priced model)
report 0 Neurons and add `X-Eden-Usd-Cost` (USD estimate, from `USD_PER_M` in `pricing.ts`).

## Models and cost

| Use | Env var | Default | Neurons / M tokens (in, out) |
|---|---|---|---|
| think, chat | `THINK_MODEL` | `@cf/google/gemma-4-26b-a4b-it` | 9091 / 27273 |
| embed | `EMBED_MODEL` | `@cf/baai/bge-m3` | 1075 / — |

Gemma 4 (256k context) was chosen after a live comparison on 2026-11-06 (think fixture, 3 runs
each, all valid JSON): best in-character plans and beliefs, 2.1–3.6 s and 5.3–6.0 Neurons per
thought; glm-4.7-flash was 12% cheaper but 2x slower; qwen3 is deprecated on 2026-11-20.
Known alternatives already priced in `src/providers/pricing.ts`: `@cf/zai-org/glm-4.7-flash`
(5500 / 36400) and `@cf/qwen/qwen3-30b-a3b-fp8` (4625 / 30475, planned deprecation 2026-11-20).
Switching is an env var change plus a pricing row, no code change. Free tier is 10,000
Neurons/day; with Gemma a large thought (≈ 900 in, 120 out) costs ≈ 11.5 Neurons, and the real
think prompt (≈ 370 in, 140 out) ≈ 5–7 Neurons, so ≈ 1,500+ thoughts per free day.

Workers AI text generation returns the OpenAI-style `chat.completion` shape
(`choices[0].message.content`, `usage.prompt_tokens/completion_tokens`); the adapter must also
accept the legacy `{ response }` shape (Gemma adds a `message.refusal` field, ignored). Verified
live on 2026-11-06 against qwen3:

- Without `chat_template_kwargs: { enable_thinking: false }` the model spends the whole
  `max_tokens` inside its reasoning and returns no JSON. The adapter sends that flag for
  qwen3 / gemma-4 / glm-4 (`supportsThinkingToggle`).
- With thinking disabled, Workers AI's reasoning parser can still put the entire answer in
  `message.reasoning_content` / `message.reasoning` and leave `content: null`. The adapter takes
  the first non-empty of `content`, `reasoning_content`, `reasoning`, `response`.
- `<think>…</think>` blocks and ``` fences are stripped before JSON parsing (`json-repair.ts`).
- JSON mode (`response_format: { type: "json_schema" }`) is requested but not relied on: output
  is always parsed and Zod-validated, with one repair prompt on failure, then 422 with `raw`.
- With qwen3 a real thought with this prompt cost ≈ 5–6 Neurons (≈ 370 in, 140 out).

## Code layout

```
brain/
  wrangler.jsonc          name eden-brain, nodejs_compat, ai binding "AI", vars BRAIN_PROVIDER/THINK_MODEL/EMBED_MODEL
  src/index.ts            Hono app: auth middleware, routes, error envelope, cost headers
  src/schemas.ts          Zod mirrors of docs/specs/contracts/*.schema.json (+ z.infer types)
  src/prompts.ts          system/user prompt builders for think and chat (pure, tested)
  src/json-repair.ts      strip <think>, extract first JSON object, repair-prompt builder
  src/providers/types.ts  BrainProvider interface (think, chat, embed) + ProviderUsage
  src/providers/workers-ai.ts     default
  src/providers/anthropic.ts      optional, Messages API, claude-haiku-5-5 default (ANTHROPIC_MODEL)
  src/providers/openai-compatible.ts  optional, /chat/completions + /embeddings (OPENAI_BASE_URL)
  src/providers/chat-completion.ts    shared reader for the OpenAI-style completion shape
  src/providers/http.ts           fetch wrapper for upstream APIs, errors -> ProviderError
  src/providers/pricing.ts        neurons per token per model, USD per token for keyed providers
  src/*.test.ts           vitest; routes tested with app.request() and a fake AI binding
```

## Conventions

See `AGENTS.md` (no module state, no `process.env`, `.dev.vars` for local secrets,
`npm run cf-typegen` after editing `wrangler.jsonc`). Never call Workers AI from tests. Keep
CPU per request under 10 ms: no heavy string work, no synchronous crypto beyond the key compare.

## Acceptance tests

- `npm run check` (prettier + tsc) and `npm test` green.
- `schemas.test.ts`: every fixture in `docs/specs/contracts/fixtures/` agrees with Zod
  (ok parses, bad fails) and with ajv against the JSON schema.
- `index.test.ts`: 401 without key; 400 on bad body with Zod issues in `details`; `/think`
  happy path with fake AI returning valid JSON; repair path (first invalid, second valid)
  yields 200; double failure yields 422; `/embed` maps `data` to `vectors`; cost header present.
- Smoke (manual, done once in M0): `npx wrangler dev`, `curl /health`, `/embed`, `/think` with
  the fixtures return 200 against real Workers AI.
- M5: deployed with `wrangler deploy` (asked first), game talks to it end to end.

## Carried-over items

See `90-backlog.md`; the rows naming your milestone are part of your scope.

## Do not touch

- Anything under `game/`. The C# `WorkerProxyProvider` belongs to `20-mind.md`; if the wire
  format must change, change the JSON schema in `contracts/` and say so.
- Do not add KV, D1, Durable Objects, Queues, AI Gateway with paid features, or any binding
  beyond `AI` without asking.
