// Lyntai.Playground — full-stack smoke over the real library surface:
// AddLyntai(SQLite + claude-cli + an openai-compatible endpoint) → prompt override/compose →
// completion via the router → scoring (incl. an LLM judge) → trace persist/read → memory recall.
// Honors LYNTAI_PROVIDER_CMD (the devtools e2e harness points it at the deterministic stub, so a
// run spends no real tokens) and LYNTAI_DATA (isolated data folder).
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Lyntai;
using Lyntai.Agents;
using Lyntai.Cortex;
using Lyntai.Cortex.Scorers;
using Lyntai.Diagnostics;
using Lyntai.Jobs;
using Lyntai.Llm;
using Lyntai.Prompts;
using Lyntai.Storage;
using Microsoft.Extensions.DependencyInjection;

var dataDir = Environment.GetEnvironmentVariable("LYNTAI_DATA")
    ?? Path.Combine("devtools", "_playground-data");
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "lyntai.db");
Console.WriteLine($"playground: data={dataDir}");

// Observability: subscribe BCL listeners to BOTH telemetry surfaces (GenAI "Lyntai.Llm" + agentic
// "Lyntai.Agents"). This is what wiring the OpenTelemetry SDK's AddSource/AddMeter subscribes to (see
// README); counting in-process keeps the sample dependency-free while proving spans/metrics really fire.
// Attached before the first LLM call so no span is missed.
var tel = new TelemetryTally();
using var spanListener = tel.CreateSpanListener();
ActivitySource.AddActivityListener(spanListener);
using var meterListener = tel.CreateMeterListener();

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
    .AddJobHandler<DemoJobHandler>()
    // an inline tool the model can call inside the tool loop (step 8)
    .AddTool(_ => new FunctionTool("echo", (args, _) => Task.FromResult($"observed:{args}"), "echoes its JSON arguments"))
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

// 7. durable jobs — enqueue a checkpointing job and pump the runner to completion (the app owns the
// pump; here the Playground IS the host). Persisted in the same SQLite db, so it would survive a restart.
var queue = sp.GetRequiredService<IJobQueue>();
var jobId = await queue.EnqueueAsync("demo-lane", "demo", """{"steps":2}""");
var runner = sp.GetRequiredService<IJobRunner>();
for (var pass = 0; pass < 10 && await runner.RunOnceAsync() > 0; pass++) { }
var jobStore = sp.GetRequiredService<IJobStore>();
var finishedJob = await jobStore.GetAsync(jobId);
Console.WriteLine($"playground: job status={finishedJob?.Status} checkpoint={finishedJob?.Checkpoint ?? "none"}");

// 8. agentic tool loop — the model calls the registered "echo" tool, gets the observation fed back, then
// answers. The CLI provider isn't native-tool-capable, so this drives the prompt-protocol fallback path.
var toolLoop = sp.GetRequiredService<IToolLoop>();
var toolResult = await toolLoop.RunAsync(new LlmRequest
{
    Consumer = "playground",
    Messages = [LlmMessage.User("Use your tools to greet the project, then report the result. TOOL_DEMO")],
});
Console.WriteLine($"playground: toolloop verdict={toolResult.Verdict} steps={toolResult.Steps.Count} answer={toolResult.Answer}");

// 9. telemetry — report what the two OTel surfaces emitted across the whole run (the listeners above
// tallied it). Proves the GenAI + agentic instrumentation actually fires end-to-end.
Console.WriteLine($"playground: telemetry chatSpans={tel.ChatSpans} toolLoopSpans={tel.ToolLoopSpans} " +
    $"toolCallSpans={tel.ToolCallSpans} jobSpans={tel.JobSpans} toolInvocations={tel.ToolInvocations} " +
    $"jobsProcessed={tel.JobsProcessed}");
var telemetryEmitted = tel.ChatSpans > 0 && tel.ToolLoopSpans > 0 && tel.ToolCallSpans > 0
    && tel.JobSpans > 0 && tel.ToolInvocations > 0 && tel.JobsProcessed > 0;

var healthy = trace is { Steps.Count: > 0 } && scores.Count > 0 && recalled.Count > 0 && streamedChunks > 0
    && finishedJob is { Status: JobStatus.Succeeded } && toolResult.Ok && telemetryEmitted;
Console.WriteLine(healthy ? "playground: OK" : "playground: INCOMPLETE");
return healthy ? 0 : 1;

/// <summary>A two-step job that checkpoints after step 1 — on a resume it skips straight to step 2 (the
/// idempotent-from-checkpoint contract). Registered via AddJobHandler; dispatched by type "demo".</summary>
sealed class DemoJobHandler : IJobHandler
{
    public string Type => "demo";

    public async Task<JobOutcome> HandleAsync(JobContext ctx, CancellationToken ct = default)
    {
        if (ctx.Checkpoint is null)          // fresh run → do step 1, then persist progress
            await ctx.SaveCheckpointAsync("step1-done", ct);
        // step 2 runs on the first pass or on a resume (where ctx.Checkpoint == "step1-done")
        return JobOutcome.Complete;
    }
}

/// <summary>In-process tally of both Lyntai OTel surfaces — the GenAI source/meter ("Lyntai.Llm") and the
/// agentic one ("Lyntai.Agents"). A real app would AddSource/AddMeter these into the OpenTelemetry SDK
/// instead; the BCL listeners here keep the sample dependency-free while proving the instrumentation
/// fires. Counters are interlocked because the job runner + streaming produce activity off the main flow.</summary>
sealed class TelemetryTally
{
    public int ChatSpans, ToolLoopSpans, ToolCallSpans, JobSpans, ToolInvocations, JobsProcessed;

    public ActivityListener CreateSpanListener() => new()
    {
        ShouldListenTo = s => s.Name is LyntaiDiagnostics.ActivitySourceName or LyntaiDiagnostics.AgentActivitySourceName,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        ActivityStopped = a =>
        {
            if (a.Source.Name == LyntaiDiagnostics.ActivitySourceName) Interlocked.Increment(ref ChatSpans);
            else if (a.OperationName == "tool_loop") Interlocked.Increment(ref ToolLoopSpans);
            else if (a.OperationName.StartsWith("execute_tool")) Interlocked.Increment(ref ToolCallSpans);
            else if (a.OperationName.StartsWith("run_job")) Interlocked.Increment(ref JobSpans);
        },
    };

    public MeterListener CreateMeterListener()
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name is LyntaiDiagnostics.MeterName or LyntaiDiagnostics.AgentMeterName)
                    l.EnableMeasurementEvents(inst);
            },
        };
        listener.SetMeasurementEventCallback<long>((inst, value, _, _) =>
        {
            if (inst.Name == "lyntai.tool.invocations") Interlocked.Add(ref ToolInvocations, (int)value);
            else if (inst.Name == "lyntai.jobs.processed") Interlocked.Add(ref JobsProcessed, (int)value);
        });
        listener.Start();
        return listener;
    }
}
