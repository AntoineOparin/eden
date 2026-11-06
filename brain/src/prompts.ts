import { ACTIONS, MOODS } from "./schemas";
import type { ChatRequest, ThinkRequest } from "./schemas";

const pct = (v: number) => `${Math.round(v * 100)}%`;

function describeNpc(npc: ThinkRequest["npc"]): string {
  const traits = Object.entries(npc.traits)
    .map(([k, v]) => `${k} ${pct(v)}`)
    .join(", ");
  const needs = Object.entries(npc.needs)
    .map(([k, v]) => `${k} ${pct(v)}`)
    .join(", ");
  return [
    `You are ${npc.name}, ${npc.ageYears} years old. ${npc.persona}`,
    traits ? `Traits: ${traits}.` : "",
    needs ? `Needs (100% = fully satisfied): ${needs}.` : "",
    npc.knowledge.length
      ? `You know: ${npc.knowledge.join(", ")}.`
      : "You know almost nothing yet.",
    npc.currentGoals.length ? `Current goals: ${npc.currentGoals.join("; ")}.` : "",
    `You are at ${npc.location}.`,
  ]
    .filter(Boolean)
    .join("\n");
}

function describeMemories(memories: ThinkRequest["memories"]): string {
  if (memories.length === 0) return "You remember nothing notable.";
  return (
    "What you remember (most relevant first):\n" +
    memories.map((m) => `- [importance ${m.importance}] ${m.text}`).join("\n")
  );
}

export function buildThinkPrompt(req: ThinkRequest): { system: string; user: string } {
  const system = [
    req.worldRules,
    "You decide what this person does next. Think like them, not like an assistant.",
    `Allowed actions: ${ACTIONS.join(", ")}.`,
    "Respond with ONLY a JSON object of this exact shape:",
    `{"plan":[{"action":"<one allowed action>","target":"<optional short target or null>","reason":"<short reason or null>"}],"say":"<one short line spoken aloud, or null>","belief":{"text":"<a belief formed now>","importance":1-10} or null}`,
    `plan has at most ${req.maxGoals} entries. No markdown, no commentary, no keys other than plan, say, belief.`,
  ].join("\n");
  const user = [
    describeNpc(req.npc),
    describeMemories(req.memories),
    `Right now: ${req.situation}`,
  ].join("\n\n");
  return { system, user };
}

export function buildChatPrompt(req: ChatRequest): { system: string; user: string } {
  const who =
    req.speaker === "god"
      ? `A voice from the sky that calls itself "${req.speakerName}" speaks to you. You may be awed, afraid or doubtful.`
      : `${req.speakerName} speaks to you face to face.`;
  const system = [
    req.worldRules,
    describeNpc(req.npc),
    describeMemories(req.memories),
    who,
    "Reply in character in one to three sentences.",
    `Respond with ONLY a JSON object: {"reply":"<what you say>","memory":{"text":"<what you will remember of this>","importance":1-10} or null,"mood":"<one of ${MOODS.join("|")}> or null}`,
  ].join("\n\n");
  const history = req.history.map(
    (t) => `${t.speaker === "npc" ? req.npc.name : req.speakerName}: ${t.text}`,
  );
  const user = [...history, `${req.speakerName}: ${req.message}`].join("\n");
  return { system, user };
}
