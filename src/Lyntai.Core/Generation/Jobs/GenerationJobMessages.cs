namespace Lyntai.Generation.Jobs;

/// <summary>The codes the generation job engine reports its stages under (<see cref="Lyntai.Jobs.JobMessage.Code"/>),
/// for a reader that localizes job status. A stage of a multi-stage pipeline carries the arguments <c>stage</c>
/// (1-based) and <c>stages</c>; a lone render, and the pipeline's final delivery, carry none. The message text is
/// the English line the engine has always reported.</summary>
public static class GenerationJobMessages
{
    /// <summary>A stage's render was submitted to its backend.</summary>
    public const string Submitted = "lyntai.generation.submitted";

    /// <summary>A stage's render is running at its backend.</summary>
    public const string Running = "lyntai.generation.running";

    /// <summary>A stage's artifacts were delivered — or, with no arguments, the whole job's.</summary>
    public const string Delivered = "lyntai.generation.delivered";
}
