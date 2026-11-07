# Brief: brain Worker, M5-ready (launched 2026-11-06, base 9ab043a, branch worker-m5-ready)

You are working in an isolated git worktree of the Eden repo (origin at ~/Projects/eden, base commit 9ab043a "M0"). Your working directory is the worktree root; do all work there and nowhere else.

Read AGENTS.md first, then docs/specs/30-brain-worker.md and the JSON schemas + fixtures in docs/specs/contracts/. The Worker in brain/ already has /health, /embed and /think working against Workers AI (verified live today), Zod schemas, pricing, json repair and 43 vitest tests.

Goal: finish everything the spec lists except deployment.
1. Implement POST /chat (currently 501) using the existing buildChatPrompt and chatResponseSchema with the same one-repair-retry flow, cost headers and error envelope as /think.
2. Implement the anthropic and openai-compatible provider adapters (currently stubs in brain/src/providers/) behind X-Eden-Provider / BRAIN_PROVIDER, keys from env secrets only (ANTHROPIC_API_KEY, OPENAI_API_KEY, OPENAI_BASE_URL), with pricing rows added to pricing.ts. Check Anthropic model ids (claude-haiku-5-5, claude-sonnet-5-5) and prices against the live docs at https://docs.claude.com (use web search/fetch), never from memory; note in the code where each number came from. Anthropic has no embeddings endpoint, so embed must return a clear 501 for that provider.
3. Keep the Workers AI quirks from the spec intact: enable_thinking off for qwen3/gemma-4/glm-4, answers may land in reasoning_content, outputs always Zod-validated, 422 with raw text on double failure.
4. Add tests for every new path using app.request() and fake bindings or a mocked global fetch; never call a real API from tests. Keep brain/src/schemas.test.ts green: brain/src/schemas.ts must stay equivalent to the JSON schemas.

Constraints: keep the Worker stateless (no module state, no KV/D1/Durable Objects/Queues, no new bindings), keep CPU per request under 10 ms, never read process.env, run `npm run cf-typegen` if you edit wrangler.jsonc. Do not touch game/ or docs/specs/contracts/ (if the wire format must change, stop and explain in your report). Do not run `wrangler deploy` and do not enable any paid Cloudflare feature. wrangler is used via `npx wrangler` and is already logged in; copy brain/.dev.vars.example to brain/.dev.vars in your worktree before running wrangler dev.

Definition of done: `cd brain && npm run check && npm test` green, `./scripts/check.sh` green from the worktree root, and a `npx wrangler dev` smoke test of /chat and /think with the fixtures in docs/specs/contracts/fixtures/ (costs a few free-tier Neurons; report the X-Eden-Neurons-Cost values you saw and the replies). Commit on a branch named worker-m5-ready with a message ending in "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>". Do not merge or push.

Final report: what you built (file list), test counts, the smoke-test results, the Anthropic prices you verified and their source URLs, and anything you could not finish and why.

Mid-run addendum (user decision): make @cf/google/gemma-4-26b-a4b-it the default think/chat model in wrangler.jsonc, select.ts, the spec table (qwen3 becomes an alternative with its 2026-11-20 deprecation note, cite the live comparison), tests, and re-run the smoke test on Gemma.
