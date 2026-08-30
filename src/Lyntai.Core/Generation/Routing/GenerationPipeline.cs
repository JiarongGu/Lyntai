namespace Lyntai.Generation.Routing;

/// <summary>One stage of a pipeline: what to generate, and which backends may serve it.</summary>
/// <param name="Request">The generation. A later stage's chained inputs are APPENDED to its own
/// <see cref="GenerationRequest.Inputs"/>, and this record is never mutated.</param>
/// <param name="Candidates">Backends for THIS stage, in order. Each stage routes independently, because an
/// image backend and a video backend are rarely the same vendor.</param>
public sealed record GenerationStage(
    GenerationRequest Request,
    IReadOnlyList<GenerationCandidate> Candidates)
{
    /// <summary>What the previous stage's artifact IS to this one — a <see cref="GenerationInputRoles"/>
    /// value, or null for the backend's own default interpretation.
    /// <para>The library never picks it: the same PNG is a first frame to one video backend and an init image
    /// to another, and two honest applications answer differently.</para>
    /// <para>Nothing reads it on the FIRST stage, which chains from nothing — setting it there THROWS rather
    /// than being ignored.</para></summary>
    public string? InputRole { get; init; }

    /// <summary>Which of the previous stage's artifacts chain into this one. Null takes the default: the
    /// single artifact when the previous stage produced exactly one, and a REFUSAL
    /// (<see cref="GenerationVerdict.Unsupported"/>, without calling a backend) when it produced none or
    /// several.
    /// <para>There is deliberately no cleverer default. A media type cannot be branched on — a mesh backend
    /// reports a GLB as <c>application/octet-stream</c> — and "the first <c>image/*</c>" picks a UV texture
    /// atlas, which chains, renders, and is wrong.</para>
    /// <para>An exception thrown here PROPAGATES: reporting a caller's own bug as a capability gap would send
    /// them looking at their backends.</para>
    /// <para>Nothing reads it on the first stage; setting it there THROWS.</para></summary>
    public Func<GenerationResult, IEnumerable<GenerationArtifact>>? SelectInput { get; init; }
}

/// <summary>What a pipeline run produced.</summary>
/// <param name="Stages">One entry per stage that RAN, in order, including the one that failed — so
/// <c>Stages.Count</c> says how far the run got. <b>Every artifact in here was PAID FOR</b>, and an earlier
/// stage's survives a later stage's failure precisely so a caller can keep it.</param>
/// <param name="FailedAt">Index into <paramref name="Stages"/> of the stage that stopped the run, or null.
/// <see cref="Detail"/> numbers stages from 1, so a <c>FailedAt</c> of 1 reads as "stage 2".</param>
/// <param name="Usage">Summed over the stages that reported each field, and null where none did — the
/// platform never invents a cost.</param>
public sealed record GenerationPipelineResult(
    IReadOnlyList<GenerationResult> Stages,
    int? FailedAt,
    GenerationUsage? Usage)
{
    /// <summary>Whether every stage produced.</summary>
    public bool IsOk => FailedAt is null;

    /// <summary>The LAST stage's artifacts — the pipeline's output. <b>Empty unless <see cref="IsOk"/></b>,
    /// mirroring <see cref="GenerationResult"/>'s own rule. A failed run's earlier, paid-for artifacts are
    /// read from <see cref="Stages"/>.</summary>
    public IReadOnlyList<GenerationArtifact> Artifacts =>
        IsOk && Stages.Count > 0 ? Stages[^1].Artifacts : [];

    /// <summary>The failing stage's verdict, or <see cref="GenerationVerdict.Ok"/>.</summary>
    public GenerationVerdict Verdict => FailedAt is { } at ? Stages[at].Verdict : GenerationVerdict.Ok;

    /// <summary>The failing stage's own words, or null.</summary>
    public string? Detail => FailedAt is { } at ? Stages[at].Detail : null;
}

/// <summary>Runs ordered generation stages, feeding each one's artifact into the next through
/// <see cref="GenerationArtifact.ToInput"/>.
/// <para>This COMPOSES <see cref="IGenerationRouter.GenerateAsync"/> and adds nothing to it — every stage is
/// an ordinary routed call, so per-stage fallback, dead-host cooldown, spend caps and throttling govern a
/// pipeline exactly as they govern one render.</para></summary>
public static class GenerationPipeline
{
    /// <summary>Run <paramref name="stages"/> in order, stopping at the first that does not produce.
    /// <para><b>Nothing is ever re-run.</b> A failure at stage 3 does not regenerate stages 1 and 2, whose
    /// output is already paid for; retry WITHIN a stage is the router's fallback across that stage's own
    /// candidates.</para></summary>
    /// <param name="router">The router every stage goes through.</param>
    /// <param name="stages">The stages, in order. At least one.</param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    /// <exception cref="ArgumentException"><paramref name="stages"/> is empty, or its first stage sets
    /// <see cref="GenerationStage.InputRole"/> or <see cref="GenerationStage.SelectInput"/> — which chain
    /// from nothing there, so a caller who set one believes something is happening.</exception>
    public static Task<GenerationPipelineResult> RunPipelineAsync(
        this IGenerationRouter router, IReadOnlyList<GenerationStage> stages, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(stages);
        if (stages.Count == 0)
            throw new ArgumentException("a pipeline needs at least one stage", nameof(stages));
        if (stages[0].InputRole is not null || stages[0].SelectInput is not null)
            throw new ArgumentException(
                "the first stage chains from nothing, so InputRole and SelectInput are never read there",
                nameof(stages));

        return Run(router, stages, ct);
    }

    private static async Task<GenerationPipelineResult> Run(
        IGenerationRouter router, IReadOnlyList<GenerationStage> stages, CancellationToken ct)
    {
        var results = new List<GenerationResult>(stages.Count);

        for (var i = 0; i < stages.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var stage = stages[i];
            var request = stage.Request;

            if (i > 0)
            {
                var chained = Chain(stage, results[i - 1]);
                if (chained is null)
                {
                    results.Add(Refuse(stage, results[i - 1], i));
                    return new GenerationPipelineResult(results, i, Total(results));
                }

                // APPEND: the caller's own inputs keep their order, and `with` leaves their record untouched
                request = request with { Inputs = [.. request.Inputs, .. chained] };
            }

            var result = await router.GenerateAsync(stage.Candidates, request, ct).ConfigureAwait(false);
            results.Add(result);
            if (!result.IsOk) return new GenerationPipelineResult(results, i, Total(results));
        }

        return new GenerationPipelineResult(results, null, Total(results));
    }

    /// <summary>The inputs <paramref name="stage"/> chains from <paramref name="previous"/>, or null when it
    /// cannot identify one — which is a refusal, never a fallback.</summary>
    private static IReadOnlyList<GenerationInput>? Chain(GenerationStage stage, GenerationResult previous)
    {
        List<GenerationArtifact>? chosen;

        if (stage.SelectInput is { } select)
        {
            // deliberately not wrapped: a caller's delegate throwing is their bug, and reporting it as a
            // capability gap would send them looking at their backends
            var selected = select(previous);
            chosen = selected is null ? null : [.. selected];
        }
        else
        {
            // one artifact is not a guess, it is the only candidate; zero and several are where a rule would
            // have to invent something
            chosen = previous.Artifacts.Count == 1 ? [.. previous.Artifacts] : null;
        }

        return chosen is { Count: > 0 } ? [.. chosen.Select(a => a.ToInput(stage.InputRole))] : null;
    }

    /// <summary><see cref="GenerationVerdict.Unsupported"/> because the pipeline cannot serve the request as
    /// posed and no backend is at fault — <c>Failed</c> would say something broke.</summary>
    private static GenerationResult Refuse(GenerationStage stage, GenerationResult previous, int index) =>
        GenerationResult.Failure(GenerationVerdict.Unsupported, stage.SelectInput is null
            ? $"stage {index + 1} cannot choose among stage {index}'s {previous.Artifacts.Count} artifacts; " +
              $"set {nameof(GenerationStage)}.{nameof(GenerationStage.SelectInput)} to say which one chains forward"
            : $"stage {index + 1}'s {nameof(GenerationStage.SelectInput)} chose nothing from stage {index}'s " +
              $"{previous.Artifacts.Count} artifacts");

    /// <summary>Each field summed over the stages that reported it, null where none did. A summed zero would
    /// be an invented cost, which <see cref="GenerationUsage"/> exists to avoid.</summary>
    private static GenerationUsage? Total(IReadOnlyList<GenerationResult> results)
    {
        int? count = null;
        double? seconds = null, cost = null;

        foreach (var usage in results.Select(r => r.Usage).OfType<GenerationUsage>())
        {
            if (usage.Count is { } c) count = (count ?? 0) + c;
            if (usage.Seconds is { } s) seconds = (seconds ?? 0) + s;
            if (usage.CostUsd is { } u) cost = (cost ?? 0) + u;
        }

        return count is null && seconds is null && cost is null
            ? null
            : new GenerationUsage(count, seconds, cost);
    }
}
