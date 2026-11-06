// Validates docs/specs/contracts/*.schema.json (draft 2020-12) and every fixture in
// docs/specs/contracts/fixtures/. Fixture naming: <schema>.<def>.<ok|bad>[.n].json, or
// <schema>.<ok|bad>[.n].json for the root schema. Exit code 1 on any failure.
import { readdirSync, readFileSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import Ajv2020 from "ajv/dist/2020.js";
import addFormats from "ajv-formats";

const root = join(dirname(fileURLToPath(import.meta.url)), "..", "docs", "specs", "contracts");
const ajv = new Ajv2020({ strict: true, allErrors: true, allowUnionTypes: true });
addFormats(ajv);

const schemaFiles = readdirSync(root).filter((f) => f.endsWith(".schema.json"));
for (const f of schemaFiles) {
  const schema = JSON.parse(readFileSync(join(root, f), "utf8"));
  ajv.addSchema(schema, schema.$id);
}
// Compiling every schema catches bad $refs and strict-mode violations.
for (const f of schemaFiles) ajv.getSchema(`https://eden.local/contracts/${f}`) ?? fail(`cannot compile ${f}`);

let failures = 0;
const fixtures = readdirSync(join(root, "fixtures")).filter((f) => f.endsWith(".json")).sort();
for (const f of fixtures) {
  const parts = f.replace(/\.json$/, "").split(".");
  const schemaName = parts[0];
  const verdictIdx = parts.findIndex((p, i) => i > 0 && (p === "ok" || p === "bad"));
  const def = verdictIdx === 1 ? null : parts.slice(1, verdictIdx).join(".");
  const expectOk = parts[verdictIdx] === "ok";
  const ref = `https://eden.local/contracts/${schemaName}.schema.json${def ? `#/$defs/${def}` : ""}`;
  const validate = ajv.getSchema(ref) ?? fail(`no schema ${ref} for fixture ${f}`);
  const data = JSON.parse(readFileSync(join(root, "fixtures", f), "utf8"));
  const ok = validate(data);
  if (ok === expectOk) {
    console.log(`ok   ${f}`);
  } else {
    failures++;
    console.log(`FAIL ${f}: expected ${expectOk ? "valid" : "invalid"}`);
    if (!ok) console.log(ajv.errorsText(validate.errors, { separator: "\n     " }));
  }
}
console.log(`${schemaFiles.length} schemas, ${fixtures.length} fixtures, ${failures} failures`);
process.exit(failures ? 1 : 0);

function fail(msg) {
  console.error(msg);
  process.exit(1);
}
