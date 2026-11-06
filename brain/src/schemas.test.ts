import { readdirSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import Ajv2020 from "ajv/dist/2020.js";
import addFormats from "ajv-formats";
import { describe, expect, it } from "vitest";
import type { ZodType } from "zod";
import {
  chatRequestSchema,
  chatResponseSchema,
  chatResultSchema,
  conceptSchema,
  conceptsFileSchema,
  embedRequestSchema,
  embedResponseSchema,
  embedResultSchema,
  errorSchema,
  thinkRequestSchema,
  thinkResponseSchema,
  thinkResultSchema,
} from "./schemas";

const contractsDir = fileURLToPath(new URL("../../docs/specs/contracts/", import.meta.url).href);

const zodFor: Record<string, ZodType> = {
  "think.request": thinkRequestSchema,
  "think.response": thinkResponseSchema,
  "think.result": thinkResultSchema,
  "chat.request": chatRequestSchema,
  "chat.response": chatResponseSchema,
  "chat.result": chatResultSchema,
  "embed.request": embedRequestSchema,
  "embed.response": embedResponseSchema,
  "embed.result": embedResultSchema,
  concepts: conceptsFileSchema,
  "concepts.concept": conceptSchema,
  "common.error": errorSchema,
};

const ajv = new Ajv2020({ strict: true, allErrors: true, allowUnionTypes: true });
addFormats(ajv);
for (const f of readdirSync(contractsDir).filter((f) => f.endsWith(".schema.json"))) {
  const schema = JSON.parse(readFileSync(contractsDir + f, "utf8")) as { $id: string };
  ajv.addSchema(schema, schema.$id);
}

interface Fixture {
  file: string;
  key: string;
  ref: string;
  expectOk: boolean;
  data: unknown;
}

const fixtures: Fixture[] = readdirSync(contractsDir + "fixtures")
  .filter((f) => f.endsWith(".json"))
  .sort()
  .map((file) => {
    const parts = file.replace(/\.json$/, "").split(".");
    const verdictIdx = parts.findIndex((p, i) => i > 0 && (p === "ok" || p === "bad"));
    const def = verdictIdx === 1 ? null : parts.slice(1, verdictIdx).join(".");
    const key = def ? `${parts[0]}.${def}` : parts[0]!;
    return {
      file,
      key,
      ref: `https://eden.local/contracts/${parts[0]}.schema.json${def ? `#/$defs/${def}` : ""}`,
      expectOk: parts[verdictIdx] === "ok",
      data: JSON.parse(readFileSync(contractsDir + "fixtures/" + file, "utf8")) as unknown,
    };
  });

describe("contract fixtures", () => {
  it("every fixture has a Zod schema", () => {
    for (const f of fixtures) expect(zodFor[f.key], f.file).toBeDefined();
  });

  it.each(fixtures.map((f) => [f.file, f] as const))("%s agrees with Zod and ajv", (_, f) => {
    const zod = zodFor[f.key]!;
    const zodOk = zod.safeParse(f.data).success;
    const validate = ajv.getSchema(f.ref);
    expect(validate, f.ref).toBeDefined();
    const ajvOk = validate!(f.data) === true;
    expect(zodOk, `zod verdict for ${f.file}`).toBe(f.expectOk);
    expect(ajvOk, `ajv verdict for ${f.file}`).toBe(f.expectOk);
  });

  it("Zod action enum equals the JSON schema enum", () => {
    const common = JSON.parse(readFileSync(contractsDir + "common.schema.json", "utf8")) as {
      $defs: { action: { enum: string[] } };
    };
    expect(thinkResponseSchema.shape.plan.element.shape.action.options).toEqual(
      common.$defs.action.enum,
    );
  });
});
