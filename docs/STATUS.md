# Status

## 2026-11-06 — M5 minds

**What.** Eden.Mind is complete: `MindPool` (spotlight every in-game hour, thoughts every 4 in-game
hours staggered by id, reflection at day end, embeddings on promotion, priority queue
dialogue > reflection > thought > embeddings, concurrency from settings, 20 s task timeout, daily
Neuron budget with the 90 % rule, 5 s → 5 min backoff, rules-only fallback), Generative Agents
retrieval over bge-m3 embeddings cached per memory text, `IntentionMapper`, the completed
`WorkerProxyProvider` (surfaces `X-Eden-Usd-Cost`), and direct `AnthropicProvider` /
`OpenAiCompatibleProvider` (C#, checked against the live docs). Eden.Sim: `SetThinkingCommand`
(contract change), `IMindSnapshot` extras on `WorldSnapshot`, drama tick per NPC. Eden.Game: dialogue
panel, brain settings panel (key AES-encrypted in `user://`), HUD budget meter, inspector thought line
from the Mind, god-powers toolbar (Select / Lightning / Whisper), `--m5-check` scripted acceptance run.

**How verified.**

- `./scripts/check.sh` green from the worktree root.
- Eden.Sim.Tests: 207 (was 153): 205 non-Slow in 0.8 s plus the 2 existing Slow tests, whole run
  1 m 8 s as before. The 54 new tests are under `Mind/`. `MindPoolTests` runs in a non-parallel
  collection, because it timed out while the Slow tests saturated the thread pool. The Mind tests
  ran 6× in a row without a flake.
- Eden.Game.Tests (gdUnit4): 23 (was 19): `MindUiTests` (settings round trip, key encryption,
  budget meter render, dialogue panel through a fake provider) and extended `MainSceneTests`.
- Manual M5 check against `wrangler dev` on :8787 (Workers AI, Gemma 4 + bge-m3), seed 20,
  `godot --path game -- --seed=20 --m5-check --m5-year=10`: fast-forward at 500× until a village has
  a leader, select the leader, strike lightning 7 m away (radius 3), then speak through the dialogue
  panel with a line that does not mention the strike. Final two runs:
  - Run 4, Hamelah, leader of Elderford (13 people), year 18.
    God: "Tell me, how do your people fare today?"
    Hamelah: "We are weary, and the sky has already shown its teeth today with a bolt from the blue.
    My people seek food and rest, though our hearts are heavy with questions."
    Meter: `Neurons 482.9 / 9000  worker: ok  thinking 11`; that run cost 163.45 Neurons.
  - Run 5, Vaveek, leader of Marstead (11 people), year 18.
    God: "Tell me, how do your people fare today?"
    Vaveek: "The sky speaks, but it brings no rain, only a strange voice. My people are fed and warm,
    but the lightning that struck from nothing makes my heart heavy with doubt."
    Meter: `Neurons 589.0 / 9000  worker: ok  thinking 9`; that run cost 106.11 Neurons.
  - Runs 1 and 3 (before the "still shaken" persona line) answered only obliquely ("under the eye
    that watches and judges", "we look to the horizon with wary eyes"); run 2 named the lightning.
    This led to the prompt-context change in DECISIONS.
- Worker log for the whole session: 72 `/think`, 5 `/chat`, 82 `/embed`, no 4xx/5xx.

**Numbers.** Neurons spent in total (all five runs, per the budget meter): 589.0, about 6 % of
one free day. A run costs about 75–165 Neurons: roughly 6 per thought or reply, ≈ 0.1 per embed
call, most of it background thoughts during the fast-forward. Dialogue round trip ≈ 2–4 s.

**Not verified here.** A pasted Anthropic key (no key; covered by stub tests and the encrypted
settings round trip). The budget cap against the live Worker (covered by `MindPoolTests`). The Worker
is not deployed.
