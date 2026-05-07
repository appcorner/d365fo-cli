using System.Text.RegularExpressions;
using System.Xml.Linq;
using D365FO.Core;
using D365FO.Core.Index;
using Spectre.Console.Cli;

namespace D365FO.Cli.Commands.Analyze;

/// <summary>
/// Walks a workspace folder (or single model directory) and cross-checks
/// every AOT XML object against the local SQLite index, reporting broken
/// references.
///
/// Checks performed:
/// <list type="bullet">
///   <item><term>MISSING_DUTY</term> — AxSecurityRole references a duty not in the index.</item>
///   <item><term>MISSING_PRIVILEGE</term> — AxSecurityRole / AxSecurityDuty references a privilege not in the index.</item>
///   <item><term>MISSING_EDT</term> — AxTable field references an EDT not in the index.</item>
///   <item><term>MISSING_LABEL</term> — An AOT XML element value holds a label token (@File:Key) whose key has no indexed translation.</item>
/// </list>
///
/// Mirrors upstream MCP heuristic; ROADMAP §P4b.
/// </summary>
public sealed class AnalyzeCompletenessCommand : Command<AnalyzeCompletenessCommand.Settings>
{
    public sealed class Settings : D365OutputSettings
    {
        [CommandArgument(0, "<PATH>")]
        [System.ComponentModel.Description("Path to a model folder, PackagesLocalDirectory, or single AOT XML file to analyse.")]
        public string Path { get; init; } = "";

        [CommandOption("--skip-labels")]
        [System.ComponentModel.Description("Skip label-key existence checks (faster).")]
        public bool SkipLabels { get; init; }

        [CommandOption("--skip-edts")]
        [System.ComponentModel.Description("Skip EDT existence checks.")]
        public bool SkipEdts { get; init; }

        [CommandOption("--skip-security")]
        [System.ComponentModel.Description("Skip security role/duty/privilege cross-checks.")]
        public bool SkipSecurity { get; init; }
    }

    // Matches @LabelFile:LabelKey or @LabelKey (no file prefix).
    private static readonly Regex LabelTokenRegex =
        new(@"@(?:[A-Za-z0-9_]+:)?[A-Za-z0-9_]+", RegexOptions.Compiled);

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);
        var path = settings.Path.Trim();

        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail(
                D365FoErrorCodes.BadInput, $"Path not found: {path}"));

        var repo   = RepoFactory.Create();
        var issues = new List<object>();

        IEnumerable<string> xmlFiles = File.Exists(path)
            ? [path]
            : Directory.EnumerateFiles(path, "*.xml", SearchOption.AllDirectories);

        foreach (var file in xmlFiles)
        {
            XDocument doc;
            try
            {
                doc = XDocument.Load(file);
            }
            catch (Exception ex)
            {
                issues.Add(Issue("error", "PARSE_ERROR", file, $"Could not parse XML: {ex.Message}"));
                continue;
            }

            var rootName = doc.Root?.Name.LocalName ?? "";

            // ---- Security checks ------------------------------------------------
            if (!settings.SkipSecurity)
            {
                if (rootName == "AxSecurityRole")
                {
                    foreach (var dutyRef in doc.Descendants("AxSecurityDutyReference"))
                    {
                        var duty = dutyRef.Element("Name")?.Value;
                        if (!string.IsNullOrWhiteSpace(duty) && repo.GetSecurityDuty(duty) is null)
                            issues.Add(Issue("warning", "MISSING_DUTY", file,
                                $"Role references duty '{duty}' which is not in the index."));
                    }
                    foreach (var privRef in doc.Descendants("AxSecurityPrivilegeReference"))
                    {
                        var priv = privRef.Element("Name")?.Value;
                        if (!string.IsNullOrWhiteSpace(priv) && repo.GetSecurityPrivilege(priv) is null)
                            issues.Add(Issue("warning", "MISSING_PRIVILEGE", file,
                                $"Role references privilege '{priv}' which is not in the index."));
                    }
                }

                if (rootName == "AxSecurityDuty")
                {
                    foreach (var privRef in doc.Descendants("AxSecurityPrivilegeReference"))
                    {
                        var priv = privRef.Element("Name")?.Value;
                        if (!string.IsNullOrWhiteSpace(priv) && repo.GetSecurityPrivilege(priv) is null)
                            issues.Add(Issue("warning", "MISSING_PRIVILEGE", file,
                                $"Duty references privilege '{priv}' which is not in the index."));
                    }
                }
            }

            // ---- EDT checks (AxTable fields) ------------------------------------
            if (!settings.SkipEdts && rootName == "AxTable")
            {
                foreach (var field in doc.Descendants("AxTableField"))
                {
                    var edtName = field.Element("ExtendedDataType")?.Value
                                ?? field.Element("Edt")?.Value;
                    if (!string.IsNullOrWhiteSpace(edtName) && repo.GetEdt(edtName) is null)
                    {
                        var fieldName = field.Element("Name")?.Value ?? "(unknown)";
                        issues.Add(Issue("warning", "MISSING_EDT", file,
                            $"Field '{fieldName}' references EDT '{edtName}' which is not in the index."));
                    }
                }
            }

            // ---- Label checks (any element value @File:Key) ---------------------
            if (!settings.SkipLabels)
            {
                foreach (var el in doc.Descendants())
                {
                    if (el.HasElements) continue; // only leaf text nodes
                    var text = el.Value;
                    if (string.IsNullOrWhiteSpace(text) || !text.StartsWith('@')) continue;
                    var m = LabelTokenRegex.Match(text.Trim());
                    if (!m.Success) continue;
                    var token = m.Value;
                    var hits = repo.ResolveLabel(token);
                    if (hits.Count == 0)
                        issues.Add(Issue("warning", "MISSING_LABEL", file,
                            $"Element <{el.Name.LocalName}> references label '{token}' which has no indexed translation."));
                }
            }
        }

        var summary = new
        {
            path,
            issueCount = issues.Count,
            skipLabels   = settings.SkipLabels,
            skipEdts     = settings.SkipEdts,
            skipSecurity = settings.SkipSecurity,
            issues,
        };

        return RenderHelpers.Render(kind, issues.Count == 0
            ? ToolResult<object>.Success(summary)
            : ToolResult<object>.Success(summary, warnings: [$"{issues.Count} completeness issue(s) found."]));
    }

    private static object Issue(string severity, string code, string file, string message) =>
        new { severity, code, file = System.IO.Path.GetFileName(file), filePath = file, message };
}
