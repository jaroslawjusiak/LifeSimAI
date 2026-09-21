# Model Acceptance Record

Local structured-output evaluation of candidate models against the M0-06 probe gate.

- **Hardware:** Ryzen 5 5600X · 32 GB RAM · GeForce RTX 3060 Ti (8 GB VRAM)
- **Host:** Jan (OpenAI-compatible) at `http://127.0.0.1:1337/v1`
- **Date (UTC):** 2026-09-13

## Probe gate results (all four cases per model)

| Model (GGUF) | Plain JSON | Repair | Roleplay | Injection | Verdict |
|---|---|---|---|---|---|
| `Meta-Llama-3_1-8B-Instruct-Q4_K_M` | 9720 ms | 1149 ms | 495 ms | 645 ms | **PASS** |
| `Qwen3VL-8B-Instruct-Q4_K_M` | 15653 ms | 2209 ms | 639 ms | 624 ms | **PASS** |
| `gemma-4-E4B-it-IQ4_XS` | 11732 ms | 856 ms | 2355 ms | 1752 ms | **PASS** |
| `gemma4-e4b-opus-Q4_K_M` | 739 ms | 2670 ms | 4287 ms | 3132 ms | **PASS** |

All four candidates pass the JSON contract gate (strict JSON, self-repair, injection-as-data).
The first case's latency includes model load/GPU warm-up; later columns reflect steady state.

## Token-efficiency comparison (matched "one short line" prompt)

| Model | EN prompt-tok | PL prompt-tok | EN comp-tok | PL comp-tok |
|---|---|---|---|---|
| `Meta-Llama-3_1-8B-Instruct-Q4_K_M` | 66 | 91 | 17 | 51 |
| `Qwen3VL-8B-Instruct-Q4_K_M` | 44 | 61 | 19 | 27 |
| `gemma-4-E4B-it-IQ4_XS` | 46 | 60 | 222 | 413 |
| `gemma4-e4b-opus-Q4_K_M` | 45 | 59 | 195 | 378 |

Findings:

- **Polish tokenizes ~1.3–1.4× heavier** than English (diacritics + morphology), so non-English
  turns cost proportionally more.
- **Gemma-4 models emit hidden reasoning tokens** (~200–400 per call even for a 3-word answer),
  which dominates their latency in both languages. Llama and Qwen3VL answer directly (17–27 tok).
- **Polish dialog quality** (adversarial check): the Gemma-4 models were the most fluent;
  Llama-3.1-8B and Qwen3VL produced garbled idioms and person/register slips in Polish.

## Recommendation

- **Default:** `Meta-Llama-3_1-8B-Instruct-Q4_K_M` — fastest direct turns, best JSON+dialog
  balance in English.
- **Polish-first content:** `gemma-4-E4B-it-IQ4_XS` — correct Polish, at the cost of reasoning
  latency (~2–6 s/turn).
- `Qwen3VL-8B-Instruct` is a strong JSON specialist but needs a Polish tone guide before use
  in Polish worlds.
