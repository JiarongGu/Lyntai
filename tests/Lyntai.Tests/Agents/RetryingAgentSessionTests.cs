using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Agents;

/// <summary>The retry decorator against a scripted session and a clock that only moves when told to: the
/// disposition table (what each kind and delivery does), the bound, the wait (held until the clock moves, cut by
/// a cancellation) and the one-layer rule. Zero tokens, zero real waiting.</summary>
public class RetryingAgentSessionTests
{
    private static readonly AgentSessionOptions Turn = new() { Prompt = "do the thing" };

    private sealed record DoorOptions : AgentSessionOptions { public string? Extra { get; init; } }

    private static SessionEnded Failed(AgentFailureKind kind, AgentDelivery delivery, string? sessionId = null,
        AgentRestriction? restriction = null) =>
        new(ProviderVerdict.Failed, true, null, sessionId, null, "boom")
        {
            Failure = new AgentFailure(kind, "test") { Delivery = delivery, Restriction = restriction },
        };

    private static SessionEnded Succeeded() => new(ProviderVerdict.Ok, false, "success", "s-1", "done", null);

    private static AgentStreamEvent[] Attempt(params AgentStreamEvent[] events) => events;

    private static RetryingAgentSession Retry(IAgentSession inner, ManualTimeProvider clock,
        Action<AgentRetryOptions>? configure = null)
    {
        var options = new AgentRetryOptions();
        configure?.Invoke(options);
        return new RetryingAgentSession(inner, options, clock);
    }

    // Runs the turn to its end, letting each wait it parks on elapse at once — the clock is the only thing that
    // ever moves, so a test that never reaches a wait never advances it.
    private static async Task<List<AgentStreamEvent>> RunToEnd(IAgentSession session, ManualTimeProvider clock,
        AgentSessionOptions? options = null, CancellationToken ct = default)
    {
        var run = Task.Run(() => session.StreamAsync(options ?? Turn, ct).ToListAsync(ct).AsTask(), ct);
        var deadline = DateTime.UtcNow + TestTimeouts.GateWait;
        while (!run.IsCompleted)
        {
            if (clock.NextDue() is { } due) clock.Advance(due);
            else await Task.Delay(1, ct);
            if (DateTime.UtcNow > deadline) throw new TimeoutException("the turn never finished");
        }
        return await run;
    }

    private static SessionEnded Terminal(IEnumerable<AgentStreamEvent> events) => events.OfType<SessionEnded>().Single();

    // ── the disposition table ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_never_sent_transient_failure_resends_the_same_prompt()
    {
        var inner = new ScriptedAgentSession(
            Attempt(Failed(AgentFailureKind.Transient, AgentDelivery.NeverSent)),
            Attempt(Succeeded()));
        var clock = new ManualTimeProvider();

        var events = await RunToEnd(Retry(inner, clock), clock);

        Assert.Equal(2, inner.Received.Count);
        Assert.Equal("do the thing", inner.Received[1].Prompt);
        Assert.Null(inner.Received[1].ResumeToken);
        Assert.False(Terminal(events).IsError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_taken_transient_failure_resumes_with_the_continue_word(bool tokenOnTerminal)
    {
        // the session id arrives as an event, or only on the terminal: either is the conversation to resume
        var inner = new ScriptedAgentSession(
            tokenOnTerminal
                ? Attempt(new TextDelta("half"), Failed(AgentFailureKind.Transient, AgentDelivery.Taken, "s-9"))
                : Attempt(new SessionStarted("s-9"), new TextDelta("half"),
                    Failed(AgentFailureKind.Transient, AgentDelivery.Taken)),
            Attempt(Succeeded()));
        var clock = new ManualTimeProvider();

        var events = await RunToEnd(Retry(inner, clock), clock);

        Assert.Equal(2, inner.Received.Count);
        Assert.Equal("continue", inner.Received[1].Prompt);
        Assert.Equal("s-9", inner.Received[1].ResumeToken);
        Assert.False(Terminal(events).IsError);
    }

    [Fact]
    public async Task A_taken_failure_on_a_resumed_turn_resumes_the_token_it_was_given()
    {
        var inner = new ScriptedAgentSession(
            Attempt(new TextDelta("half"), Failed(AgentFailureKind.Transient, AgentDelivery.Taken)),
            Attempt(Succeeded()));
        var clock = new ManualTimeProvider();

        await RunToEnd(Retry(inner, clock, o => o.ContinueWord = "go on"), clock,
            Turn with { ResumeToken = "earlier" });

        Assert.Equal("go on", inner.Received[1].Prompt);
        Assert.Equal("earlier", inner.Received[1].ResumeToken);
    }

    [Fact]
    public async Task A_taken_failure_with_nothing_to_resume_is_not_retried_and_says_so()
    {
        // sending the prompt again would send it twice
        var inner = new ScriptedAgentSession(
            Attempt(new TextDelta("half"), Failed(AgentFailureKind.Transient, AgentDelivery.Taken)));
        var clock = new ManualTimeProvider();

        var events = await RunToEnd(Retry(inner, clock), clock);

        Assert.Single(inner.Received);
        Assert.Contains("not retried", Terminal(events).Diagnostic);
        Assert.Contains("no session to resume", Terminal(events).Diagnostic);
    }

    [Fact]
    public async Task An_uncertain_failure_is_not_retried_and_says_so()
    {
        var inner = new ScriptedAgentSession(
            Attempt(new SessionStarted("s-1"), new ToolCall("Bash", "{}"),
                Failed(AgentFailureKind.Transient, AgentDelivery.Uncertain, "s-1")),
            Attempt(Succeeded()));
        var clock = new ManualTimeProvider();

        var ended = Terminal(await RunToEnd(Retry(inner, clock), clock));

        Assert.Single(inner.Received);
        Assert.Empty(clock.Requested);
        Assert.True(ended.IsError);
        Assert.Equal(AgentFailureKind.Transient, ended.Failure!.Kind);
        Assert.Contains("not retried: delivery uncertain", ended.Diagnostic);
        Assert.StartsWith("boom", ended.Diagnostic);
    }

    [Theory]
    [InlineData(AgentFailureKind.Limit, AgentDelivery.NeverSent)]
    [InlineData(AgentFailureKind.Access, AgentDelivery.NeverSent)]
    [InlineData(AgentFailureKind.Fatal, AgentDelivery.NeverSent)]
    [InlineData(AgentFailureKind.Limit, AgentDelivery.Taken)]
    [InlineData(AgentFailureKind.Fatal, AgentDelivery.Taken)]
    public async Task Only_a_transient_failure_is_retried(AgentFailureKind kind, AgentDelivery delivery)
    {
        var failed = Failed(kind, delivery, "s-1");
        var inner = new ScriptedAgentSession(Attempt(failed), Attempt(Succeeded()));
        var clock = new ManualTimeProvider();

        var events = await RunToEnd(Retry(inner, clock), clock);

        Assert.Single(inner.Received);
        Assert.Same(failed, Terminal(events));   // untouched: it was never a candidate
    }

    [Fact]
    public async Task A_failure_that_carries_no_reading_and_a_success_pass_through()
    {
        var unread = new SessionEnded(ProviderVerdict.Failed, true, null, null, null, "boom");
        var clock = new ManualTimeProvider();

        var failed = await RunToEnd(Retry(new ScriptedAgentSession(Attempt(unread)), clock), clock);
        var ok = Succeeded();
        var succeeded = await RunToEnd(Retry(new ScriptedAgentSession(Attempt(ok)), clock), clock);

        Assert.Same(unread, Terminal(failed));
        Assert.Same(ok, Terminal(succeeded));
    }

    [Fact]
    public async Task A_derived_options_type_reaches_the_next_attempt_intact()
    {
        var inner = new ScriptedAgentSession(
            Attempt(new SessionStarted("s-1"), Failed(AgentFailureKind.Transient, AgentDelivery.Taken)),
            Attempt(Succeeded()));
        var clock = new ManualTimeProvider();

        await RunToEnd(Retry(inner, clock), clock, new DoorOptions { Prompt = "p", Extra = "kept" });

        var second = Assert.IsType<DoorOptions>(inner.Received[1]);
        Assert.Equal("kept", second.Extra);
        Assert.Equal("continue", second.Prompt);
    }

    // ── what the caller sees ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_stream_forwards_every_event_and_holds_back_only_the_failed_terminal_it_retries()
    {
        var usage = new UsageFinal(1, 2, 0, 0, "m");
        var inner = new ScriptedAgentSession(
            Attempt(new SessionStarted("s-1"), new TextDelta("half"),
                Failed(AgentFailureKind.Transient, AgentDelivery.Taken), usage),
            Attempt(new SessionStarted("s-1"), new TextDelta(" rest"), Succeeded()));
        var clock = new ManualTimeProvider();

        var events = await RunToEnd(Retry(inner, clock), clock);

        Assert.Equal(
            [typeof(SessionStarted), typeof(TextDelta), typeof(UsageFinal),
             typeof(SessionStarted), typeof(TextDelta), typeof(SessionEnded)],
            events.Select(e => e.GetType()).ToArray());
        Assert.False(Terminal(events).IsError);
    }

    // ── the bound ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_bound_stops_the_retries_and_the_last_failure_says_the_budget_is_spent()
    {
        var inner = ScriptedAgentSession.Failing(5, Attempt(Failed(AgentFailureKind.Transient, AgentDelivery.NeverSent)));
        var clock = new ManualTimeProvider();

        var events = await RunToEnd(Retry(inner, clock, o => o.MaxRetries = 2), clock);

        Assert.Equal(3, inner.Received.Count);   // the first attempt and two retries
        Assert.Equal(2, clock.Requested.Count);
        Assert.Contains("retry budget of 2 is spent", Terminal(events).Diagnostic);
    }

    [Fact]
    public async Task Zero_retries_retries_nothing()
    {
        var inner = ScriptedAgentSession.Failing(2, Attempt(Failed(AgentFailureKind.Transient, AgentDelivery.NeverSent)));
        var clock = new ManualTimeProvider();

        await RunToEnd(Retry(inner, clock, o => o.MaxRetries = 0), clock);

        Assert.Single(inner.Received);
    }

    [Fact]
    public async Task Each_wait_doubles_up_to_the_ceiling()
    {
        var inner = ScriptedAgentSession.Failing(5, Attempt(Failed(AgentFailureKind.Transient, AgentDelivery.NeverSent)));
        var clock = new ManualTimeProvider();

        await RunToEnd(Retry(inner, clock, o =>
        {
            o.MaxRetries = 4;
            o.InitialDelay = TimeSpan.FromSeconds(2);
            o.MaxDelay = TimeSpan.FromSeconds(5);
        }), clock);

        Assert.Equal([2, 4, 5, 5], clock.Requested.Select(t => (int)t.TotalSeconds).ToArray());
    }

    [Fact]
    public async Task A_stated_retry_not_before_is_waited_for_when_it_is_later_than_the_backoff()
    {
        var clock = new ManualTimeProvider();
        var asked = new AgentRestriction(RestrictionCause.Capacity, RestrictionSubject.Endpoint,
            RestrictionRecovery.RetryNotBefore(clock.GetUtcNow().AddSeconds(10)));
        var inner = new ScriptedAgentSession(
            Attempt(Failed(AgentFailureKind.Transient, AgentDelivery.NeverSent, restriction: asked)),
            Attempt(Succeeded()));

        await RunToEnd(Retry(inner, clock), clock);

        Assert.Equal(10, clock.Requested.Single().TotalSeconds);
    }

    [Fact]
    public async Task A_retry_not_before_past_the_ceiling_is_not_waited_for_and_says_so()
    {
        var clock = new ManualTimeProvider();
        var asked = new AgentRestriction(RestrictionCause.Capacity, RestrictionSubject.Endpoint,
            RestrictionRecovery.RetryNotBefore(clock.GetUtcNow().AddMinutes(10)));
        var inner = new ScriptedAgentSession(
            Attempt(Failed(AgentFailureKind.Transient, AgentDelivery.NeverSent, restriction: asked)),
            Attempt(Succeeded()));

        var events = await RunToEnd(Retry(inner, clock), clock);

        Assert.Single(inner.Received);
        Assert.Empty(clock.Requested);
        Assert.Contains("asked for no retry", Terminal(events).Diagnostic);
    }

    [Fact]
    public async Task A_stated_reset_on_a_transient_failure_does_not_stretch_the_wait()
    {
        // a reset is for an allowance, which a transient failure is not: only retry-not-before is a fact about
        // the next attempt
        var clock = new ManualTimeProvider();
        var reset = new AgentRestriction(RestrictionCause.Capacity, RestrictionSubject.Endpoint,
            RestrictionRecovery.ResetAt(clock.GetUtcNow().AddMinutes(10)));
        var inner = new ScriptedAgentSession(
            Attempt(Failed(AgentFailureKind.Transient, AgentDelivery.NeverSent, restriction: reset)),
            Attempt(Succeeded()));

        await RunToEnd(Retry(inner, clock), clock);

        Assert.Equal(2, clock.Requested.Single().TotalSeconds);
    }

    // ── the wait ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_wait_holds_until_the_clock_reaches_it_and_not_a_moment_before()
    {
        var inner = new ScriptedAgentSession(
            Attempt(Failed(AgentFailureKind.Transient, AgentDelivery.NeverSent)),
            Attempt(Succeeded()));
        var clock = new ManualTimeProvider();
        var run = Task.Run(() => Retry(inner, clock).StreamAsync(Turn).ToListAsync().AsTask());

        await WaitUntil(() => clock.Pending == 1);
        clock.Advance(TimeSpan.FromMilliseconds(1999));
        await Task.Delay(50);   // real time passes, the fake clock does not: nothing may have moved
        Assert.False(run.IsCompleted);
        Assert.Single(inner.Received);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        var events = await run.WaitAsync(TestTimeouts.GateWait);

        Assert.Equal(2, inner.Received.Count);
        Assert.False(Terminal(events).IsError);
    }

    [Fact]
    public async Task A_cancellation_cuts_the_wait_and_no_further_attempt_is_made()
    {
        var inner = new ScriptedAgentSession(
            Attempt(Failed(AgentFailureKind.Transient, AgentDelivery.NeverSent)),
            Attempt(Succeeded()));
        var clock = new ManualTimeProvider();
        using var cts = new CancellationTokenSource();
        var run = Task.Run(() => Retry(inner, clock).StreamAsync(Turn, cts.Token).ToListAsync(cts.Token).AsTask());

        await WaitUntil(() => clock.Pending == 1);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TestTimeouts.GateWait));
        Assert.Single(inner.Received);
        Assert.Equal(0, clock.Pending);   // the timer was released, not left to fire into a dead turn
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TestTimeouts.GateWait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("the condition never held");
            await Task.Delay(1);
        }
    }

    // ── one layer, and off unless registered ─────────────────────────────────────────────────────

    [Fact]
    public void A_retrying_session_will_not_wrap_another()
    {
        var clock = new ManualTimeProvider();
        var once = Retry(new ScriptedAgentSession(), clock);

        var ex = Assert.Throws<InvalidOperationException>(() => Retry(once, clock));

        Assert.Contains("one layer owns the retry budget", ex.Message);
    }

    [Fact]
    public void A_bound_that_cannot_hold_is_refused()
    {
        var inner = new ScriptedAgentSession();
        var clock = new ManualTimeProvider();

        Assert.Throws<ArgumentOutOfRangeException>(() => Retry(inner, clock, o => o.MaxRetries = -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Retry(inner, clock, o => o.InitialDelay = TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Retry(inner, clock, o =>
        {
            o.InitialDelay = TimeSpan.FromSeconds(10);
            o.MaxDelay = TimeSpan.FromSeconds(1);
        }));
        Assert.Throws<ArgumentException>(() => Retry(inner, clock, o => o.ContinueWord = " "));
    }

    private static ServiceProvider Compose(ManualTimeProvider clock, Action<LyntaiBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddLyntai(configure);
        return services.BuildServiceProvider();
    }

    private static ScriptedAgentSession Flaky() =>
        ScriptedAgentSession.Failing(9, Attempt(Failed(AgentFailureKind.Transient, AgentDelivery.NeverSent)));

    [Fact]
    public void Nothing_is_retried_unless_AddAgentRetry_is_called()
    {
        var inner = Flaky();
        using var sp = Compose(new ManualTimeProvider(), b =>
        {
            b.Services.AddSingleton<IAgentSession>(inner);
            b.Services.AddKeyedSingleton<IAgentSession>("door", inner);
        });

        Assert.Same(inner, sp.GetRequiredService<IAgentSession>());
        Assert.Same(inner, sp.GetRequiredKeyedService<IAgentSession>("door"));
    }

    [Fact]
    public async Task AddAgentRetry_wraps_the_unkeyed_and_the_keyed_session_whatever_the_order()
    {
        var clock = new ManualTimeProvider();
        var plain = Flaky();
        var door = Flaky();
        var typed = Flaky();
        using var sp = Compose(clock, b =>
        {
            b.Services.AddSingleton<IAgentSession>(plain);              // registered BEFORE the call
            b.AddAgentRetry(o => o.MaxRetries = 1);
            b.Services.AddKeyedSingleton<IAgentSession>("door", (_, _) => door);   // and AFTER it
            b.Services.AddKeyedSingleton<IAgentSession>("typed", typed);
        });

        await RunToEnd(sp.GetRequiredService<IAgentSession>(), clock);
        await RunToEnd(sp.GetRequiredKeyedService<IAgentSession>("door"), clock);
        await RunToEnd(sp.GetRequiredKeyedService<IAgentSession>("typed"), clock);

        Assert.IsType<RetryingAgentSession>(sp.GetRequiredService<IAgentSession>());
        Assert.Equal(2, plain.Received.Count);
        Assert.Equal(2, door.Received.Count);
        Assert.Equal(2, typed.Received.Count);
    }

    [Fact]
    public async Task A_session_registered_by_type_is_wrapped_too()
    {
        var clock = new ManualTimeProvider();
        using var sp = Compose(clock, b =>
        {
            b.AddAgentRetry();
            b.Services.AddSingleton<IAgentSession, EmptyAgentSession>();
        });

        var events = await RunToEnd(sp.GetRequiredService<IAgentSession>(), clock);

        Assert.IsType<RetryingAgentSession>(sp.GetRequiredService<IAgentSession>());
        Assert.Empty(events);
    }

    private sealed class EmptyAgentSession : IAgentSession
    {
        public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
            AgentSessionOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield break;
        }
    }

    [Fact]
    public async Task Calling_AddAgentRetry_twice_leaves_one_layer_with_the_last_options()
    {
        var clock = new ManualTimeProvider();
        var inner = Flaky();
        using var sp = Compose(clock, b =>
        {
            b.Services.AddSingleton<IAgentSession>(inner);
            b.AddAgentRetry(o => o.MaxRetries = 5);
            b.AddAgentRetry(o => o.MaxRetries = 1);
        });

        await RunToEnd(sp.GetRequiredService<IAgentSession>(), clock);

        Assert.Equal(2, inner.Received.Count);   // two layers would have made it 4
    }

    [Fact]
    public void A_session_the_host_already_wrapped_is_refused_when_resolved()
    {
        var clock = new ManualTimeProvider();
        using var sp = Compose(clock, b =>
        {
            b.Services.AddSingleton<IAgentSession>(_ =>
                new RetryingAgentSession(Flaky(), new AgentRetryOptions(), clock));
            b.AddAgentRetry();
        });

        var ex = Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<IAgentSession>());

        Assert.Contains("one layer owns the retry budget", ex.Message);
    }

    [Fact]
    public void AddAgentRetry_refuses_a_bound_that_cannot_hold_where_it_is_set()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Compose(new ManualTimeProvider(), b => b.AddAgentRetry(o => o.MaxRetries = -1)));
    }
}
