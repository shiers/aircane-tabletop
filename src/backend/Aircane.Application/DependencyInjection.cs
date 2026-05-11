using Aircane.Application.AiRuntime;
using Aircane.Application.GameSystems;
using Aircane.Application.Validation;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Aircane.Application;

/// <summary>
/// Extension methods for registering Application-layer services with the DI container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers all Application-layer services, including FluentValidation validators.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<RegisterFolderValidator>();
        services.AddSingleton<AiOutputParser>();
        services.AddSingleton<IGameSystemDefaultsApplicator, GameSystemDefaultsApplicator>();

        // Game System Definition serialization
        services.AddSingleton<IGameSystemDefinitionSerializer, GameSystemDefinitionSerializer>();

        // System-agnostic mechanic components (stateless — safe as singletons)
        services.AddSingleton<IMechanicResolver, MechanicResolver>();
        services.AddSingleton<ICharacterSchemaEngine, CharacterSchemaEngine>();
        services.AddSingleton<IConditionRegistry, ConditionRegistry>();
        services.AddSingleton<IActionEconomyTracker, ActionEconomyTracker>();
        services.AddSingleton<IAiContextAdapter, AiContextAdapter>();
        services.AddSingleton<IEncounterBudgetEngine, EncounterBudgetEngine>();

        return services;
    }
}
