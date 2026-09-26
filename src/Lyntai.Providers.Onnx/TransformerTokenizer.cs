using Lyntai.Text;

namespace Lyntai.Providers.Onnx;

/// <summary>What <see cref="WindowedTokenizer"/> needs from a model's tokenizer, whichever family it is: its
/// content ids, its own truncating encodings, and its layout of special tokens around ids already windowed.</summary>
internal interface ITransformerTokenizer
{
    IReadOnlyList<int> EncodeToIds(string text);

    TokenEncoding Encode(string text, int maxTokens);

    TokenEncoding Encode(string a, string b, int maxTokens);

    TokenEncoding Frame(IReadOnlyList<int> content);

    TokenEncoding Frame(IReadOnlyList<int> first, IReadOnlyList<int> second);
}

/// <summary>A BERT-family model's <c>vocab.txt</c>.</summary>
internal sealed class WordPieceRows(WordPieceTokenizer tokenizer) : ITransformerTokenizer
{
    public IReadOnlyList<int> EncodeToIds(string text) => tokenizer.EncodeToIds(text);

    public TokenEncoding Encode(string text, int maxTokens) => tokenizer.Encode(text, maxTokens);

    public TokenEncoding Encode(string a, string b, int maxTokens) => tokenizer.Encode(a, b, maxTokens);

    public TokenEncoding Frame(IReadOnlyList<int> content) => tokenizer.Frame(content);

    public TokenEncoding Frame(IReadOnlyList<int> first, IReadOnlyList<int> second) => tokenizer.Frame(first, second);
}

/// <summary>An XLM-R-family model's Unigram <c>tokenizer.json</c>.</summary>
internal sealed class SentencePieceRows(SentencePieceTokenizer tokenizer) : ITransformerTokenizer
{
    public IReadOnlyList<int> EncodeToIds(string text) => tokenizer.EncodeToIds(text);

    public TokenEncoding Encode(string text, int maxTokens) => tokenizer.Encode(text, maxTokens);

    public TokenEncoding Encode(string a, string b, int maxTokens) => tokenizer.Encode(a, b, maxTokens);

    public TokenEncoding Frame(IReadOnlyList<int> content) => tokenizer.Frame(content);

    public TokenEncoding Frame(IReadOnlyList<int> first, IReadOnlyList<int> second) => tokenizer.Frame(first, second);
}

/// <summary>Picks the tokenizer a model directory ships.</summary>
internal static class TransformerTokenizer
{
    /// <summary><c>vocab.txt</c> is WordPiece, read exactly as it always was — so no WordPiece deployment
    /// changes, even one that also ships a <c>tokenizer.json</c>. Otherwise a <c>tokenizer.json</c> is
    /// SentencePiece (<c>docs/DECISIONS.md</c> <b>D191</b>).</summary>
    /// <exception cref="FileNotFoundException">Neither file, naming both.</exception>
    /// <exception cref="InvalidDataException">The <c>tokenizer.json</c> is not a Unigram pipeline
    /// <see cref="SentencePieceTokenizer"/> runs.</exception>
    public static (ITransformerTokenizer Tokenizer, TokenBoundaries Boundaries) FromModelDirectory(string directory)
    {
        var vocabulary = Path.Combine(directory, "vocab.txt");
        if (File.Exists(vocabulary))
            return (new WordPieceRows(WordPieceTokenizer.FromModelDirectory(directory)),
                TokenBoundaries.FromVocabulary(File.ReadAllLines(vocabulary)));

        if (File.Exists(Path.Combine(directory, "tokenizer.json")))
        {
            var tokenizer = SentencePieceTokenizer.FromModelDirectory(directory);
            // pooling reads row zero as the classification token, and an empty text must still be a row
            if (tokenizer.Frame([]).Ids.Length == 0)
                throw new InvalidDataException(
                    $"The tokenizer.json in '{directory}' declares no post-processor, so its rows carry no special "
                    + "tokens; a transformer encoder is trained on framed input.");
            return (new SentencePieceRows(tokenizer), TokenBoundaries.FromPieces(tokenizer.Pieces));
        }

        throw new FileNotFoundException(
            $"No tokenizer in '{directory}' — looked for vocab.txt (WordPiece) and tokenizer.json (SentencePiece "
            + "Unigram). A partial download is the usual reason.", vocabulary);
    }
}
