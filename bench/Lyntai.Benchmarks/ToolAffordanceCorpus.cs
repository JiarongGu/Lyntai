using System.Text.Json;
using Lyntai.Agents;

namespace Lyntai.Benchmarks;

/// <summary>The synthetic tool roster the <c>affordance</c> shape is measured on, and the executable
/// <see cref="ITool"/> that backs each entry.
///
/// <para><b>A fixture built to be measured — the weakest evidence tier this repository publishes</b>, and
/// the owner's call (2026-09-12) rather than a fallback. No corpus of real tool rosters exists here, and
/// the alternative was not measuring the shape at all.</para>
///
/// <para><b>Confusability is AUTHORED, inside a family.</b> Each of the six families holds exactly seven
/// tools with adjacent purposes, so a request is disambiguated against its six siblings rather than against
/// all 41 non-gold tools — which keeps the authoring constraint local enough to check by reading, and is
/// the mistake LoCoMo's plural evidence field already cost this repository once.</para>
///
/// <para><b>A request must not echo its tool's NAME or DESCRIPTION.</b> A prompt that names the answer in
/// an option label measures label-following (<c>.claude/knowledge/model-decoupling.md</c>), so every request
/// is written as a user would put it and the <c>cosine</c> arm is kept in the table as the readout of how
/// much surface leakage is left.</para></summary>
internal static class ToolAffordanceCorpus
{
    /// <summary>One roster entry: the declaration a model sees, plus the requests exactly this tool serves.
    /// <c>Requests</c> is the trial source — one trial per request, with the gold fixed.</summary>
    internal sealed record ToolSpec(
        string Family, string Name, string Description, string Schema, IReadOnlyList<string> Requests);

    internal const int FamilySize = 7;

    internal const int RequestsPerTool = 4;

    private static string Schema(params string[] properties)
    {
        var props = string.Join(",", properties.Select(p =>
        {
            var (name, type) = p.EndsWith('#') ? (p[..^1], "integer") : (p, "string");
            return $"\"{name}\":{{\"type\":\"{type}\"}}";
        }));
        var required = string.Join(",", properties.Select(p => $"\"{p.TrimEnd('#')}\""));
        return $"{{\"type\":\"object\",\"properties\":{{{props}}},\"required\":[{required}]}}";
    }

    internal static IReadOnlyList<ToolSpec> Tools { get; } =
    [
        // ── weather ───────────────────────────────────────────────────────────────────────────────────────
        new("weather", "current_conditions",
            "Weather observed at a place at this moment: temperature, wind, humidity, and whether "
            + "precipitation is falling right now.",
            Schema("place"),
            [
                "Is it raining on me in Lisbon at this very moment?",
                "How cold is it outside the office in Helsinki as we speak?",
                "Do I need to grab an umbrella before I walk out the door in Seattle?",
                "Is the wind bad enough in Chicago right now to bother with a hat?",
            ]),
        new("weather", "daily_forecast",
            "Predicted weather for a place on a named upcoming calendar day, up to ten days ahead.",
            Schema("place", "day"),
            [
                "Should we book the picnic for Saturday in Austin?",
                "Will Tuesday be dry enough to paint the fence in Manchester?",
                "What are we in for in Denver next Friday?",
                "Is it worth planning a beach day in Nice this coming weekend?",
            ]),
        new("weather", "severe_alerts",
            "Official government warnings in force for a region — storm, flood, heat, wildfire or "
            + "evacuation notices.",
            Schema("region"),
            [
                "Has anyone official told people in Osaka to stay indoors?",
                "Are the authorities warning about flooding anywhere in Valencia province?",
                "Is there a formal evacuation notice out for Sonoma County?",
                "Did the government declare a heat emergency for greater Athens?",
            ]),
        new("weather", "air_quality",
            "Pollutant and pollen concentrations at a place, with the health advisory band they fall in.",
            Schema("place"),
            [
                "My son's asthma is flaring — is it safe to send him out in Delhi today?",
                "Should I keep the windows shut in Krakow because of the smog?",
                "Is the pollen going to wreck me in Dallas this afternoon?",
                "How unhealthy is the haze for a run in Jakarta?",
            ]),
        new("weather", "marine_conditions",
            "Sea state at a coastal point: wave height, swell period, water temperature and tide times.",
            Schema("place"),
            [
                "Will a small boat have a rough time off Galway tomorrow morning?",
                "Is the swell at Ericeira going to be surfable or flat?",
                "When does the water go out at Mont-Saint-Michel so we can walk across?",
                "Is the sea warm enough to swim without a wetsuit at Brighton?",
            ]),
        new("weather", "uv_index",
            "Ultraviolet radiation strength at a place, and how long unprotected skin can be exposed safely.",
            Schema("place"),
            [
                "How long can my kid stay out before burning in Cairns at noon?",
                "Do I actually need sunscreen in Oslo in March?",
                "Is it strong enough in Nairobi to burn through a thin shirt?",
                "What time of day is safest for a very pale person outdoors in Perth?",
            ]),
        new("weather", "historical_weather",
            "Observations already recorded for a place on a date in the past.",
            Schema("place", "date"),
            [
                "What was it actually like in Reykjavik on the day we got married in 2019?",
                "Was it really that hot in Paris in August 2003, or am I misremembering?",
                "Did it snow in Rome on Christmas Day 2010?",
                "How much rain fell on Kerala during the last week of July 2018?",
            ]),

        // ── calendar ──────────────────────────────────────────────────────────────────────────────────────
        new("calendar", "create_event",
            "Put a new appointment on the calendar at a stated time, optionally inviting people.",
            Schema("title", "start"),
            [
                "Block out an hour with Priya on Thursday at two to go over the budget.",
                "Put the dentist down for the 14th at nine in the morning.",
                "Get the quarterly review on everyone's calendar for next Monday afternoon.",
                "Set up the standup for tomorrow at 9:15 with the platform team.",
            ]),
        new("calendar", "find_free_slot",
            "Search several people's calendars for a window in which all of them are unbooked.",
            Schema("people", "duration_minutes#"),
            [
                "When can the four of us actually all be in a room for ninety minutes?",
                "Is there any gap next week where Tom, Lena and I are all clear?",
                "Find me half an hour where nobody on the design team is busy.",
                "What day works for a two-hour workshop given everything already booked?",
            ]),
        new("calendar", "list_events",
            "Read back what is already booked on the calendar over a stated range.",
            Schema("range"),
            [
                "What have I got on tomorrow?",
                "Read me everything on the books for the rest of this week.",
                "How packed is my Thursday?",
                "What is already in the diary between now and the end of the month?",
            ]),
        new("calendar", "cancel_event",
            "Remove an appointment that is already booked and notify the people invited to it.",
            Schema("event"),
            [
                "Kill the Friday sync and let everyone know.",
                "I am not going to make the three o'clock — take it off and tell them.",
                "Drop the offsite planning session entirely.",
                "Scrap tomorrow's interview slot and notify the candidate.",
            ]),
        new("calendar", "set_reminder",
            "Create a one-off nudge at a stated time that is not an appointment and occupies no calendar slot.",
            Schema("text", "at"),
            [
                "Poke me at four to take the bread out of the oven.",
                "Nudge me an hour before the flight to check in — nothing in the diary, just a nudge.",
                "Just tell me on Sunday night that the bins go out.",
                "Ping me in twenty minutes so I do not forget the kettle.",
            ]),
        new("calendar", "convert_timezone",
            "Translate a stated wall-clock time from one place's local time into another's.",
            Schema("time", "from", "to"),
            [
                "If it is nine in the morning in Sydney, what is it in Lisbon?",
                "Our call is at 4pm their time in Bogota — when is that for me in Berlin?",
                "What is the New York equivalent of 13:00 Tokyo?",
                "Is 7am Pacific a civilised hour in Helsinki?",
            ]),
        new("calendar", "business_hours",
            "When a named venue or office is open to the public on a given day.",
            Schema("place", "day"),
            [
                "Will the passport office still be letting people in if I get there at 4:30?",
                "Is the hardware shop on Kirkgate open on a Sunday at all?",
                "What time does the embassy stop taking walk-ins on Fridays?",
                "Can I get to the post office before it shuts today?",
            ]),

        // ── documents ─────────────────────────────────────────────────────────────────────────────────────
        new("documents", "search_documents",
            "Find documents across the shared drive by their content, author or metadata.",
            Schema("query"),
            [
                "Where is the thing we wrote up about the Hanford contract?",
                "Find whatever we have on file about last year's price increase.",
                "I know there is a spreadsheet somewhere with the 2024 headcount in it.",
                "Dig out anything Priya authored about onboarding.",
            ]),
        new("documents", "read_document",
            "Return the full text of one identified document, unabridged.",
            Schema("document"),
            [
                "Open up the Q3 board pack and show me what it actually says.",
                "Pull the whole text of the signed lease so I can read it properly.",
                "Give me contract-2291 verbatim, every clause.",
                "I want to see every word of the incident write-up, not a precis.",
            ]),
        new("documents", "summarize_document",
            "Condense one identified document into a short account of its substance.",
            Schema("document"),
            [
                "Give me the gist of the ninety-page compliance manual.",
                "I have no time for the whole board pack — what is the shape of it?",
                "Boil the vendor contract down to the bits that matter.",
                "Tell me in a paragraph what the incident write-up concluded.",
            ]),
        new("documents", "list_recent_files",
            "Show what has been created or edited lately across the drive, newest first.",
            Schema("days#"),
            [
                "What has been touched around here in the last couple of days?",
                "Show me what changed while I was on leave.",
                "What did the team actually produce this week?",
                "Which files have moved since Friday?",
            ]),
        new("documents", "move_file",
            "Relocate a document into a different folder.",
            Schema("document", "destination"),
            [
                "Get the invoice out of my downloads and into the finance folder.",
                "This belongs under Archive/2025, not where it is sitting now.",
                "Shift the whole onboarding deck into the shared HR space.",
                "Put contract-2291 where the rest of the legal paperwork lives.",
            ]),
        new("documents", "share_document",
            "Grant another person access to a document they cannot currently open.",
            Schema("document", "person"),
            [
                "Let Priya in on the budget sheet.",
                "Give the auditors read access to the ledger export.",
                "Tom cannot open the deck — sort him out.",
                "Make sure the new starter can get at the onboarding pack.",
            ]),
        new("documents", "restore_version",
            "Bring back an earlier saved state of a document, discarding later edits.",
            Schema("document", "version"),
            [
                "Someone has wrecked the pricing sheet — put it back to how it was yesterday.",
                "Roll the policy doc back to before Tuesday's edits.",
                "I want the state of the deck from before the rewrite.",
                "Undo whatever happened to the budget file this morning.",
            ]),

        // ── finance ───────────────────────────────────────────────────────────────────────────────────────
        new("finance", "exchange_rate",
            "The current conversion rate between two currencies.",
            Schema("from", "to"),
            [
                "How many yen do I get for a pound today?",
                "What is a euro worth in Brazilian money right now?",
                "Is the dollar still buying about nine tenths of a euro?",
                "Roughly what is the Swiss franc against sterling this morning?",
            ]),
        new("finance", "stock_quote",
            "The current traded price of a named listed company's shares.",
            Schema("symbol"),
            [
                "What is Maersk trading at?",
                "Where did Nintendo close today?",
                "Is the utility we hold up or down this morning?",
                "Give me the last price on the Tokyo listing.",
            ]),
        new("finance", "list_transactions",
            "Itemise the individual payments in and out of an account over a period.",
            Schema("account", "period"),
            [
                "What actually went out of the current account last month?",
                "Show me every card payment since the first.",
                "Where did all the money go in March?",
                "I need it line by line for the business account this quarter.",
            ]),
        new("finance", "account_balance",
            "The amount of money presently held in an account.",
            Schema("account"),
            [
                "How much is actually in there right now?",
                "Have I got enough in the joint account to cover the boiler?",
                "What is sitting in the savings pot today?",
                "Am I overdrawn on the business account?",
            ]),
        new("finance", "create_invoice",
            "Issue a bill to a customer for a stated amount.",
            Schema("customer", "amount"),
            [
                "Bill Hendricks for the two days of consulting.",
                "Send the council what they owe for the survey work.",
                "Raise the paperwork for 4,200 against the Larsen job.",
                "Get something out to the new client for the deposit they owe.",
            ]),
        new("finance", "refund_payment",
            "Return money to a customer for a payment already taken.",
            Schema("payment", "amount"),
            [
                "Give Mrs Okafor her money back for the cancelled order.",
                "We took too much off that card — put the difference back.",
                "Reverse the charge on order 8841.",
                "The customer sent it back, so return what they paid.",
            ]),
        new("finance", "estimate_shipping",
            "Quote the cost and transit time to send a parcel between two places.",
            Schema("from", "to", "weight_kg#"),
            [
                "What will it cost to get a five-kilo box to Oslo?",
                "How long and how much to send this to a customer in Chile?",
                "Is it cheaper to post the samples or courier them to Milan?",
                "Price up sending a pallet from the Leeds warehouse to Cork.",
            ]),

        // ── communication ─────────────────────────────────────────────────────────────────────────────────
        new("communication", "send_email",
            "Deliver a new message to named recipients immediately.",
            Schema("to", "subject", "body"),
            [
                "Get a note out to the whole team that the office is shut Monday.",
                "Write to the landlord about the leak and let it go straight away.",
                "Tell the supplier we are going ahead, and do not sit on it.",
                "Let the candidate know they have got the job.",
            ]),
        new("communication", "draft_reply",
            "Compose a response to a message already received and hold it for the user to approve.",
            Schema("message"),
            [
                "Have a go at answering Priya's note but let me read it first.",
                "Put something together in response to the complaint — I will approve it.",
                "Work up an answer to the auditor's questions for me to check over.",
                "Write back to the landlord, but hold it for my review.",
            ]),
        new("communication", "search_inbox",
            "Find messages already received, by sender, subject or content.",
            Schema("query"),
            [
                "Where is that note from the insurer about the claim?",
                "Did the accountant ever actually write to me about the filing?",
                "Find whatever came in from the council last month.",
                "I am sure someone sent me the access codes — dig it up.",
            ]),
        new("communication", "schedule_send",
            "Hold a composed message and deliver it at a stated future time.",
            Schema("message", "at"),
            [
                "Write it now but do not let it land until Monday morning.",
                "Have this go out at nine their time, not at midnight.",
                "Queue the newsletter for Thursday at eight.",
                "Let this one go after the embargo lifts on the 3rd.",
            ]),
        new("communication", "create_group_chat",
            "Open a new multi-person conversation thread.",
            Schema("people", "topic"),
            [
                "Get the three of us into one thread about the Larsen job.",
                "Set up somewhere the whole incident crew can talk in real time.",
                "Spin up a room for the launch team.",
                "I want a single conversation with legal, finance and me in it.",
            ]),
        new("communication", "translate_text",
            "Render a supplied passage into another language.",
            Schema("text", "to"),
            [
                "Put this paragraph into Portuguese for the Lisbon office.",
                "What does this German clause say in English?",
                "Turn the safety notice into Spanish for the site.",
                "Give me the Japanese for the whole apology note.",
            ]),
        new("communication", "transcribe_audio",
            "Turn a recording of speech into written text.",
            Schema("recording"),
            [
                "Turn the voicemail into something I can read.",
                "Write out what was said on the call recording.",
                "I need the interview tape as text.",
                "Get the words out of the site inspection recording.",
            ]),

        // ── operations ────────────────────────────────────────────────────────────────────────────────────
        new("operations", "service_status",
            "Whether a named service is currently up, degraded or down.",
            Schema("service"),
            [
                "Is checkout working at all right now?",
                "Customers say the app is broken — is it actually?",
                "Are we up?",
                "Is payments healthy this minute?",
            ]),
        new("operations", "restart_service",
            "Stop and start a named service to clear its state.",
            Schema("service"),
            [
                "Give the queue worker a kick.",
                "Bounce the API and see if that clears it.",
                "Turn the search node off and on again.",
                "Cycle the session service — it is wedged.",
            ]),
        new("operations", "tail_logs",
            "Stream the most recent lines a named service has written to its log.",
            Schema("service", "lines#"),
            [
                "Show me what it is actually printing right now.",
                "I want the last few hundred lines out of the worker.",
                "What is it saying as the requests come in?",
                "Let me watch the output live while I reproduce it.",
            ]),
        new("operations", "list_deployments",
            "Show what has been released to an environment and when.",
            Schema("environment"),
            [
                "What went out to production this week?",
                "When was the last time anyone shipped to staging?",
                "Who released what, and when, over the last day?",
                "Has anything at all changed in prod since Tuesday?",
            ]),
        new("operations", "rollback_deployment",
            "Return an environment to a version released earlier.",
            Schema("environment", "version"),
            [
                "Put production back on whatever it was running yesterday.",
                "Undo the release — get us back to the last good one.",
                "Take staging back to build 4471.",
                "Revert prod to what it was before lunch.",
            ]),
        new("operations", "error_rate",
            "The proportion of requests a service is failing over a period.",
            Schema("service", "period"),
            [
                "How many of our requests are actually failing?",
                "What proportion blew up in the last hour?",
                "Is the failure percentage climbing or flat?",
                "Give me the share of five-hundreds on checkout since midnight.",
            ]),
        new("operations", "create_incident",
            "Open a formal incident record and page the on-call rota.",
            Schema("title", "severity"),
            [
                "This is bad enough to wake someone up — make it official.",
                "Declare it and get the on-call engineer paged.",
                "Open a formal record so the postmortem has something to hang off.",
                "Raise it as a sev-1 and pull people in.",
            ]),
    ];

    /// <summary>Requests NO tool in this corpus serves, where the right answer is a direct one and any tool
    /// call is a false positive.
    ///
    /// <para><b>Without these the corpus cannot refute a prompt change.</b> Every trial above has exactly
    /// one correct tool, so an instruction pushing harder toward tool use scores better on ALL of them and
    /// the regression it causes — fabricating a call when nothing fits — is invisible by construction. That
    /// is the fail-open shape this repository files everywhere, one level up: the fixture agrees with you.</para>
    ///
    /// <para>They are deliberately ADJACENT to the roster's domains — chit-chat would be refused by anything
    /// and would measure nothing. Each is a plausible request that the 42 tools genuinely cannot serve.</para>
    /// </summary>
    internal static IReadOnlyList<string> NoToolRequests { get; } =
    [
        "Why does my laptop fan get loud when I open a lot of browser tabs?",
        "What is a reasonable notice period to give when resigning?",
        "Explain the difference between a bond and a share to someone with no finance background.",
        "Is it rude to decline a wedding invitation from a colleague?",
        "How many litres of paint should I buy for a room four metres by three?",
        "Talk me through what usually causes a sourdough starter to go flat.",
        "What is the origin of the phrase 'reading the riot act'?",
        "Should I learn to drive a manual or an automatic first?",
        "Give me three arguments against holding a daily standup.",
        "What does it mean when a contract says 'time is of the essence'?",
        "Roughly how long does it take to get fit enough to run ten kilometres?",
        "Why do aeroplane windows have a small hole in them?",
        "What is the difference between a recession and a depression?",
        "How do I tell whether a mushroom I found is safe to eat?",
        "Summarise the plot of Hamlet in two sentences.",
        "Is it better to rent or buy a flat if I might move in three years?",
        "What should I look for when choosing a bicycle helmet?",
        "Explain why the sky is red at sunset.",
        "How do I politely tell a neighbour their music is too loud?",
        "What are the main differences between arabica and robusta coffee?",
    ];

    /// <summary>The declaration line a model sees for one tool — mirrored from <c>ToolLoop</c>'s private
    /// system-prompt builder so the <c>select</c> cross-shape arm shows byte-identical tool text.
    /// <para>A MIRROR of code this bench cannot call, so the two can drift. The prompt-size control is what
    /// notices: it reads the chars the transport actually sent, not the chars this method composed.</para>
    /// </summary>
    internal static string Declare(ToolSpec spec) =>
        $"- {spec.Name}: {spec.Description}\n  arguments JSON schema: {spec.Schema}\n";

    /// <summary>What the embedder and the cross-encoder score a request against — the SAME text the model
    /// reads, so no arm is scoring a representation another arm was not shown.</summary>
    internal static string Declaration(ToolSpec spec) => $"{spec.Name}: {spec.Description}";

    /// <summary>Structural defects that make the corpus unmeasurable rather than merely hard: a duplicate
    /// tool name (the registry keeps the first and the gold silently becomes unreachable), a family that is
    /// not exactly <see cref="FamilySize"/> (a roster of seven cannot be filled from it), a tool with the
    /// wrong request count, or two tools claiming the same request.</summary>
    internal static IReadOnlyList<string> Audit()
    {
        var problems = new List<string>();

        foreach (var group in Tools.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            problems.Add($"duplicate tool name '{group.Key}' ({group.Count()} entries)");

        foreach (var family in Tools.GroupBy(t => t.Family).Where(f => f.Count() != FamilySize))
            problems.Add($"family '{family.Key}' holds {family.Count()} tools, not {FamilySize}");

        foreach (var tool in Tools.Where(t => t.Requests.Count != RequestsPerTool))
            problems.Add($"tool '{tool.Name}' carries {tool.Requests.Count} requests, not {RequestsPerTool}");

        foreach (var group in Tools.SelectMany(t => t.Requests.Select(r => (Tool: t.Name, Request: r)))
                     .GroupBy(x => x.Request, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            problems.Add($"request claimed by {string.Join(" and ", group.Select(x => x.Tool))}: \"{group.Key}\"");

        foreach (var tool in Tools)
        {
            try { using var _ = JsonDocument.Parse(tool.Schema); }
            catch (JsonException ex) { problems.Add($"tool '{tool.Name}' has an unparseable schema: {ex.Message}"); }
        }

        return problems;
    }

    /// <summary>An executable roster entry. It VALIDATES the arguments against its own declared schema and
    /// reports a violation as an <c>error: malformed arguments …</c> observation.
    ///
    /// <para><b>That is the whole reason it validates.</b> Choosing the right tool and passing usable
    /// arguments are two outcomes, and a forced choice cannot express the second at all — so a tool that
    /// accepted anything would make the transport look better than it is by silently absorbing the failure
    /// mode the shape is being priced for.</para>
    ///
    /// <para>The observation is deliberately CONTENTLESS about correctness: a result that hinted the choice
    /// was wrong would let the loop's second turn correct itself, and the measurement reads the first
    /// step.</para></summary>
    internal sealed class SyntheticTool : ITool
    {
        /// <summary>Marker the sweep counts malformed-argument observations by; it sits under
        /// <c>ToolObservations.ErrorPrefix</c> so the loop also flags it as an error observation.</summary>
        internal const string MalformedPrefix = "error: malformed arguments";

        private readonly IReadOnlyList<(string Name, string Type)> _required;

        internal SyntheticTool(ToolSpec spec)
        {
            Name = spec.Name;
            Description = spec.Description;
            ParametersJsonSchema = spec.Schema;

            using var doc = JsonDocument.Parse(spec.Schema);
            var properties = doc.RootElement.GetProperty("properties");
            _required = [.. doc.RootElement.GetProperty("required").EnumerateArray()
                .Select(r => r.GetString()!)
                .Select(name => (name, properties.GetProperty(name).GetProperty("type").GetString()!))];
        }

        public string Name { get; }

        public string? Description { get; }

        public string? ParametersJsonSchema { get; }

        public Task<string> InvokeAsync(string argumentsJson, CancellationToken ct = default)
        {
            JsonElement args;
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return Task.FromResult($"{MalformedPrefix}: expected a JSON object");
                args = doc.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                return Task.FromResult($"{MalformedPrefix}: {ex.Message}");
            }

            foreach (var (name, type) in _required)
            {
                if (!args.TryGetProperty(name, out var value))
                    return Task.FromResult($"{MalformedPrefix}: '{name}' is required and was not supplied");
                var ok = type switch
                {
                    "integer" => value.ValueKind == JsonValueKind.Number
                        || (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out _)),
                    _ => value.ValueKind == JsonValueKind.String && value.GetString()?.Length > 0,
                };
                if (!ok) return Task.FromResult($"{MalformedPrefix}: '{name}' is not a usable {type}");
            }

            return Task.FromResult($"{{\"ok\":true,\"source\":\"{Name}\"}}");
        }
    }
}
