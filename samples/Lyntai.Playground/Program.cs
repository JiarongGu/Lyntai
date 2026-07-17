// Lyntai.Playground — full-stack smoke over the real library surface:
// AddLyntai(SQLite + claude-cli + an openai-compatible endpoint) → prompt override/compose →
// completion via the router → scoring (incl. an LLM judge) → trace persist/read → memory recall.
// Honors LYNTAI_PROVIDER_CMD (the devtools e2e harness points it at the deterministic stub, so a
// run spends no real tokens) and LYNTAI_DATA (isolated data folder).
using System.Diagnostics;
using Lyntai;
using Lyntai.Cortex;
using Lyntai.Cortex.Scorers;
using Lyntai.Llm;
using Lyntai.Prompts;
using Lyntai.Storage;
using Microsoft.Extensions.DependencyInjection;

var dataDir = Environment.GetEnvironmentVariable("LYNTAI_DATA")
    ?? Path.Combine("devtools", "_playground-data");
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "lyntai.db");
Console.WriteLine($"playground: data={dataDir}");

var services = new ServiceCollection();
services.AddLyntai(b => b
    .AddClaudeCliProvider()
    .AddOpenAiCompatibleProvider("ollama", c =>
    {
        c.BaseUrl = Environment.GetEnvironmentVariable("LYNTAI_OLLAMA_URL") ?? "http://localhost:11434";
        c.DefaultModel = "llama3";
    })
    .UseSqliteStorage(dbPath)
    .AddScorer<OutcomeScorer>()
    .AddScorer<StructureScorer>()
    .AddScorer<RelevancyScorer>()
    .DefaultCandidates("claude-cli", "ollama"));
await using var sp = services.BuildServiceProvider();

var sessionId = $"playground-{Guid.NewGuid():N}";
var traces = sp.GetRequiredService<ITraceService>();
var recorder = traces.Begin(sessionId, "playground");

// 1. remember a fact, render the (overridable) prompt, compose recalled memory into it
var memory = sp.GetRequiredService<IMemoryStore>();
await memory.RememberAsync("playground", "demo", "answers should be a single short sentence");

var prompts = sp.GetRequiredService<IPromptRegistry>();
var basePrompt = await prompts.RenderAsync("playground.ask",
    "Answer briefly: {question}",
    new Dictionary<string, string> { ["question"] = "What is the Lyntai library?" });

var composer = sp.GetRequiredService<IPromptComposer>();
var prompt = await composer.ComposeAsync(basePrompt, "playground", scope: "demo");
recorder.Record(new TraceStep { Kind = "phase", Label = "compose" });

// 2. completion through the front door — Lyntai behaving like one provider
//    (fallback across claude-cli → ollama happens invisibly behind ILlmClient)
var llm = sp.GetRequiredService<ILlmClient>();
var stopwatch = Stopwatch.StartNew();
var reply = await llm.CompleteAsync(
    new LlmRequest { Messages = [LlmMessage.User(prompt)], Consumer = "playground" });
recorder.Record(new TraceStep
{
    Kind = "llm",
    Label = "complete",
    InputTokens = reply.Usage?.InputTokens ?? 0,
    OutputTokens = reply.Usage?.OutputTokens ?? 0,
    CostUsd = reply.Usage?.CostUsd ?? 0,
    DurationMs = stopwatch.ElapsedMilliseconds,
    Detail = reply.Verdict.ToString(),
});

Console.WriteLine($"playground: verdict={reply.Verdict}");
Console.WriteLine($"playground: reply={reply.Text}");
if (reply.Verdict != LlmVerdict.Ok)
{
    Console.Error.WriteLine($"playground: completion failed — {reply.Detail}");
    await recorder.CompleteAsync();
    return 1;
}

// 3. score the exchange (deterministic + the LLM judge through the router)
var scoring = sp.GetRequiredService<IScoringService>();
var scores = await scoring.EvaluateAsync(new ScoreContext
{
    SessionId = sessionId,
    Input = prompt,
    Output = reply.Text,
});
Console.WriteLine($"playground: scores={scores.Count} [{string.Join(", ", scores.Select(s => $"{s.ScorerId}={s.Score:0.##}"))}]");

// 4. finish the trace, read it back from storage
await recorder.CompleteAsync();
var trace = await traces.GetAsync(sessionId);
Console.WriteLine($"playground: trace steps={trace?.Steps.Count ?? 0} " +
    $"tokens={trace?.TotalInputTokens ?? 0}+{trace?.TotalOutputTokens ?? 0} cost={trace?.TotalCostUsd ?? 0:0.####}");

// 5. recall what we remembered
var recalled = await memory.RecallAsync("playground", scope: "demo", query: "short sentence");
Console.WriteLine($"playground: memory recall={recalled.Count}");

// 6. streaming through the same front door (the e2e asserts chunks actually flow)
var streamedChunks = 0;
var streamedText = new System.Text.StringBuilder();
await foreach (var chunk in llm.StreamAsync(
    new LlmRequest { Messages = [LlmMessage.User("Stream one short sentence.")], Consumer = "playground" }))
{
    if (chunk.Kind == LlmChunkKind.Content)
    {
        streamedChunks++;
        streamedText.Append(chunk.Text);
    }
    else if (chunk.Kind == LlmChunkKind.Error)
    {
        Console.Error.WriteLine($"playground: stream error — {chunk.Verdict}: {chunk.Detail}");
    }
}
Console.WriteLine($"playground: stream chunks={streamedChunks} text={streamedText}");

var healthy = trace is { Steps.Count: > 0 } && scores.Count > 0 && recalled.Count > 0 && streamedChunks > 0;
Console.WriteLine(healthy ? "playground: OK" : "playground: INCOMPLETE");
return healthy ? 0 : 1;
