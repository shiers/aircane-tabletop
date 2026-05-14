# AI Provider Configuration

Aircane Tabletop supports multiple AI providers. You can configure them through the UI (Settings → AI Provider) or via environment variables and user secrets.

## Supported Providers

| Provider | Use Case | Requires Internet | Status |
|----------|----------|-------------------|--------|
| **OpenAI** | GPT-4o, GPT-4o-mini | Yes | Implemented |
| **Azure OpenAI** | OpenAI models on Azure | Yes | Planned |
| **AWS Bedrock** | Claude, Titan, etc. | Yes | Planned |
| **Ollama** | Local models (fully offline) | No | Planned (embeddings only for now) |
| **Grok (xAI)** | Grok models | Yes | Planned |
| **Fake** | Development/testing (deterministic) | No | Implemented |

> **Note:** Only OpenAI and Fake are currently implemented as chat AI providers. Ollama is implemented for embeddings only. Other providers will fall back to Fake until their implementations are added. Configuration examples below are provided so you can pre-configure credentials for when support lands.

## Quick Setup via UI

1. Open the app and navigate to **Settings → AI Provider**
2. Select your provider from the dropdown
3. Enter the required credentials (API key, endpoint, etc.)
4. Click **Test Connection** to verify
5. Save

Keys are stored server-side only and are never exposed unmasked in API responses.

---

## Provider-Specific Configuration

### OpenAI

**Required:** API key

```bash
# Environment variable
export Ai__Provider=OpenAi
export Ai__OpenAi__ApiKey=sk-...
export Ai__OpenAi__Model=gpt-4o-mini
```

Or via .NET user secrets:

```bash
cd src/backend/Aircane.Api
dotnet user-secrets set "Ai:Provider" "OpenAi"
dotnet user-secrets set "Ai:OpenAi:ApiKey" "sk-..."
dotnet user-secrets set "Ai:OpenAi:Model" "gpt-4o-mini"
```

### Azure OpenAI

**Required:** API key, endpoint, deployment name

```bash
export Ai__Provider=AzureOpenAi
export Ai__AzureOpenAi__ApiKey=your-key
export Ai__AzureOpenAi__Endpoint=https://your-resource.openai.azure.com/
export Ai__AzureOpenAi__DeploymentName=your-deployment
```

### AWS Bedrock

**Required:** AWS credentials (access key + secret or IAM role), region

```bash
export Ai__Provider=AwsBedrock
export Ai__AwsBedrock__Region=us-east-1
export Ai__AwsBedrock__ModelId=anthropic.claude-3-sonnet-20240229-v1:0
export Ai__AwsBedrock__AccessKeyId=AKIA...
export Ai__AwsBedrock__SecretAccessKey=...
```

If running on an EC2 instance or ECS task with an IAM role, omit the access key fields.

### Ollama (Local)

**Required:** Ollama running locally with a model pulled

```bash
# Install Ollama: https://ollama.ai/
ollama pull llama3.1
ollama pull nomic-embed-text  # for embeddings
```

Configuration:

```bash
export Ai__Provider=Ollama
export Ai__Ollama__BaseUrl=http://localhost:11434
export Ai__Ollama__Model=llama3.1
```

Ollama is the recommended provider for fully offline play with no API costs.

### Grok (xAI)

**Required:** xAI API key

```bash
export Ai__Provider=Grok
export Ai__Grok__ApiKey=xai-...
export Ai__Grok__Model=grok-2
```

---

## Embedding Provider

Embeddings are used for semantic search over your imported documents. Configure separately from the chat AI provider:

```bash
# Use Ollama for local embeddings (default for development)
export Embeddings__Provider=Ollama
export Embeddings__Ollama__BaseUrl=http://localhost:11434
export Embeddings__Ollama__Model=nomic-embed-text
```

Other options: `OpenAi`, `AzureOpenAi`, `Fake` (development only — generates random vectors).

---

## Security Notes

- API keys are **never** stored in the database or sent to the client unmasked.
- Use .NET user secrets for local development — never commit keys to source control.
- In production, use environment variables or a secret manager.
- The `Fake` provider is for development only and returns deterministic placeholder responses.

## Testing Your Configuration

Use the **Test Connection** button in the UI, or call the API directly:

```bash
curl -X POST http://localhost:5000/api/ai/settings/test-connection \
  -H "Content-Type: application/json" \
  -d '{"activeProvider": "OpenAi", "openAi": {"apiKey": "sk-...", "model": "gpt-4o-mini"}}'
```

A successful response confirms the provider is reachable and the credentials are valid.
