import type { BrainProvider } from "./providers/types";

/** Bindings and secrets the Worker reads. Vars come from wrangler.jsonc, secrets from .dev.vars / `wrangler secret put`. */
export interface AppEnv {
  AI: Ai;
  BRAIN_PROVIDER?: string;
  THINK_MODEL?: string;
  EMBED_MODEL?: string;
  EDEN_SHARED_SECRET?: string;
  ANTHROPIC_API_KEY?: string;
  OPENAI_API_KEY?: string;
  OPENAI_BASE_URL?: string;
}

export type AppContext = { Bindings: AppEnv; Variables: { provider: BrainProvider } };
