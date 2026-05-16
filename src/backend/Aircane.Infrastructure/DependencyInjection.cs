using Aircane.Application.Abstractions;
using Aircane.Application.AiRuntime;
using Aircane.Application.AiRuntime.Validators;
using Aircane.Application.Characters;
using Aircane.Application.GameSystems;
using Aircane.Infrastructure.Adventures;
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

        // IDocumentSource: FolderWatchDocumentSource is the active implementation for local/desktop mode.
        // UploadDocumentSource is reserved for future cloud/upload mode and is not registered here.
        services.AddScoped<IDocumentSource, FolderWatchDocumentSource>();

        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<ILibraryService, LibraryService>();
        services.AddScoped<ICampaignService, CampaignService>();
        services.AddScoped<ICampaignStateService, CampaignStateService>();
        services.AddScoped<ICharacterService, CharacterService>();
        services.AddScoped<ISessionHostingService, SessionHostingService>();
        // Token revocation is a singleton so the in-memory set survives across requests.
        services.AddSingleton<ITokenRevocationService, InMemoryTokenRevocationService>();
        // ParticipantTokenService is a singleton - the signing key is loaded once from config.
        services.AddSingleton<IParticipantTokenService, ParticipantTokenService>();
        services.AddScoped<CharacterSchemaValidator>();
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

        // Adventure generation - encounter validation (D&D 5e placeholder)
        services.AddSingleton<IEncounterValidator, Dnd5eEncounterValidator>();

        // Adventure generation - staged pipeline
        services.AddScoped<IAdventureGenerationService, AdventureGenerationService>();

        // Adventure retrieval - loading and deserializing generated adventures
        services.AddScoped<IAdventureRetrievalService, AdventureRetrievalService>();

        // Adventure indexing - converts generated adventures into searchable chunks
        services.AddScoped<IAdventureIndexingService, AdventureIndexingService>();

        // Game System Definition - registry and migration (scoped, uses DbContext)
        services.AddScoped<ISystemRegistry, SystemRegistry>();
        services.AddScoped<IGameSystemMigrationService, GameSystemMigrationService>();

        return services;
    }

    private static void RegisterAiProvider(
        IServiceCollection services,
        IConfiguration configuration)
    {
        // Register named HttpClients for providers that need them
        services.AddHttpClient("OpenAI");

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
                    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                    var httpClient = httpClientFactory.CreateClient("OpenAI");
                    var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OpenAiProvider>>();
                    var apiKey = settingsService.GetRawSetting("Ai:OpenAi:ApiKey");
                    var model = settingsService.GetRawSetting("Ai:OpenAi:Model");
                    return new OpenAiProvider(httpClient, apiKey, model, logger);
                }

                // Azure OpenAI, Ollama, Grok, and AWS Bedrock will be added in future tasks.

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

            default:
                services.AddSingleton<IEmbeddingProvider, FakeEmbeddingProvider>();
                break;
        }
    }
}
