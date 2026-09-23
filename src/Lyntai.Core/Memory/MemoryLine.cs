namespace Lyntai.Memory;

/// <summary>Renders one recalled memory as exactly ONE line, so nothing it carries can begin a line.
/// <para>Recalled content is consumer-authored, and therefore attacker-influenceable: whatever reached a
/// remember call is rendered back into a prompt. Content carrying its own newlines escapes the bullet it
/// is written into and can then write whatever structure it likes — including the very headings a composer
/// uses to tell a model which material is exact. A grade is the renderer's to state and never the
/// content's, so every rendered memory passes through here first.</para>
/// <para>Collapsing is not truncation — no content is dropped: line breaks become spaces and the ends are
/// trimmed. What a multi-line fact does lose is its line breaks, which is the price of the one-line
/// invariant.</para>
/// <para><see cref="string.ReplaceLineEndings(string)"/> folds <c>\r</c>, <c>\n</c>, <c>\f</c> and U+0085,
/// U+2028, U+2029; <c>\v</c> and the FS/GS/RS separators (U+001C–U+001E) it leaves, so they are folded here.
/// Each of them starts a line for some reader downstream.</para></summary>
internal static class MemoryLine
{
    public static string Flatten(string content) => content.ReplaceLineEndings(" ")
        .Replace('\v', ' ').Replace('\u001C', ' ').Replace('\u001D', ' ').Replace('\u001E', ' ').Trim();
}
