// Zod mirrors of docs/specs/contracts/*.schema.json. schemas.test.ts proves they agree with the
// JSON schemas on every shared fixture. Change both sides together.
import { z } from "zod";

export const ACTIONS = [
  "idle",
  "eat",
  "drink",
  "rest",
  "gather",
  "hunt",
  "build",
  "farm",
  "craft",
  "court",
  "teach",
  "learn",
  "trade",
  "raid",
  "flee",
  "pray",
  "explore",
  "talk",
  "give",
  "settle",
  "invent",
] as const;

export const actionSchema = z.enum(ACTIONS);
export const importanceSchema = z.number().int().min(1).max(10);
export const unitIntervalSchema = z.number().min(0).max(1);
export const shortTextSchema = z.string().min(1).max(280);
export const longTextSchema = z.string().max(4000);
export const identifierSchema = z.string().regex(/^[a-z][a-z0-9-]{0,47}$/);

const boundedRecord = (max: number) =>
  z
    .record(z.string(), unitIntervalSchema)
    .refine((o) => Object.keys(o).length <= max, { message: `at most ${max} keys` });

export const memoryLineSchema = z.strictObject({
  tick: z.number().int().min(0),
  text: shortTextSchema,
  importance: importanceSchema,
});

export const npcContextSchema = z.strictObject({
  id: z.number().int().min(0),
  name: z.string().min(1).max(64),
  ageYears: z.number().int().min(0).max(200),
  persona: longTextSchema,
  traits: boundedRecord(32),
  needs: boundedRecord(16),
  knowledge: z.array(identifierSchema).max(64),
  currentGoals: z.array(shortTextSchema).max(8),
  location: shortTextSchema,
});

export const usageSchema = z.strictObject({
  inputTokens: z.number().int().min(0),
  outputTokens: z.number().int().min(0),
  neurons: z.number().min(0),
  model: z.string(),
});

export const ERROR_CODES = [
  "bad_request",
  "unauthorized",
  "invalid_output",
  "provider_error",
  "budget_exceeded",
  "unsupported",
  "not_found",
] as const;

export const errorSchema = z.strictObject({
  error: z.strictObject({
    code: z.enum(ERROR_CODES),
    message: z.string(),
    details: z.unknown().optional(),
  }),
});

// ---- concepts ----

export const effectSchema = z.discriminatedUnion("type", [
  z.strictObject({
    type: z.literal("yieldMod"),
    resource: identifierSchema,
    factor: z.number().min(0.5).max(3),
  }),
  z.strictObject({ type: z.literal("unlockBuilding"), building: identifierSchema }),
  z.strictObject({ type: z.literal("unlockTool"), tool: identifierSchema }),
  z.strictObject({ type: z.literal("unlockAction"), action: actionSchema }),
  z.strictObject({ type: z.literal("belief"), text: shortTextSchema }),
]);

export const conceptSchema = z.strictObject({
  id: identifierSchema,
  name: z.string().min(1).max(48),
  description: z.string().min(1).max(400),
  prerequisites: z
    .array(identifierSchema)
    .max(6)
    .refine((a) => new Set(a).size === a.length, { message: "prerequisites must be unique" }),
  difficulty: unitIntervalSchema,
  effects: z.array(effectSchema).min(1).max(6),
  tags: z.array(identifierSchema).max(8).optional(),
});

export const conceptsFileSchema = z.array(conceptSchema);

// ---- /think ----

export const thinkRequestSchema = z.strictObject({
  npc: npcContextSchema,
  memories: z.array(memoryLineSchema).max(40),
  worldRules: longTextSchema,
  situation: longTextSchema,
  maxGoals: z.number().int().min(1).max(5).default(3),
  allowInvention: z.boolean().default(false),
});

export const goalSchema = z.strictObject({
  action: actionSchema,
  target: z.string().max(64).nullish(),
  reason: z.string().max(200).nullish(),
});

export const beliefSchema = z.strictObject({ text: shortTextSchema, importance: importanceSchema });

export const thinkResponseSchema = z.strictObject({
  plan: z.array(goalSchema).max(5),
  say: z.string().max(280).nullish(),
  belief: beliefSchema.nullish(),
  invention: conceptSchema.nullish(),
});

export const thinkResultSchema = z.strictObject({
  response: thinkResponseSchema,
  usage: usageSchema,
});

// ---- /chat ----

export const speakerSchema = z.enum(["god", "npc", "possessed"]);
export const MOODS = ["calm", "happy", "afraid", "angry", "sad", "awed", "suspicious"] as const;

export const chatTurnSchema = z.strictObject({
  speaker: speakerSchema,
  text: z.string().min(1).max(1000),
});

export const chatRequestSchema = z.strictObject({
  npc: npcContextSchema,
  memories: z.array(memoryLineSchema).max(40),
  worldRules: longTextSchema,
  speaker: speakerSchema,
  speakerName: z.string().min(1).max(64),
  history: z.array(chatTurnSchema).max(20),
  message: z.string().min(1).max(1000),
});

export const chatResponseSchema = z.strictObject({
  reply: z.string().min(1).max(600),
  memory: beliefSchema.nullish(),
  mood: z.enum(MOODS).nullish(),
});

export const chatResultSchema = z.strictObject({
  response: chatResponseSchema,
  usage: usageSchema,
});

// ---- /embed ----

export const embedRequestSchema = z.strictObject({
  texts: z.array(z.string().min(1).max(2000)).min(1).max(64),
});

export const embedResponseSchema = z.strictObject({
  model: z.string(),
  dimensions: z.number().int().min(1),
  vectors: z.array(z.array(z.number()).min(1)).min(1),
});

export const embedResultSchema = z.strictObject({
  response: embedResponseSchema,
  usage: usageSchema,
});

export type ThinkRequest = z.infer<typeof thinkRequestSchema>;
export type ThinkResponse = z.infer<typeof thinkResponseSchema>;
export type ChatRequest = z.infer<typeof chatRequestSchema>;
export type ChatResponse = z.infer<typeof chatResponseSchema>;
export type EmbedRequest = z.infer<typeof embedRequestSchema>;
export type EmbedResponse = z.infer<typeof embedResponseSchema>;
export type Usage = z.infer<typeof usageSchema>;
export type ErrorBody = z.infer<typeof errorSchema>;
export type ErrorCode = (typeof ERROR_CODES)[number];

/** The JSON Schema handed to the model as response_format (a plain, self-contained subset). */
export const THINK_OUTPUT_JSON_SCHEMA = {
  type: "object",
  additionalProperties: false,
  required: ["plan"],
  properties: {
    plan: {
      type: "array",
      maxItems: 5,
      items: {
        type: "object",
        additionalProperties: false,
        required: ["action"],
        properties: {
          action: { type: "string", enum: [...ACTIONS] },
          target: { type: ["string", "null"], maxLength: 64 },
          reason: { type: ["string", "null"], maxLength: 200 },
        },
      },
    },
    say: { type: ["string", "null"], maxLength: 280 },
    belief: {
      type: ["object", "null"],
      additionalProperties: false,
      required: ["text", "importance"],
      properties: {
        text: { type: "string", maxLength: 280 },
        importance: { type: "integer", minimum: 1, maximum: 10 },
      },
    },
  },
} as const;

/** The JSON Schema handed to the model as response_format for /chat (a subset of chat.schema.json#/$defs/response). */
export const CHAT_OUTPUT_JSON_SCHEMA = {
  type: "object",
  additionalProperties: false,
  required: ["reply"],
  properties: {
    reply: { type: "string", minLength: 1, maxLength: 600 },
    memory: {
      type: ["object", "null"],
      additionalProperties: false,
      required: ["text", "importance"],
      properties: {
        text: { type: "string", maxLength: 280 },
        importance: { type: "integer", minimum: 1, maximum: 10 },
      },
    },
    mood: { type: ["string", "null"], enum: [...MOODS, null] },
  },
} as const;
