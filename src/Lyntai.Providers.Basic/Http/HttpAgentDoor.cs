using Lyntai.Agents;

namespace Lyntai.Providers.Http;

/// <summary>What the HTTP door states about itself, for <see cref="AgentProviderDescriptor"/>. Every claim is
/// <see cref="AgentAbilitySource.Declared"/> from what <see cref="ToolLoop"/> and the chat engine measurably do;
/// the one thing only the endpoint can say (whether it runs the model a turn names) is
/// <see cref="AgentSupport.Unknown"/>, which refuses nothing and promises nothing.</summary>
internal static class HttpAgentDoor
{
    public const string DoorId = "http";

    public static AgentProviderDescriptor Describe(string providerId) => new()
    {
        Id = providerId,
        Doors =
        [
            new AgentDoor
            {
                Id = DoorId,
                Kind = AgentDoorKinds.Http,
                Abilities =
                [
                    Declared(AgentAbilities.Resume, AgentSupport.Unsupported, "a turn is one loop run and the door states no conversation token; the next turn starts from its own prompt"),
                    Declared(AgentAbilities.ReadOnlyGate, AgentSupport.Supported,
                        "a read-only turn offers the model no tool and a write turn the registered ones. Every turn sends its prompt, system prompt and each tool's result to the endpoint the registration names, which may be a remote service"),
                    Declared(AgentAbilities.McpServers, AgentSupport.Unsupported, "the model's tools are the registered tools; a turn names no server of its own"),
                    Declared(AgentAbilities.DisallowedTools, AgentSupport.Supported, "a named tool is left out of the roster the model is shown, so it cannot call it"),
                    Declared(AgentAbilities.SystemPrompt, AgentSupport.Supported, "the request's leading system message"),
                    Declared(AgentAbilities.Model, AgentSupport.Unknown,
                        "the id is the request's model field; whether the endpoint runs that model is the endpoint's (a single-model llama-server answers with the one it loaded, whatever the label)"),
                    Declared(AgentAbilities.Steering, AgentSupport.Unsupported, "a turn is one loop run; a word sent during it has nowhere to go"),
                    Declared(AgentAbilities.PlanEvents, AgentSupport.Unsupported, "the loop keeps no plan and emits no PlanUpdated"),
                    Declared(AgentAbilities.LiveUsage, AgentSupport.Unsupported, "the loop reports its usage once, as UsageFinal, when the turn ends"),
                    Declared(AgentAbilities.TypedFailures, AgentSupport.Supported,
                        "the failed response's status, Retry-After header and error envelope (ProviderVerdictClassifier.ReadHttpFailure), or a transport fault's kind"),
                    Declared(AgentAbilities.Accounts, AgentSupport.Unsupported, "the key is the registration's own; a turn cannot choose one"),
                    Declared(AgentAbilities.UsageReading, AgentSupport.Unsupported, "the door asks the endpoint no usage question, and a turn reports its own usage"),
                    Declared(AgentAbilities.PermissionRequests, AgentSupport.Unsupported, "the model asks no permission: a call is put to the registered IToolApprovers, which are the tool loop's"),
                    Declared(AgentAbilities.PermissionMode, AgentSupport.Unsupported, "the endpoint has no permission mode: a call is put to the registered IToolApprovers"),
                    Declared(AgentAbilities.Effort, AgentSupport.Unsupported, "the request carries no effort field"),
                    Declared(AgentAbilities.Plugins, AgentSupport.Unsupported, "the model's tools are the registered tools; a turn loads no plugin"),
                ],
            },
        ],
    };

    private static AgentAbility Declared(string name, AgentSupport support, string evidence) =>
        new(name, support, AgentAbilitySource.Declared) { Evidence = evidence };
}
