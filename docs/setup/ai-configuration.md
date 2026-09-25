# AI Provider Configuration

Aircane Tabletop supports multiple AI providers. You can configure them through the UI (Settings → AI Provider) or via environment variables and user secrets.

## Out of the Box

Aircane ships with the **Fake** provider as the default. It requires no install, no API key, and no internet — the app runs immediately after setup and returns deterministic placeholder responses. This lets you explore the UI, import content, and run sessions before committing to a real AI provider.

When you're ready for real AI, pick one of the options below and switch to it in **Settings → AI Provider**. No restart or config editing required.

## Choosing a Provider

**Easiest path to real quality:** **OpenAI GPT-4o-mini** — paste an API key in Settings and you're done. Cloud, paid per token.

**Fully offline / local-first play:** **Ollama** — free and private, but requires installing Ollama and pulling a model (see below). Best if you want zero cloud dependency.

## Supported Providers

| Provider | Use Case | Requires Internet | Setup Effort | Status |
|----------|----------|-------------------|--------------|--------|
| **Fake** | Runs out of the box, deterministic placeholders | No | None | **Default** |
| **OpenAI** | GPT-4o-mini, easiest real provider | Yes | API key | Implemented |
| **Ollama** | Local models, fully offline and private | No | Install + model pull | Implemented |
| **Azure OpenAI** | OpenAI models on Azure | Yes | — | Planned |
| **AWS Bedrock** | Claude, Titan, etc. | Yes | — | Planned |
| **Grok (xAI)** | Grok models | Yes | — | Planned |

---

## Optional: Ollama (Fully Offline)

Ollama runs open-source LLMs locally on your machine. No API key, no internet, no per-token cost. This is opt-in — it takes more setup than the cloud providers, so choose it if offline/private play matters to you.

### 1. Install Ollama

**Windows:**
```bash
winget install Ollama.Ollama
```

**macOS:**
```bash
brew install ollama
```

**Linux:**
```bash
curl -fsSL https://ollama.com/install.sh | sh
```

### 2. Pull a model

Choose based on your hardware:

| GPU VRAM | Recommended Model | Command | Quality |
|----------|-------------------|---------|---------|
| 8GB+ | llama3.1:8b | `ollama pull llama3.1:8b` | Best |
| 4-6GB | llama3.2:3b | `ollama pull llama3.2:3b` | Good |
| No GPU / <4GB | llama3.2:3b (CPU) | `ollama pull llama3.2:3b` | Good (slower) |

```bash
# Default recommendation for most users:
ollama pull llama3.2:3b

# Also pull the embedding model for semantic search:
ollama pull nomic-embed-text
```

### 3. Verify it's running

Ollama starts automatically as a service on Windows/macOS. Verify:

```bash
curl http://localhost:11434/api/version
```

### 4. Configure Aircane

Ollama is **not** the default provider. To enable it, open **Settings → AI Provider → Ollama** in the app and save (it defaults to `llama3.2:3b` at `http://localhost:11434`).

To use a different model, set it in that same screen or via:

```bash
export Ai__Ollama__Model=llama3.1:8b
```

### Performance expectations

| Scenario | First request | Subsequent requests |
|----------|---------------|---------------------|
| Model cold start | 15-25s (loading into RAM) | — |
| Warm model (GPU) | — | <1-2s |
| Warm model (CPU, 3B) | — | 2-3s |
| Warm model (CPU, 8B) | — | 5-10s |

The first request after a restart is slow because Ollama loads the model into memory. After that, responses are fast. Ollama keeps the model loaded for 5 minutes of inactivity by default.

---

## OpenAI (Cloud Fallback)

Use OpenAI for tasks where quality matters more than speed (adventure generation, long narration).

**Model:** `gpt-4o-mini` — fastest OpenAI model with good quality.

### Setup

1. Create an account at [platform.openai.com](https://platform.openai.com)
2. Add a payment method and credit ($5 minimum)
3. Create an API key under your project

```bash
export Ai__Provider=OpenAi
export Ai__OpenAi__ApiKey=sk-...
export Ai__OpenAi__Model=gpt-4o-mini
```

Or configure via the UI: Settings → AI Provider → OpenAI.

### Latency

Expect 3-15 seconds per request depending on context size and OpenAI load. Not ideal for mid-combat rules checks, but fine for background tasks.

---

## Other Providers

### Azure OpenAI

```bash
export Ai__Provider=AzureOpenAi
export Ai__AzureOpenAi__ApiKey=your-key
export Ai__AzureOpenAi__Endpoint=https://your-resource.openai.azure.com/
export Ai__AzureOpenAi__DeploymentName=your-deployment
```

### AWS Bedrock

```bash
export Ai__Provider=AwsBedrock
export Ai__AwsBedrock__Region=us-east-1
export Ai__AwsBedrock__ModelId=anthropic.claude-3-sonnet-20240229-v1:0
export Ai__AwsBedrock__AccessKeyId=AKIA...
export Ai__AwsBedrock__SecretAccessKey=...
```

### Grok (xAI)

```bash
export Ai__Provider=Grok
export Ai__Grok__ApiKey=xai-...
export Ai__Grok__Model=grok-3-mini
```

---

## Embedding Provider

Embeddings power semantic search (RAG) over your imported documents and the built-in rules
content. **The embedding provider is configured separately from the chat provider** — an OpenAI
(or other) chat API key does *not* enable embeddings. They are two different settings.

```bash
# Ollama (recommended — local, free)
export Embeddings__Provider=Ollama
export Embeddings__Ollama__BaseUrl=http://localhost:11434
export Embeddings__Ollama__Model=nomic-embed-text
```

Available options today:

| Provider | Use for | Notes |
|----------|---------|-------|
| **Ollama** (`nomic-embed-text`, 768-dim) | Real semantic search | Local, free, private. Requires Ollama installed and the model pulled. |
| **Fake** | Development/tests only | Deterministic placeholder vectors — **no real search**. Queries return nothing useful. |

There is **no cloud (e.g. OpenAI) embedding provider yet.** For working retrieval you currently
need Ollama with `nomic-embed-text`.

> ### ⚠️ Important: embeddings are locked to the provider and model that created them
>
> When you index documents (including the built-in rules bundles seeded on first run), each text
> chunk is converted to a vector using **whatever embedding provider is active at that moment**.
> Search only works when your *queries* are embedded with the **same provider and model**.
> Vectors from different providers or models are not comparable — mixing them produces silently
> wrong or empty results, not an error message.
>
> **What this means for you:**
>
> - **If you seed/index with Ollama and later remove Ollama** (or change the embedding model, or
>   switch to a different embedding provider), your existing embeddings become unusable. Semantic
>   search and the built-in rules assistant will stop returning relevant results until you
>   **re-index**.
> - **Re-indexing is currently a manual step.** There is no automatic detection of a provider
>   mismatch and no one-click re-embed yet (both are planned — see the backlog). Re-indexing means
>   re-generating embeddings for all affected documents, which for the built-in bundles is on the
>   order of hundreds to ~1,000 chunks and can take a few minutes against a local model.
> - **Pick your embedding provider before you index a large library**, and try to stick with it.
>   Changing it later means paying the re-index cost again.
> - **The stored embedding dimension is fixed at 768** (Ollama `nomic-embed-text`). A future
>   provider with a different dimension (e.g. OpenAI's 1536) will require a database change, not
>   just a config switch.
>
> If you switch the embedding provider or model, treat it as a re-index event: re-run indexing for
> your imported documents and re-seed the built-in content so every vector is regenerated
> consistently.

---

## Security Notes

- **Enter your key once.** Keys entered in the UI (Settings → AI Provider) are saved and persist across restarts — you don't need to re-enter them. Because Aircane is local-first, they're stored in a local `ai-settings.json` in the backend data directory on your own machine.
- This `ai-settings.json` is **gitignored** and must never be committed or served over HTTP. Since it can hold a plaintext key, treat it like any other local credential file.
- API keys are **never** stored in the database or sent back to the client unmasked.
- **Developers:** prefer .NET user secrets or environment variables for keys — these take precedence over the file, so your key never has to live in the project tree.
- **Shared/hosted deployments:** the plaintext-file approach is only appropriate for single-user local installs. A multi-user backend must use a secret manager with per-user isolation instead.

## Testing Your Configuration

Use the **Test Connection** button in the UI, or call the API directly:

```bash
curl -X POST http://localhost:5000/api/ai/settings/test-connection \
  -H "Content-Type: application/json" \
  -d '{"activeProvider": 4, "ollama": {"baseUrl": "http://localhost:11434", "model": "llama3.2:3b"}}'
```
