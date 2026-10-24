namespace Lyntai.Memory;

/// <summary>What <see cref="MemoryComposition.RenderWithManifest"/> did with one item — one value per cut the
/// renderer actually makes. A composition cut, never a recall cut: what a recall removed before rendering is
/// <see cref="MemoryCandidateOutcome"/>.</summary>
public enum MemoryCompositionOutcome
{
    /// <summary>It is in the text.</summary>
    Rendered = 0,

    /// <summary>Its line did not fit what was left of its section's budget — the authoritative reserve, or
    /// for associative material what <see cref="MemoryCompositionOptions.Budget"/> had left after it.</summary>
    CutByBudget = 1,

    /// <summary>An identical line was already rendered, so the model has it; it spent nothing.</summary>
    Duplicate = 2,
}

/// <summary>One item offered to <see cref="MemoryComposition.RenderWithManifest"/> and what became of it.</summary>
/// <param name="Item">The item, exactly as the caller passed it.</param>
/// <param name="Section">The section it was rendered into, or would have been: <see cref="MemoryGrade.Authoritative"/>
/// or, for every other grade, <see cref="MemoryGrade.Associative"/>.</param>
/// <param name="Outcome">Whether it was rendered, and if not, which cut removed it.</param>
/// <param name="Characters">The CHARACTERS its line costs against the budget, bullet and line break included —
/// what it spent when rendered, and what it would have spent when cut.</param>
public sealed record MemoryCompositionEntry(
    MemoryItem Item,
    MemoryGrade Section,
    MemoryCompositionOutcome Outcome,
    int Characters)
{
    /// <summary>Whether it is in the text: <see cref="Outcome"/> is <see cref="MemoryCompositionOutcome.Rendered"/>.</summary>
    public bool Included => Outcome == MemoryCompositionOutcome.Rendered;
}

/// <summary>The rendered text and its manifest — what <see cref="MemoryComposition.RenderWithManifest"/>
/// returns.</summary>
/// <param name="Text">Byte-identical to <see cref="MemoryComposition.Render"/> for the same arguments.</param>
/// <param name="Entries">One entry per item offered, in the order offered; empty when none was.</param>
public sealed record MemoryCompositionResult(string Text, IReadOnlyList<MemoryCompositionEntry> Entries);
