using Aircane.Application.Abstractions;
using Aircane.Application.Abstractions.BackgroundJobs;
using Aircane.Application.AiRuntime;
using Aircane.Application.AiRuntime.Validators;
using Aircane.Application.Characters;
using Aircane.Application.GameSystems;
using Aircane.Application.Library;
using Aircane.Infrastructure.Adventures;
using Aircane.Infrastructure.BackgroundJobs;
using Aircane.Infrastructure.Ai;
using Aircane.Infrastructure.Campaigns;
using Aircane.Infrastructure.Characters;
using Aircane.Infrastructure.Dice;
using Aircane.Infrastructure.DocumentProcessing;
using Aircane.Infrastructure.DocumentSources;
using Aircane.Infrastructure.Embeddings;
using Aircane.Infrastructure.GameSystems;
using Aircane.Infrastructure.Library;
using Aircane.Infrastructure.Retrieval;
using Aircane.Infrastructure.Sessions;
using Aircane.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aircane.Infrastructure;

/// <summary>
/// Extension methods for registering Infrastructure services with the DI container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers all Infrastructure-layer services.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register IHttpClientFactory for AI provider connectivity tests and general HTTP usage.
        services.AddHttpClient();

        // IDocumentSource: FolderWatchDocumentSource is the active/default implementation for local/desktop mode.
        // Keyed registrations allow selecting a source by SourceMode:
        //   "folder"   → FolderWatchDocumentSource (local filesystem)
        //   "upload"   → UploadDocumentSource       (reserved for future cloud/upload mode)
        //   "embedded" → EmbeddedResourceDocumentSource (built-in content compiled into an assembly)
        services.AddScoped<IDocumentSource, FolderWatchDocumentSource>();
        services.AddKeyedScoped<IDocumentSource, FolderWatchDocumentSource>("folder");
        services.AddKeyedScoped<IDocumentSource, UploadDocumentSource>("upload");
        // The "embedded" source is bound to the assembly that carries the built-in resource bundle.
        // That assembly (Aircane.Workers) is supplied by the seeder rather than referenced here,
        // to keep Infrastructure from depending on Workers. See BuiltInContentSeeder.

        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<ILibraryService, LibraryService>();
        services.AddScoped<ILicenseService, LicenseService>();
        services.AddScoped<ICampaignService, CampaignService>();
        services.AddScoped<ICampaignStateService, CampaignStateService>();
        services.AddScoped<ICharacterService, CharacterService>();
        services.AddScoped<ISessionHostingService, SessionHostingService>();
        // Token revocation is a singleton so the in-memory fast-path set survives across
        // requests. Persistent (per-jti) revocation is backed by the RevokedTokens table,
        // which the singleton reaches through a scoped DbContext via IServiceScopeFactory.
        services.AddSingleton<ITokenRevocationService, TokenRevocationService>();
        // Tunnel/internet-mode state is a singleton: one process-wide flag that switches on
        // internet-mode hardening (rate limiting, CSRF) when the host enables the tunnel.
        services.AddSingleton<ITunnelStateService, InMemoryTunnelStateService>();
        // ParticipantTokenService is a singleton - the signing key is loaded once from config.
        services.AddSingleton<IParticipantTokenService, ParticipantTokenService>();
        services.AddScoped<CharacterSchemaValidator>();

        // OCR pipeline (optional). The Tesseract engine depends on native binaries and
        // language data that may not be present; it gates cleanly to unavailable when so.
        // When disabled by config, a no-op engine keeps scanned PDFs marked OCR-required.
        RegisterOcrEngine(services, configuration);

        services.AddScoped<IPdfTextExtractor, PdfPigTextExtractor>();
        services.AddScoped<IPdfCharacterExtractor, PdfCharacterExtractor>();
        services.AddSingleton<ITextChunker, SlidingWindowTextChunker>();
        services.AddScoped<IDocumentImportJob, DocumentImportJob>();
        services.AddScoped<IFolderScanJob, FolderScanJob>();
        services.AddScoped<IRetrievalService, RetrievalService>();
        services.AddScoped<IRagContextBuilder, RagContextBuilder>();
        services.AddScoped<IRulesQuestionService, RulesQuestionService>();

        // Dice
        services.AddSingleton<IRollRandomizer, CryptoRollRandomizer>();
        services.AddScoped<IDiceService, DiceService>();

        RegisterEmbeddingProvider(services, configuration);
        RegisterAiProvider(services, configuration);

        // AI settings management (singleton - holds runtime config in memory)
        services.AddSingleton<IAiSettingsService, AiSettingsService>();

        // AI role configuration (singleton - static capability definitions)
        services.AddSingleton<IAiRoleConfigurationService, AiRoleConfigurationService>();

        // AI authority configuration (singleton - static authority level definitions)
        services.AddSingleton<IAiAuthorityService, AiAuthorityConfigurationService>();

        // AI proposal queue service
        services.AddScoped<IAiProposalService, AiProposalService>();

        // State command validators (one per action type)
        services.AddSingleton<IStateCommandValidator, RequestRollValidator>();
        services.AddSingleton<IStateCommandValidator, ApplyDamageValidator>();
        services.AddSingleton<IStateCommandValidator, ApplyHealingValidator>();
        services.AddSingleton<IStateCommandValidator, ApplyConditionValidator>();
        services.AddSingleton<IStateCommandValidator, RemoveConditionValidator>();
        services.AddSingleton<IStateCommandValidator, RevealContentValidator>();
        services.AddSingleton<IStateCommandValidator, MoveSceneValidator>();

        // State command executor - single entry point for AI-initiated state changes
        services.AddScoped<IStateCommandExecutor, StateCommandExecutor>();

        // Player action service - orchestrates the full AI DM loop
        services.AddScoped<IPlayerActionService, PlayerActionService>();

        // Adventure generation - party analysis
        services.AddScoped<IPartyAnalysisService, PartyAnalysisService>();

        // Adventure generation - encounter validation. Both system validators are registered as
        // concretes; the selector picks the right one per campaign's bound game system. The
        // default IEncounterValidator remains D&D 5e for callers that don't select by system.
        services.AddSingleton<Dnd5eEncounterValidator>();
        services.AddSingleton<Pf2eEncounterValidator>();
        services.AddSingleton<IEncounterValidator>(sp => sp.GetRequiredService<Dnd5eEncounterValidator>());
        services.AddSingleton<IEncounterValidatorSelector, EncounterValidatorSelector>();

        // Adventure generation - staged pipeline
        services.AddScoped<IAdventureGenerationService, AdventureGenerationService>();

        // Adventure retrieval - loading and deserializing generated adventures
        services.AddScoped<IAdventureRetrievalService, AdventureRetrievalService>();

        // Adventure indexing - converts generated adventures into searchable chunks
        services.AddScoped<IAdventureIndexingService, AdventureIndexingService>();

        // Game System Definition - registry and migration (scoped, uses DbContext)
        services.AddScoped<ISystemRegistry, SystemRegistry>();
        services.AddScoped<IGameSystemMigrationService, GameSystemMigrationService>();
        services.AddScoped<IGameSystemCanonicalizer, GameSystemCanonicalizer>();
        services.AddScoped<IGameSystemAliasService, GameSystemAliasService>();
        services.AddSingleton<IScanCandidateAnalyzer, ScanCandidateAnalyzer>();

        RegisterBackgroundJobs(services, configuration);

        RegisterFeedback(services, configuration);

        return services;
    }

    /// <summary>
    /// Registers the GitHub-backed feedback service, its settings, and the named HttpClient used
    /// to call the GitHub REST API. The GitHub token is only read inside the service; it is never
    /// logged or echoed back during registration.
    /// </summary>
    private static void RegisterFeedback(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = new Aircane.Application.Configuration.FeedbackSettings();
        configuration.GetSection(Aircane.Application.Configuration.FeedbackSettings.SectionName).Bind(settings);
        services.AddSingleton(settings);

        services.AddHttpClient(Feedback.GitHubFeedbackService.HttpClientName);
        services.AddScoped<Aircane.Application.Feedback.IFeedbackService, Feedback.GitHubFeedbackService>();
    }

    /// <summary>
    /// Registers the background job queue, status store, the <see cref="IDocumentImportService"/>
    /// enqueue seam, and the per-type job handlers.
    /// </summary>
    /// <remarks>
    /// Default runner is the in-process channel queue. A <c>BackgroundJobs:Runner=Hangfire</c>
    /// switch is reserved so a persistent runner can be introduced later without touching callers;
    /// only the queue/worker registration would change here.
    /// </remarks>
    private static void RegisterBackgroundJobs(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var runner = configuration["BackgroundJobs:Runner"] ?? "InProcess";

        // Status store is shared regardless of runner.
        services.AddSingleton<IBackgroundJobStatusStore, InMemoryBackgroundJobStatusStore>();

        switch (runner.Trim().ToLowerInvariant())
        {
            // case "hangfire": // Reserved: register a Hangfire-backed IBackgroundJobQueue here.
            default:
                // Singleton so the enqueuing request scope and the hosted worker share the channel.
                services.AddSingleton<IBackgroundJobQueue, ChannelBackgroundJobQueue>();
                break;
        }

        // Enqueue seam used by controllers/services for document import.
        services.AddScoped<IDocumentImportService, DocumentImportService>();

        // Per-type job handlers, resolved by the worker inside a fresh scope.
        services.AddScoped<IJobHandler<DocumentImportJobMessage>, DocumentImportJobHandler>();
        services.AddScoped<IJobHandler<FolderScanJobMessage>, FolderScanJobHandler>();
        services.AddScoped<IJobHandler<ReembedJobMessage>, ReembedJobHandler>();
        services.AddScoped<IJobHandler<ReocrJobMessage>, ReocrJobHandler>();
    }

    private static void RegisterOcrEngine(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new Aircane.Application.DocumentProcessing.OcrOptions();
        configuration.GetSection(Aircane.Application.DocumentProcessing.OcrOptions.SectionName).Bind(options);

        if (options.Enabled)
        {
            // Singleton: the Tesseract Engine is expensive to construct and is initialized
            // lazily on first use. It is internally thread-safe for our single-image calls.
            services.AddSingleton(options);

            // Opt-in tessdata auto-download (B5.2): resolve/populate the tessdata path before the
            // engine is constructed so it can find the language data.
            TryAutoDownloadTessdata(options);

            services.AddSingleton<Aircane.Application.Abstractions.IOcrEngine, TesseractOcrEngine>();

            // Full-page rasterizer (B5.1): only wire the real (native) rasterizer when the feature
            // is enabled; otherwise a no-op keeps the extractor's optional dependency satisfied.
            if (options.FullPageRasterization)
                services.AddSingleton<Aircane.Application.Abstractions.IPdfRasterizer, DocnetPdfRasterizer>();
            else
                services.AddSingleton<Aircane.Application.Abstractions.IPdfRasterizer, NullPdfRasterizer>();
        }
        else
        {
            services.AddSingleton(options);
            services.AddSingleton<Aircane.Application.Abstractions.IOcrEngine, NullOcrEngine>();
            services.AddSingleton<Aircane.Application.Abstractions.IPdfRasterizer, NullPdfRasterizer>();
        }
    }

    /// <summary>
    /// Opt-in (B5.2): when OCR is enabled, <c>Ocr:AutoDownloadTessdata</c> is true, and no
    /// tessdata path is configured, download <c>eng.traineddata</c> to a local app-data directory
    /// and set <see cref="Aircane.Application.DocumentProcessing.OcrOptions.TessdataPath"/>.
    /// Best-effort and synchronous at startup; failures are swallowed (OCR simply stays
    /// unavailable and logs a clear message when tessdata is missing).
    /// </summary>
    private static void TryAutoDownloadTessdata(Aircane.Application.DocumentProcessing.OcrOptions options)
    {
        if (!options.AutoDownloadTessdata || !string.IsNullOrWhiteSpace(options.TessdataPath))
            return;

        try
        {
            var targetDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Aircane", "tessdata");
            Directory.CreateDirectory(targetDir);
            var targetFile = Path.Combine(targetDir, "eng.traineddata");

            if (!File.Exists(targetFile))
            {
                // Official Tesseract trained data (fast model) from the tessdata_fast release.
                const string url =
                    "https://github.com/tesseract-ocr/tessdata_fast/raw/main/eng.traineddata";
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                var bytes = http.GetByteArrayAsync(url).GetAwaiter().GetResult();
                File.WriteAllBytes(targetFile, bytes);
            }

            options.TessdataPath = targetDir;
        }
        catch
        {
            // Non-fatal: leave TessdataPath unset. TesseractOcrEngine reports unavailable and the
            // startup logs will explain that tessdata is missing.
        }
    }

    private static void RegisterAiProvider(
        IServiceCollection services,
        IConfiguration configuration)
    {
        // Register named HttpClients for providers that need them
        services.AddHttpClient("OpenAI");
        services.AddHttpClient("Ollama");
        services.AddHttpClient("AzureOpenAI");

        // Use a scoped factory that resolves the correct provider based on the
        // current AiSettingsService state. This allows the user to switch providers
        // at runtime via the UI without restarting the backend.
        services.AddScoped<IAiProvider>(sp =>
        {
            var settingsService = sp.GetRequiredService<IAiSettingsService>();
            var currentConfig = settingsService.GetCurrentConfig();

            switch (currentConfig.ActiveProvider)
            {
                case Application.DTOs.AiSettings.AiProviderType.OpenAi:
                {
                    var apiKey = settingsService.GetRawSetting("Ai:OpenAi:ApiKey");
                    if (string.IsNullOrWhiteSpace(apiKey))
                    {
                        // No API key configured yet — fall back to Fake so the app doesn't crash
                        return new FakeAiProvider();
                    }

                    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                    var httpClient = httpClientFactory.CreateClient("OpenAI");
                    var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OpenAiProvider>>();
                    var model = settingsService.GetRawSetting("Ai:OpenAi:Model");
                    return new OpenAiProvider(httpClient, apiKey, model, logger);
                }

                case Application.DTOs.AiSettings.AiProviderType.Ollama:
                {
                    var baseUrl = settingsService.GetRawSetting("Ai:Ollama:BaseUrl")
                        ?? "http://localhost:11434";
                    var model = settingsService.GetRawSetting("Ai:Ollama:Model");
                    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                    var httpClient = httpClientFactory.CreateClient("Ollama");
                    var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OllamaProvider>>();
                    return new OllamaProvider(httpClient, baseUrl, model, logger);
                }

                // Azure OpenAI, Grok, and AWS Bedrock will be added in future tasks.

                default:
                    return new FakeAiProvider();
            }
        });
    }

    private static void RegisterEmbeddingProvider(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var providerName = configuration["Embeddings:Provider"] ?? "Fake";

        switch (providerName.Trim().ToLowerInvariant())
        {
            case "ollama":
                services.AddSingleton<IEmbeddingProvider, OllamaEmbeddingProvider>();
                break;

            case "openai":
                services.AddSingleton<IEmbeddingProvider>(sp =>
                {
                    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                    var httpClient = httpClientFactory.CreateClient("OpenAI");
                    // Reuse the chat provider's key; allow a dedicated embedding model/dimension.
                    var apiKey = configuration["Ai:OpenAi:ApiKey"];
                    var model = configuration["Embeddings:OpenAi:Model"];
                    var dims = configuration.GetValue<int?>("Embeddings:OpenAi:Dimensions");
                    var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OpenAiEmbeddingProvider>>();
                    return new OpenAiEmbeddingProvider(httpClient, apiKey, model, dims, logger);
                });
                break;

            case "azureopenai":
                services.AddSingleton<IEmbeddingProvider>(sp =>
                {
                    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                    var httpClient = httpClientFactory.CreateClient("AzureOpenAI");
                    var endpoint = configuration["Ai:AzureOpenAi:Endpoint"];
                    var apiKey = configuration["Ai:AzureOpenAi:ApiKey"];
                    var deployment = configuration["Embeddings:AzureOpenAi:DeploymentName"];
                    var apiVersion = configuration["Ai:AzureOpenAi:ApiVersion"];
                    var dims = configuration.GetValue<int?>("Embeddings:AzureOpenAi:Dimensions");
                    var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AzureOpenAiEmbeddingProvider>>();
                    return new AzureOpenAiEmbeddingProvider(
                        httpClient, endpoint, apiKey, deployment, apiVersion, dims, logger);
                });
                break;

            default:
                services.AddSingleton<IEmbeddingProvider, FakeEmbeddingProvider>();
                break;
        }

        // Reports whether the active provider dimension matches the pgvector column dimension so
        // vector operations can be gracefully disabled on a mismatch (keyword search still works).
        services.AddSingleton<IEmbeddingCompatibility, EmbeddingCompatibility>();
    }
}
