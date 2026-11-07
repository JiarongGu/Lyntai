using Lyntai.Providers.Onnx;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>A graph the head cannot read fails at composition, and does not keep its file open.</summary>
public class OnnxHeadFaultTests : IDisposable
{
    private readonly ScratchDir _scratch = new("onnx-head");

    public void Dispose() => _scratch.Dispose();

    private string Model => _scratch.Combine("model.onnx");

    private void WriteModelDirectory(string outputName)
    {
        File.WriteAllBytes(Model, TinyOnnxGraph.Build(outputName));
        File.WriteAllText(_scratch.Combine("vocab.txt"), "[PAD]\n[UNK]\n[CLS]\n[SEP]\nhello\nworld\n");
    }

    [Fact] // a pooled-only export is [batch, hidden]: every call would throw IndexOutOfRange from Dimensions[2]
    public void A_bi_encoder_graph_whose_only_output_has_no_token_axis_is_refused_at_composition()
    {
        WriteModelDirectory("sentence_embedding");

        var error = Assert.Throws<InvalidOperationException>(() => OnnxProvider.FromDirectory(_scratch.Path));

        Assert.Contains("sentence_embedding", error.Message, StringComparison.Ordinal);
    }

    [Fact] // the session opened for a refused graph is disposed, so the file is not left mapped
    public void A_refused_graph_does_not_leave_its_model_file_open()
    {
        WriteModelDirectory("sentence_embedding");

        Assert.Throws<InvalidOperationException>(() => OnnxProvider.FromDirectory(_scratch.Path));

        File.Delete(Model);
        Assert.False(File.Exists(Model));
    }
}
