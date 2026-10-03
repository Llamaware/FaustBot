using Discord;
using Discord.Interactions;
using FaustBot.Vpn;
using Microsoft.Extensions.DependencyInjection;

namespace FaustBot.Modules;

/// <summary>Suggests configured hub names as the user types.</summary>
public sealed class HubAutocompleteHandler : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter,
        IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value as string ?? "";
        var suggestions = services.GetRequiredService<VpnServerClient>().Hubs
            .Where(h => h.Name.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(SlashCommandBuilder.MaxOptionsCount)
            .Select(h => new AutocompleteResult(h.Name, h.Name));

        return Task.FromResult(AutocompletionResult.FromSuccess(suggestions));
    }
}
