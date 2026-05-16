# AI Provider Configuration

Aircane Tabletop supports multiple AI providers. You can configure them through the UI (Settings → AI Provider) or via environment variables and user secrets.

## Recommended Setup

**For live play (rules lookup, AI DM narration):** Use **Ollama** (local, free, fast ~2-3s responses).

**For heavy tasks (adventure generation, session summaries):** Use **OpenAI GPT-4o-mini** (cloud, paid per token, higher quality for long-form content).

## Supported Providers

| Provider | Use Case | Requires Internet | Status |
|----------|----------|-------------------|--------|
| **Ollama** | Local models (fully offline, fast) | No | **Default** |
| **OpenAI** | GPT-4o-mini for cloud tasks | Yes | Implemented |
| **Azure OpenAI** | OpenAI models on Azure | Yes | Planned |
| **AWS Bedrock** | Claude, Titan, etc. | Yes | Planned |
| **Grok (xAI)** | Grok models | Yes | Planned |
| **Fake** | Development/testing (deterministic) | No | Implemented |

---

## Quick Start: Ollama (Recommended)

Ollama runs open-source LLMs locally on your machine. No API key, no internet, no per-token cost.

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

The default `appsettings.json` is already configured for Ollama. No changes needed if you pulled `llama3.2:3b`.

To use a different model, update via the UI (Settings → AI Provider → Ollama) or set:

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

Embeddings power semantic search over your imported documents. Configure separately from the chat provider:

```bash
# Ollama (recommended — local, free)
export Embeddings__Provider=Ollama
export Embeddings__Ollama__BaseUrl=http://localhost:11434
export Embeddings__Ollama__Model=nomic-embed-text
```

Other options: `OpenAi`, `Fake` (development only — generates random vectors, no real search).

---

## Security Notes

- API keys are **never** stored in the database or sent to the client unmasked.
- Settings are persisted to a local `ai-settings.json` file in the data directory.
- Use .NET user secrets for local development — never commit keys to source control.
- In production, use environment variables or a secret manager.

## Testing Your Configuration

Use the **Test Connection** button in the UI, or call the API directly:

```bash
curl -X POST http://localhost:5000/api/ai/settings/test-connection \
  -H "Content-Type: application/json" \
  -d '{"activeProvider": 4, "ollama": {"baseUrl": "http://localhost:11434", "model": "llama3.2:3b"}}'
```
