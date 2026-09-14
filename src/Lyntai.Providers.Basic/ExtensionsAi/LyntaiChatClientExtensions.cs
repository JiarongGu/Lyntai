using Lyntai.ExtensionsAi;
using Lyntai.Lifecycle;
using Microsoft.Extensions.AI;

// In `Lyntai.Llm`, beside `ILlmClient` itself — NOT in this module's own namespace. `AsChatClient()` is a
// capability of the FRONT DOOR, and a consumer who has `ILlmClient` in scope must not need a second import
// to call a method the README shows on it. The same reason `Add*` extensions live in `Lyntai` (D145).
namespace Lyntai.Llm;

/// <summary>The entry point to the REVERSE bridge — Lyntai as a <see cref="IChatClient"/>, rather than an
/// <see cref="IChatClient"/> as a Lyntai provider (<see cref="ExtensionsAiProvider"/> is that direction).
/// Use it to adopt Lyntai underneath code that already speaks Microsoft.Extensions.AI: the MEAI surface is
/// unchanged, and routing, fallback, dead-host cooldown and the ops layer come along underneath it.</summary>
public static class LyntaiChatClientExtensions
{
    /// <summary>Expose this Lyntai composition as a Microsoft.Extensions.AI <see cref="IChatClient"/>.
    /// A non-Ok outcome throws <see cref="LlmVerdictException"/> (an <see cref="InvalidOperationException"/>
    /// carrying the <see cref="ProviderVerdict"/>), except <see cref="ProviderVerdict.Refused"/>, which comes back as a
    /// <see cref="ChatFinishReason.ContentFilter"/> response rather than an exception.</summary>
    public static IChatClient AsChatClient(this ILlmClient client) => new LyntaiChatClient(client);
}
