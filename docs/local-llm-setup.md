# Local LLM Setup (Jan)

This runbook gets a local model running for LifeSim Engine in under 30 minutes on a fresh
machine. LifeSim talks to any OpenAI-compatible endpoint, but the default and recommended
host is **[Jan](https://jan.ai)** on `http://127.0.0.1:1337/v1`.

> LifeSim is **AI-optional**. If you skip this entire guide the game still plays end-to-end
> offline using deterministic fallbacks. This guide is only needed for AI narration, dialogs
> and free-text commands.

---

## 1. What you need

- A machine with 8 GB+ RAM and (optionally) a discrete GPU. CPU-only works, just slower.
- ~4–15 GB of free disk space for one model.
- Jan installed (step 2).

---

## 2. Install Jan

1. Download the installer for your OS from <https://jan.ai/download>.
2. Install and launch Jan.
3. Confirm Jan's local API server can be started: **Settings → Advanced → Local API Server**.

   - Endpoint / port: `127.0.0.1:1337`
   - API prefix: `/v1`
   - Leave "Enable API" toggled **on**.

---

## 3. Pick and download a model

LifeSim needs a model with **strong instruction-following** and reliable **JSON mode**
behaviour — not just a good conversationalist. Small quantised models are enough.

| RAM (usable) | Recommended model (GGUF)          | Quant | Typical speed (CPU / GPU)       | Notes                                   |
| ------------ | --------------------------------- | ----- | ------------------------------- | --------------------------------------- |
| 8 GB         | `Llama-3.2-3B-Instruct`           | Q4_K_M | ~8–14 tok/s / 60–120 tok/s      | Smallest viable for JSON contracts      |
| 16 GB        | `Llama-3.1-8B-Instruct`           | Q4_K_M | ~5–8 tok/s / 50–100 tok/s       | Good default for most machines          |
| 16 GB        | `Qwen2.5-7B-Instruct`             | Q5_K_M | ~4–7 tok/s / 40–90 tok/s        | Strong JSON mode                        |
| 32 GB+       | `Llama-3.1-8B-Instruct` / 14B     | Q5_K_M | ~3–6 tok/s / 40–80 tok/s        | Room for larger context                 |

> The speeds above are **indicative ranges**, not measurements. Record the actual `tokens/sec`
> on your hardware (Jan shows it after each generation) and note it next to your choice —
> the M0-06 `probe` gate uses these numbers to set per-agent timeouts.

Download the model from Jan's built-in Hub (search by the name above). On first launch Jan
will offer to download a default model — you can keep it or replace it with one from the table.

---

## 4. Verify the endpoint with curl

With the local server running, confirm a completion round-trips. The request:

```bash
curl http://127.0.0.1:1337/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "Llama-3.1-8B-Instruct",
    "messages": [
      {"role": "system", "content": "Reply with JSON only."},
      {"role": "user", "content": "Return {\"ok\": true} and nothing else."}
    ],
    "temperature": 0
  }'
```

**Expected output** (trimmed): an HTTP 200 with a JSON body whose `choices[0].message.content`
contains valid JSON:

```json
{
  "id": "chatcmpl-…",
  "object": "chat.completion",
  "model": "Llama-3.1-8B-Instruct",
  "choices": [
    {
      "message": { "role": "assistant", "content": "{\"ok\": true}" },
      "finish_reason": "stop"
    }
  ]
}
```

If the model wraps the JSON in prose or emits `null`, it is too weak for structured output —
pick a larger model from the table. You can also list models with:

```bash
curl http://127.0.0.1:1337/v1/models
```

---

## 5. Point LifeSim at the model

Edit `~/.lifesim/config.json` (create it if missing) or set environment variables. The
default `appsettings.json` ships with these values:

```json
{
  "Llm": {
    "Endpoint": "http://127.0.0.1:1337/v1",
    "Model": "default",
    "ApiKey": ""
  }
}
```

Set `Llm:Model` to the exact id reported by `curl …/v1/models` (e.g. `Llama-3.1-8B-Instruct`).
If your local server is configured to require an API key, set `Llm:ApiKey` to that token
(LifeSim sends it as `Authorization: Bearer <ApiKey>`). Leave it empty when auth is disabled.
Environment variables win over the file:

```bash
# PowerShell
$env:LIFESIM_Llm__Model = "Llama-3.1-8B-Instruct"
$env:LIFESIM_Llm__Endpoint = "http://127.0.0.1:1337/v1"
$env:LIFESIM_Llm__ApiKey = "<your-api-key>"
```

---

## 6. Verify with the `doctor` command

```bash
dotnet run --project src/LifeSim.Console/LifeSim.Console.csproj -- doctor
```

Exit code `0` means the endpoint is reachable **and** the configured model is present.
Exit code `1` means something is wrong — the panel tells you which of the two checks failed.

---

## 7. Troubleshooting

| Symptom                                      | Likely cause                                  | Fix                                                                 |
| -------------------------------------------- | --------------------------------------------- | ------------------------------------------------------------------- |
| `doctor` reports "No response within 2s"     | Jan's local server is not running             | Start it in Jan → Settings → Local API Server                       |
| `doctor` probes the right URL but 404s       | Base URL missing `/v1`                        | Set `Llm:Endpoint` to `http://127.0.0.1:1337/v1` (note the `/v1`)   |
| `doctor` lists models but yours is missing   | Wrong `Llm:Model` spelling / wrong quant name | Copy the exact id from `curl …/v1/models` output                    |
| "Port 1337 already in use"                   | Another app holds the port                    | Free the port or change Jan's port and update `Llm:Endpoint`        |
| Very slow responses                          | Model too large for CPU-only inference        | Use a smaller quant (Q4) or a 3B model                              |
| Truncated / prose-wrapped JSON               | Model too weak or context too small           | Use a model from the table; raise context in Jan; keep prompts small|
| GPU not used (CPU pegged)                    | GPU offload not enabled                       | Enable GPU acceleration in Jan (needs compatible drivers)           |
| Out-of-memory during load                    | Model exceeds RAM                             | Pick a smaller model/quant for your tier                            |

For context-size settings, prefer 4096–8192 tokens in Jan; LifeSim's per-agent budgets are
tuned for that range and the context builder (M4-04) evicts long history anyway.

---

## 8. Model acceptance gate

Run the structured-output probe against the configured model:

```bash
dotnet run --project src/LifeSim.Console/LifeSim.Console.csproj -- probe
```

A model is **accepted** only if it passes all four cases (plain JSON, self-repair, short
roleplay, injection-as-data). The probe prints per-case latency, exits non-zero on failure,
and archives results to `docs/model-acceptance.md`.

Measured results for the reference machine (Ryzen 5 5600X / 32 GB / RTX 3060 Ti 8 GB) are
recorded in [docs/model-acceptance.md](model-acceptance.md): all four tested models pass the
gate; `Meta-Llama-3_1-8B-Instruct-Q4_K_M` is the default recommendation, with the Gemma-4
models preferred for Polish-language content.
