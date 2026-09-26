namespace Lyntai.Text;

/// <summary>One text encoded for a transformer — the three tensors the graph takes, in the order it takes them.
///
/// <para><b>All three are load-bearing and none fails loudly.</b> A missing mask attends to padding, a
/// missing segment id costs a cross-encoder the signal that tells its query from its document, and either
/// returns a plausible, wrong vector.</para></summary>
/// <param name="Ids">Vocabulary ids, bracketed by the model's special tokens.</param>
/// <param name="AttentionMask">1 for a real token, 0 for padding. All ones until a batch adds padding.</param>
/// <param name="TokenTypeIds">Which segment each token belongs to, as the model's layout assigns it.</param>
public readonly record struct TokenEncoding(int[] Ids, int[] AttentionMask, int[] TokenTypeIds);
