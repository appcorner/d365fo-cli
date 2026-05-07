using D365FO.Core;
using D365FO.Core.Index;
using Spectre.Console.Cli;

namespace D365FO.Cli.Commands.Suggest;

/// <summary>
/// Suggests indexed Extended Data Types for a field name using name
/// similarity heuristics. Mirrors upstream MCP <c>suggest_edt</c>.
/// </summary>
public sealed class SuggestEdtCommand : Command<SuggestEdtCommand.Settings>
{
    public sealed class Settings : D365OutputSettings
    {
        [CommandArgument(0, "<FIELDNAME>")]
        [System.ComponentModel.Description("Field name to suggest an EDT for, e.g. CustomerAccount, OrderAmount.")]
        public string FieldName { get; init; } = "";

        [CommandOption("-l|--limit <N>")]
        public int Limit { get; init; } = 5;
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);
        if (string.IsNullOrWhiteSpace(settings.FieldName))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("BAD_INPUT", "Field name required."));

        var suggestions = EdtSuggester.Suggest(RepoFactory.Create(), settings.FieldName, settings.Limit)
            .Select(s => new
            {
                name = s.Edt.Name,
                model = s.Edt.Model,
                extends = s.Edt.Extends,
                baseType = s.Edt.BaseType,
                stringSize = s.Edt.StringSize,
                confidence = s.Confidence,
                reason = s.Reason,
            })
            .ToList();
        return RenderHelpers.Render(kind, ToolResult<object>.Success(new
        {
            fieldName = settings.FieldName,
            count = suggestions.Count,
            suggestions,
        }));
    }
}

/// <summary>
/// Recommends the best extensibility strategy for a D365FO target object
/// (Class, Table, or Form): CoC wrapper, event handler, or AOT extension.
/// Analyses what the target exposes (delegate count, sealed methods,
/// existing extensions) and outputs ranked options with one-line rationale.
/// Mirrors upstream MCP <c>suggest_extension_strategy</c>. ROADMAP §P4a.
/// </summary>
public sealed class SuggestExtensionCommand : Command<SuggestExtensionCommand.Settings>
{
    public sealed class Settings : D365OutputSettings
    {
        [CommandArgument(0, "<TARGET>")]
        [System.ComponentModel.Description("Name of the class, table, or form to analyse.")]
        public string Target { get; init; } = "";

        [CommandOption("--kind <KIND>")]
        [System.ComponentModel.Description("Hint for target kind: Class, Table, or Form. Auto-detected when omitted.")]
        public string? Kind { get; init; }
    }

    private sealed record Recommendation(
        string Approach,
        string Rationale,
        string Confidence,
        string? Command);

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);
        if (string.IsNullOrWhiteSpace(settings.Target))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("BAD_INPUT", "Target name required."));

        var repo = RepoFactory.Create();
        var target = settings.Target.Trim();

        // --- auto-detect kind ---
        string? detectedKind = settings.Kind?.Trim();
        ClassDetails? classDetails = null;
        TableDetails? tableDetails = null;
        FormDetails?  formDetails  = null;

        if (string.IsNullOrEmpty(detectedKind) || detectedKind.Equals("Class", StringComparison.OrdinalIgnoreCase))
        {
            classDetails = repo.GetClassDetails(target);
            if (classDetails is not null) detectedKind = "Class";
        }
        if (classDetails is null && (string.IsNullOrEmpty(detectedKind) || detectedKind.Equals("Table", StringComparison.OrdinalIgnoreCase)))
        {
            tableDetails = repo.GetTableDetails(target);
            if (tableDetails is not null) detectedKind = "Table";
        }
        if (classDetails is null && tableDetails is null && (string.IsNullOrEmpty(detectedKind) || detectedKind.Equals("Form", StringComparison.OrdinalIgnoreCase)))
        {
            formDetails = repo.GetForm(target);
            if (formDetails is not null) detectedKind = "Form";
        }

        if (classDetails is null && tableDetails is null && formDetails is null)
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("NOT_FOUND",
                $"'{target}' not found in the index as Class, Table, or Form. Run `d365fo index refresh` if recently added."));

        // --- gather signals ---
        var cocCount      = classDetails is not null ? repo.FindCocExtensions(target).Count : 0;
        var handlerCount  = repo.FindEventSubscribers(target).Count;
        var extensionCount= repo.FindExtensions(target).Count;
        var delegates     = classDetails?.Methods.Count(m => m.Name.StartsWith("delegate", StringComparison.OrdinalIgnoreCase)) ?? 0;

        // Detect [Hookable(false)] / [Wrappable(false)] by checking if NO CoC wrappers exist
        // despite the class being non-final. This is a heuristic — the index does not store
        // attribute declarations, so we cannot detect them directly.
        var isFinal    = classDetails?.Class.IsFinal ?? false;
        var isAbstract = classDetails?.Class.IsAbstract ?? false;
        var methodCount= classDetails?.Methods.Count ?? 0;

        var recs = new List<Recommendation>();

        switch (detectedKind)
        {
            case "Class":
                // CoC: strongly preferred when class is not final; even final
                // classes can be wrapped with [Wrappable(true)].
                if (!isFinal)
                {
                    var cocConf = cocCount > 5 ? "medium" : "high";
                    var cocNote = cocCount > 0
                        ? $"{cocCount} existing CoC wrapper(s) already present."
                        : "No existing CoC wrappers — clean slate.";
                    recs.Add(new Recommendation(
                        "CoC (Chain of Command)",
                        $"Class is non-final and wrappable. {cocNote} Wrap individual methods to extend behaviour without replacing them.",
                        cocConf,
                        $"d365fo generate coc {target} --method <method> --out <PATH>"));
                }
                else
                {
                    recs.Add(new Recommendation(
                        "CoC (Chain of Command)",
                        "Class is marked final. CoC wrapping may still work if the method carries [Wrappable(true)]. Verify before scaffolding.",
                        "low",
                        $"d365fo generate coc {target} --method <method> --out <PATH>"));
                }

                // Event handler: good when delegates exist, or as a lightweight hook.
                if (delegates > 0)
                {
                    recs.Add(new Recommendation(
                        "Event handler (delegate subscribe)",
                        $"{delegates} delegate(s) found on {target}. Subscribe with [SubscribesTo] for zero-coupling extension.",
                        "high",
                        $"d365fo generate event-handler --source-kind Class --source {target} --event <delegateName> --out <PATH>"));
                }
                else
                {
                    recs.Add(new Recommendation(
                        "Event handler (pre/post)",
                        $"No custom delegates found. Pre/post handlers via [DataEventHandler] attach before/after standard events.",
                        "medium",
                        $"d365fo generate event-handler --source-kind Class --source {target} --event <EventName> --out <PATH>"));
                }

                // Class extension (add new methods / members without CoC).
                recs.Add(new Recommendation(
                    "Class extension",
                    $"Add new public methods or parm accessors to {target} via an extension class without touching the base object.",
                    "medium",
                    $"d365fo generate extension Class {target} <Suffix> --out <PATH>"));
                break;

            case "Table":
                recs.Add(new Recommendation(
                    "Table extension",
                    $"Add new fields, field groups, indexes, or relations to {target} without modifying the base table XML.",
                    "high",
                    $"d365fo generate extension Table {target} <Suffix> --out <PATH>"));
                if (delegates > 0)
                {
                    recs.Add(new Recommendation(
                        "Event handler (data event)",
                        $"{delegates} delegate(s) found on {target}.",
                        "high",
                        $"d365fo generate event-handler --source-kind Table --source {target} --event <Inserted|Updated|Deleted> --out <PATH>"));
                }
                else
                {
                    recs.Add(new Recommendation(
                        "Event handler (data event)",
                        $"Subscribe to standard DataEventType (Inserted, Updated, Deleted, etc.) on {target}.",
                        "high",
                        $"d365fo generate event-handler --source-kind Table --source {target} --event Inserted --out <PATH>"));
                }
                recs.Add(new Recommendation(
                    "CoC (table method wrapping)",
                    $"Wrap insert(), update(), delete(), or any other method on {target}'s table class.",
                    "medium",
                    $"d365fo generate coc {target} --method insert --out <PATH>"));
                break;

            case "Form":
                recs.Add(new Recommendation(
                    "Form extension",
                    $"Add controls, datasources, or menu items to {target} without replacing the base form.",
                    "high",
                    $"d365fo generate extension Form {target} <Suffix> --out <PATH>"));
                recs.Add(new Recommendation(
                    "Event handler (form event)",
                    $"Subscribe to FormEventType (Initialized, Closed, etc.) or FormDataSourceEventType on {target}.",
                    "high",
                    $"d365fo generate event-handler --source-kind Form --source {target} --event Initialized --out <PATH>"));
                recs.Add(new Recommendation(
                    "CoC (form method wrapping)",
                    $"Wrap init(), run(), or closeCanExecute() on {target}'s form class.",
                    "medium",
                    $"d365fo generate coc {target} --method init --out <PATH>"));
                break;
        }

        return RenderHelpers.Render(kind, ToolResult<object>.Success(new
        {
            target,
            targetKind = detectedKind,
            signals = new
            {
                isFinal,
                isAbstract,
                methodCount,
                delegateCount    = delegates,
                existingCoc      = cocCount,
                existingHandlers = handlerCount,
                existingExtensions = extensionCount,
            },
            recommendations = recs.Select((r, i) => new
            {
                rank       = i + 1,
                approach   = r.Approach,
                rationale  = r.Rationale,
                confidence = r.Confidence,
                command    = r.Command,
            }).ToList(),
        }));
    }
}

