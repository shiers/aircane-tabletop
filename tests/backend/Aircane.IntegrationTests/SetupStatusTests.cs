using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// Integration tests for GET /api/setup/status (the first-launch setup wizard endpoint).
///
/// The endpoint is anonymous and must reflect: whether ai-settings.json exists
/// (isFirstLaunch), whether a non-Fake AI provider is active (aiProviderConfigured),
/// the active chat/embedding provider names, and whether Ollama is reachable.
///
/// Each test uses its own factory bound to a unique temp Storage:DocumentsPath, so
/// ai-settings.json existence is deterministic and isolated from the dev/CI machine.
/// </summary>
public class SetupStatusTests
{
    [Fact]
    public async Task SetupStatus_NoSettingsFile_ReturnsIsFirstLaunchTrue()
    {
        using var factory = new SetupTestFactory();
        var client = factory.CreateClient();

        var status = await GetStatusAsync(client);

        Assert.True(status!.IsFirstLaunch);
    }

    [Fact]
    public async Task SetupStatus_FakeProvider_ReturnsAiProviderConfiguredFalse()
    {
        using var factory = new SetupTestFactory(aiProvider: "Fake");
        var client = factory.CreateClient();

        var status = await GetStatusAsync(client);

        Assert.False(status!.AiProviderConfigured);
        Assert.Equal("Fake", status.ActiveAiProvider);
    }

    [Fact]
    public async Task SetupStatus_OllamaConfigured_ReturnsAiProviderConfiguredTrue()
    {
        using var factory = new SetupTestFactory(aiProvider: "Ollama");
        var client = factory.CreateClient();

        var status = await GetStatusAsync(client);

        Assert.True(status!.AiProviderConfigured);
        Assert.Equal("Ollama", status.ActiveAiProvider);
    }

    [Fact]
    public async Task SetupStatus_OllamaReachable_ReturnsOllamaReachableTrue()
    {
        // Spin up a tiny stub that answers /api/tags with 200, and point the backend's
        // Ollama base URL at it so the reachability probe succeeds deterministically.
        using var stub = new StubOllamaServer();
        await stub.StartAsync();

        using var factory = new SetupTestFactory(aiProvider: "Ollama", ollamaBaseUrl: stub.BaseUrl);
        var client = factory.CreateClient();

        var status = await GetStatusAsync(client);

        Assert.True(status!.OllamaReachable);
    }

    [Fact]
    public async Task SetupStatus_OllamaNotRunning_ReturnsOllamaReachableFalse()
    {
        // Point at a port nothing is listening on so the probe fails fast.
        using var factory = new SetupTestFactory(
            aiProvider: "Fake",
            ollamaBaseUrl: "http://127.0.0.1:1");
        var client = factory.CreateClient();

        var status = await GetStatusAsync(client);

        Assert.False(status!.OllamaReachable);
    }

    private static async Task<SetupStatusResponse?> GetStatusAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/setup/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<SetupStatusResponse>();
    }

    private sealed record SetupStatusResponse(
        [property: JsonPropertyName("isFirstLaunch")] bool IsFirstLaunch,
        [property: JsonPropertyName("aiProviderConfigured")] bool AiProviderConfigured,
        [property: JsonPropertyName("activeAiProvider")] string ActiveAiProvider,
        [property: JsonPropertyName("activeEmbeddingProvider")] string ActiveEmbeddingProvider,
        [property: JsonPropertyName("ollamaReachable")] bool OllamaReachable);
}

/// <summary>
/// A setup-specific factory that isolates ai-settings.json to a unique temp directory
/// (so isFirstLaunch is deterministic) and lets a test choose the active AI provider and
/// Ollama base URL. Cleans up the temp directory on dispose.
/// </summary>
internal sealed class SetupTestFactory : AircaneWebApplicationFactory
{
    private readonly string _dataDir;
    private readonly string? _aiProvider;
    private readonly string? _ollamaBaseUrl;

    public SetupTestFactory(string? aiProvider = null, string? ollamaBaseUrl = null)
    {
        _aiProvider = aiProvider;
        _ollamaBaseUrl = ollamaBaseUrl;
        _dataDir = Path.Combine(Path.GetTempPath(), "aircane-setup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_dataDir, "documents"));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Added last, so these override the base factory's in-memory values.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                // ai-settings.json path derives from the PARENT of Storage:DocumentsPath.
                ["Storage:DocumentsPath"] = Path.Combine(_dataDir, "documents"),
            };
            if (_aiProvider is not null)
                overrides["Ai:Provider"] = _aiProvider;
            if (_ollamaBaseUrl is not null)
                overrides["Ai:Ollama:BaseUrl"] = _ollamaBaseUrl;

            config.AddInMemoryCollection(overrides);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try
            {
                if (Directory.Exists(_dataDir))
                    Directory.Delete(_dataDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}

/// <summary>
/// Minimal in-process HTTP stub that answers Ollama's <c>/api/tags</c> with 200 so the
/// setup-status reachability probe can be asserted deterministically without a real daemon.
/// </summary>
internal sealed class StubOllamaServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cts;

    public string BaseUrl { get; }

    public StubOllamaServer()
    {
        var port = GetFreePort();
        BaseUrl = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add($"{BaseUrl}/");
    }

    public Task StartAsync()
    {
        _listener.Start();
        _cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync();
                }
                catch
                {
                    break; // listener stopped
                }

                try
                {
                    ctx.Response.StatusCode = 200;
                    var buffer = System.Text.Encoding.UTF8.GetBytes("{\"models\":[]}");
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.OutputStream.WriteAsync(buffer);
                    ctx.Response.Close();
                }
                catch
                {
                    // Ignore per-request errors in the stub.
                }
            }
        });
        return Task.CompletedTask;
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
            if (_listener.IsListening)
                _listener.Stop();
            _listener.Close();
        }
        catch
        {
            // Best-effort teardown.
        }
    }
}
