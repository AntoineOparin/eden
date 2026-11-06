import type { BrainProvider } from "./providers/types";

/** Bindings and secrets the Worker reads. Vars come from wrangler.jsonc, secrets from .dev.vars / `wrangler secret put`. */
export interface AppEnv {
  AI: Ai;
  BRAIN_PROVIDER?: string;
  THINK_MODEL?: string;
  EMBED_MODEL?: string;
  EDEN_SHARED_SECRET?: string;
  /** Secret. Enables provider "anthropic". */
  ANTHROPIC_API_KEY?: string;
  /** Optional, default claude-haiku-5-5. */
  ANTHROPIC_MODEL?: string;
  /** Secret. Optional for local servers (Ollama, LM Studio). */
  OPENAI_API_KEY?: string;
  /** API root with version segment, e.g. https://api.openai.com/v1. Enables "openai-compatible". */
  OPENAI_BASE_URL?: string;
  /** Optional, default gpt-5-mini. */
  OPENAI_MODEL?: string;
  /** Optional, default text-embedding-3-small. */
  OPENAI_EMBED_MODEL?: string;
}

export type AppContext = { Bindings: AppEnv; Variables: { provider: BrainProvider } };
