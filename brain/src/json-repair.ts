/** Turns raw model text into a JSON value, tolerating reasoning blocks, fences and chatter. */
export function extractJson(text: string): unknown | undefined {
  let s = text.replace(/<think>[\s\S]*?<\/think>/gi, "").trim();
  // Unterminated reasoning block: drop everything up to the last close tag or the first brace.
  s = s.replace(/^[\s\S]*?<\/think>/i, "").trim();
  s = s.replace(/^```(?:json)?\s*/i, "").replace(/\s*```$/, "");
  const direct = tryParse(s);
  if (direct !== undefined) return direct;
  const start = s.indexOf("{");
  const end = s.lastIndexOf("}");
  if (start === -1 || end <= start) return undefined;
  return tryParse(s.slice(start, end + 1));
}

function tryParse(s: string): unknown | undefined {
  try {
    return JSON.parse(s) as unknown;
  } catch {
    return undefined;
  }
}

export interface Issue {
  path: string;
  message: string;
}

/** The single repair turn: show the model what it produced and what was wrong. */
export function repairPrompt(previous: string, issues: Issue[]): string {
  const list = issues
    .slice(0, 12)
    .map((i) => `- ${i.path || "(root)"}: ${i.message}`)
    .join("\n");
  return [
    "Your previous answer was not valid. Return ONLY a corrected JSON object, no prose, no markdown.",
    "Problems:",
    list || "- output was not JSON",
    "Previous answer:",
    previous.slice(0, 2000),
  ].join("\n");
}
