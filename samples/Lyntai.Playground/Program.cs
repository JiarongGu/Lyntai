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
using Lyntai.Embeddings;
using Lyntai.Jobs;
using Lyntai.Llm;
using Lyntai.Llm.Budgeting;
using Lyntai.Memory;
using Lyntai.Prompts;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Storage;
using Microsoft.Extensions.DependencyInjection;

var dataDir = Environment.GetEnvironmentVariable("LYNTAI_DATA")
    ?? Path.Combine("devtools", "_playground-data");
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "lyntai.db");
Console.WriteLine($"playground: data={dataDir}");

// LYNTAI_DEMO=governance → a separate full-stack smoke over the platform features that the default flow
// doesn't touch (response cache / usage budget / rate limit / semantic memory / recurring schedule). Each
// is a small isolated composition, so it exercises the real DI wiring end-to-end. Returns before the
// default flow below, which is unchanged.
if (Environment.GetEnvironmentVariable("LYNTAI_DEMO") == "governance")
    return await GovernanceDemo.RunAsync(dbPath);

// LYNTAI_DEMO=agent-session → proves the self-driving CLI-agent-session primitive end-to-end against
// the stub: streaming door (session 1, read-only plan gate) → resume into the result door (session 2,
// write execute gate). Returns before the default flow below, which is unchanged.
if (Environment.GetEnvironmentVariable("LYNTAI_DEMO") == "agent-session")
    return await AgentSessionDemo.RunAsync();

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

/// <summary>The LYNTAI_DEMO=governance flow: a full-stack smoke over the platform features the default run
/// doesn't touch. Each scenario is its own isolated AddLyntai composition against the deterministic stub,
/// so it exercises the real DI wiring + decorator fold end-to-end. Prints one True/False marker per feature
/// (the p2 e2e asserts on them).</summary>
static class GovernanceDemo
{
    public static async Task<int> RunAsync(string dbPath)
    {
        var cache = await CacheScenario();
        Console.WriteLine($"governance: cache hit served={cache}");
        var budget = await BudgetScenario();
        Console.WriteLine($"governance: budget refused over cap={budget}");
        var rate = await RateLimitScenario();
        Console.WriteLine($"governance: rate limited={rate}");
        var semantic = await SemanticScenario(dbPath);
        Console.WriteLine($"governance: semantic recall ranked={semantic}");
        var schedule = await ScheduleScenario(dbPath);
        Console.WriteLine($"governance: cron/schedule enqueued={schedule}");

        var ok = cache && budget && rate && semantic && schedule;
        Console.WriteLine(ok ? "governance: OK" : "governance: INCOMPLETE");
        return ok ? 0 : 1;
    }

    // response cache: two IDENTICAL completions — the second must be served from cache (a hit on the
    // lyntai.cache.requests meter), and the provider reached once.
    private static async Task<bool> CacheScenario()
    {
        var hits = 0;
        using var meter = ResultCounter("lyntai.cache.requests", "lyntai.cache.result", "hit", () => Interlocked.Increment(ref hits));

        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddClaudeCliProvider().AddResponseCache().DefaultCandidates("claude-cli"));
        await using var sp = services.BuildServiceProvider();
        var llm = sp.GetRequiredService<ILlmClient>();
        var req = new LlmRequest { Messages = [LlmMessage.User("cache me please")], Consumer = "gov" };

        var first = await llm.CompleteAsync(req);
        var second = await llm.CompleteAsync(req); // identical → cache hit, no provider call
        return first.Verdict == LlmVerdict.Ok && second.Verdict == LlmVerdict.Ok && second.Text == first.Text && hits >= 1;
    }

    // usage budget: the stub reports cost per call, so a tiny cap lets the first call through (recording
    // spend) and refuses the second (soft ceiling checked before the call).
    private static async Task<bool> BudgetScenario()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddClaudeCliProvider().AddUsageBudget(o => o.MaxCostUsd = 0.01).DefaultCandidates("claude-cli"));
        await using var sp = services.BuildServiceProvider();
        var llm = sp.GetRequiredService<ILlmClient>();

        var first = await llm.CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("spend one")] });
        var second = await llm.CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("spend two")] });
        var spent = sp.GetRequiredService<IUsageTracker>().Total().CostUsd;
        return first.Verdict == LlmVerdict.Ok && second.Verdict == LlmVerdict.Refused && spent > 0;
    }

    // rate limit: burst 1 + a negligible refill rate + no wait → the second immediate call is refused.
    private static async Task<bool> RateLimitScenario()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddClaudeCliProvider()
            .AddRateLimit(o => { o.PermitsPerSecond = 0.0001; o.Burst = 1; o.MaxWait = TimeSpan.Zero; })
            .DefaultCandidates("claude-cli"));
        await using var sp = services.BuildServiceProvider();
        var llm = sp.GetRequiredService<ILlmClient>();

        var first = await llm.CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("a")] });
        var second = await llm.CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("b")] });
        return first.Verdict == LlmVerdict.Ok && second.Verdict == LlmVerdict.RateLimited;
    }

    // semantic memory: remember two facts, recall by a query that overlaps the relevant one — it must rank
    // first, and the unrelated fact be filtered by the minScore floor. (DemoEmbedder is a stand-in model.)
    private static async Task<bool> SemanticScenario(string dbPath)
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddClaudeCliProvider().UseSqliteStorage(dbPath).AddEmbeddings(new DemoEmbedder()).DefaultCandidates("claude-cli"));
        await using var sp = services.BuildServiceProvider();
        var mem = sp.GetRequiredService<ISemanticMemory>();

        await mem.RememberAsync("faq", "s", "You can cancel your subscription anytime from account settings.");
        await mem.RememberAsync("faq", "s", "Our pizza menu changes every week.");
        var hits = await mem.RecallAsync("faq", "s", "how do I cancel my subscription", k: 2, minScore: 0.0001);
        // the query-relevant fact ranks FIRST (the unrelated pizza fact may still trail with a tiny
        // collision-driven score from the demo's feature-hash embedder — a real model wouldn't)
        return hits.Count > 0 && hits[0].Content.Contains("cancel");
    }

    // recurring schedule: a short interval schedule fires on the tick after its first interval elapses,
    // enqueuing a durable job (next-run persisted in the SQLite key-value store).
    private static async Task<bool> ScheduleScenario(string dbPath)
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddClaudeCliProvider().UseSqliteStorage(dbPath)
            .AddJobSchedule("demo-sched", "sched-lane", "sched-type", "{}", TimeSpan.FromMilliseconds(5))
            .DefaultCandidates("claude-cli"));
        await using var sp = services.BuildServiceProvider();
        var scheduler = sp.GetRequiredService<IJobScheduler>();

        await scheduler.TickAsync();   // first sight → schedules next = now + 5ms
        await Task.Delay(30);          // let the interval lapse
        var enqueued = await scheduler.TickAsync(); // due → enqueue
        var jobs = await sp.GetRequiredService<IJobStore>().ListAsync(lane: "sched-lane");
        return enqueued >= 1 && jobs.Count >= 1;
    }

    /// <summary>A MeterListener counting a named instrument's measurements whose given tag equals a value.</summary>
    private static MeterListener ResultCounter(string instrument, string tagKey, string tagValue, Action onMatch)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) => { if (inst.Name == instrument) l.EnableMeasurementEvents(inst); },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var t in tags) if (t.Key == tagKey && (string?)t.Value == tagValue) { onMatch(); return; }
        });
        listener.Start();
        return listener;
    }
}

/// <summary>The LYNTAI_DEMO=agent-session flow: drives the self-driving CLI-agent-session primitive against
/// the stub. Session 1 (read-only plan gate) via the STREAMING door; session 2 (write execute gate) RESUMED
/// from session 1 via the RESULT door. Proves spawn + gate flags + streamed events + resume end-to-end.</summary>
static class AgentSessionDemo
{
    public static async Task<int> RunAsync()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddClaudeCliProvider()
            .AddClaudeCliAgentSession()
            .DefaultCandidates("claude-cli"));
        await using var sp = services.BuildServiceProvider();
        var session = sp.GetRequiredService<IAgentSession>();
        var cwd = Directory.GetCurrentDirectory();

        // Session 1 — read-only PLAN gate, STREAMING door.
        string? id1 = null; var events1 = 0; var tools = 0; LlmVerdict? v1 = null;
        await foreach (var e in session.StreamAsync(new ClaudeAgentOptions {
            Prompt = "Plan the change. AGENT_SESSION", ToolPolicy = AgentToolPolicy.ReadOnly, WorkingDirectory = cwd }))
        {
            events1++;
            if (e is SessionStarted s) id1 = s.SessionId;
            else if (e is ToolCall tc) { tools++; Console.WriteLine($"agent: tool={tc.Name} file={ClaudeToolCalls.FilePathOf(tc)}"); }
            else if (e is SessionEnded se) v1 = se.Verdict;
        }
        Console.WriteLine($"agent: session1 id={id1} events={events1} tools={tools} verdict={v1}");

        // Session 2 — WRITE execute gate, RESUMED from session 1, RESULT door (+ onEvent tally).
        var events2 = 0;
        var result = await session.RunAsync(new ClaudeAgentOptions {
            Prompt = "Apply the change. AGENT_SESSION", ToolPolicy = AgentToolPolicy.Write,
            ResumeToken = id1, WorkingDirectory = cwd }, onEvent: _ => events2++);
        var resumed = result.SessionId == id1;
        Console.WriteLine($"agent: session2 id={result.SessionId} events={events2} resumed={resumed} verdict={result.Verdict} text={result.FinalText}");

        var ok = id1 is not null && tools > 0 && v1 == LlmVerdict.Ok
            && result.Verdict == LlmVerdict.Ok && resumed && events2 > 0;
        Console.WriteLine(ok ? "agent: OK" : "agent: INCOMPLETE");
        return ok ? 0 : 1;
    }
}

/// <summary>A deterministic stand-in embedder for the demo (feature-hashed bag-of-words, so texts sharing
/// words land close in cosine space). A real app registers an actual embeddings model via AddEmbeddings.</summary>
sealed class DemoEmbedder : IEmbedder
{
    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<float[]>>([.. texts.Select(Embed)]);

    private static float[] Embed(string text)
    {
        var v = new float[64];
        foreach (var word in text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            uint h = 2166136261; // FNV-1a (stable across runs, unlike string.GetHashCode)
            foreach (var c in word) { h ^= c; h *= 16777619; }
            v[(int)(h & 0x7fffffff) % v.Length] += 1f;
        }
        return v;
    }
}
