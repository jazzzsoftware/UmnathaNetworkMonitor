using System.Linq;
using System.Net;
using System.Text;
using NetworkMonitor.UITests.Runner;

namespace NetworkMonitor.UITests.Evidence
{
    public static class HtmlReport
    {
        private const string ReportFileName = "report.html";

        // Dark by default, because the report is read at the end of a long run. The light block is
        // an override for anyone who has asked their browser for light, not the other way round.
        private const string CssStyle = """
            :root {
                --page: #16181c;
                --panel: #1f2228;
                --ink: #dfe3ea;
                --ink-quiet: #9aa3b2;
                --rule: #2e333c;
                --pass: #4cc38a;
                --fail: #f2726b;
                --skip: #e0b341;
                --fail-wash: #2a1c1e;
            }
            body { font-family: Segoe UI, Arial, sans-serif; margin: 2rem; color: var(--ink); background: var(--page); }
            h1 { text-transform: uppercase; letter-spacing: 0.05em; }
            h1.passed { color: var(--pass); }
            h1.failed { color: var(--fail); }
            h1.aborted { color: var(--fail); }
            section { margin-bottom: 2rem; padding-bottom: 1rem; border-bottom: 1px solid var(--rule); }
            .counts { list-style: none; padding: 0; display: flex; gap: 1.5rem; }
            .phase { margin-bottom: 1rem; padding: 0.75rem 1rem; background: var(--panel); border-radius: 6px; }
            .duration { color: var(--ink-quiet); font-weight: normal; font-size: 0.85em; }
            .badge.aborted { color: var(--page); background: var(--fail); padding: 0.1em 0.5em; border-radius: 4px; font-size: 0.75em; }
            ul.steps { list-style: none; padding-left: 0; }
            ul.steps li { padding: 0.25rem 0; border-left: 4px solid transparent; padding-left: 0.5rem; }
            ul.steps li.passed { border-left-color: var(--pass); }
            ul.steps li.failed { border-left-color: var(--fail); }
            ul.steps li.skipped { border-left-color: var(--skip); }
            .outcome { font-weight: bold; margin-right: 0.5em; }
            .message { color: var(--ink-quiet); white-space: pre-wrap; font-family: Consolas, monospace; font-size: 0.9em; }
            .failure { padding: 1rem; margin-bottom: 1rem; background: var(--fail-wash); border-radius: 6px; border: 1px solid var(--rule); }
            .failure pre.assertion { white-space: pre-wrap; font-family: Consolas, monospace; color: var(--ink); }
            .failure img.screenshot { max-width: 100%; border: 1px solid var(--rule); margin-top: 0.5rem; }
            details.tree-dump pre { max-height: 24rem; overflow: auto; background: #0f1114; color: #cbd2dc; padding: 0.75rem; font-family: Consolas, monospace; font-size: 0.8em; }
            .muted { color: var(--ink-quiet); font-style: italic; }
            @media (prefers-color-scheme: light) {
                :root {
                    --page: #ffffff;
                    --panel: #f6f6f6;
                    --ink: #202020;
                    --ink-quiet: #5a6270;
                    --rule: #dddddd;
                    --pass: #1b7a3d;
                    --fail: #b0261e;
                    --skip: #b08d1e;
                    --fail-wash: #fdf1f0;
                }
            }
            """;

        public static string Write(RunOutcome outcome, RunEnvironment environment, string artifactFolder)
        {
            string path = string.Empty;

            try
            {
                Directory.CreateDirectory(artifactFolder);

                string fullPath = Path.Combine(artifactFolder, ReportFileName);
                string html = BuildDocument(outcome, environment);

                File.WriteAllText(fullPath, html);

                path = fullPath;
            }
            catch (Exception failure)
            {
                Console.WriteLine($"Could not write the HTML report: {failure.Message}");
            }

            return path;
        }

        private static string BuildDocument(RunOutcome outcome, RunEnvironment environment)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
            builder.Append("<title>UI Test Report</title>\n");
            builder.Append($"<style>{CssStyle}</style>\n</head>\n<body>\n");
            builder.Append(BuildVerdictSection(outcome));
            builder.Append(BuildTimelineSection(outcome));
            builder.Append(BuildFailuresSection(outcome));
            builder.Append(BuildNotCoveredSection());
            builder.Append(BuildEnvironmentSection(environment));
            builder.Append("</body>\n</html>\n");

            string document = builder.ToString();

            return document;
        }

        private static string BuildVerdictSection(RunOutcome outcome)
        {
            string verdict = DetermineVerdict(outcome);
            string verdictClass = verdict.ToLowerInvariant();
            StringBuilder builder = new StringBuilder();

            builder.Append("<section id=\"verdict\">\n");
            builder.Append($"<h1 class=\"{verdictClass}\">{verdict}</h1>\n<ul class=\"counts\">\n");
            builder.Append($"<li>Passed: {outcome.PassedCount}</li>\n");
            builder.Append($"<li>Failed: {outcome.FailedCount}</li>\n");
            builder.Append($"<li>Skipped: {outcome.SkippedCount}</li>\n");
            builder.Append($"<li>Total wall-clock: {FormatDuration(outcome.TotalDuration)}</li>\n");
            builder.Append("</ul>\n</section>\n");

            string section = builder.ToString();

            return section;
        }

        private static string BuildTimelineSection(RunOutcome outcome)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("<section id=\"timeline\">\n<h2>Phase timeline</h2>\n");

            foreach (PhaseResult phase in outcome.Phases)
            {
                string abortedBadge = phase.Aborted
                    ? "<span class=\"badge aborted\">Aborted</span>"
                    : string.Empty;

                builder.Append("<div class=\"phase\">\n");
                builder.Append($"<h3>{HtmlEncode(phase.Name)} <span class=\"duration\">{FormatDuration(phase.Duration)}</span> {abortedBadge}</h3>\n");
                builder.Append("<ul class=\"steps\">\n");

                foreach (StepResult step in phase.Steps)
                {
                    builder.Append(BuildStepFragment(step));
                }

                builder.Append("</ul>\n</div>\n");
            }

            builder.Append("</section>\n");

            string section = builder.ToString();

            return section;
        }

        private static string BuildStepFragment(StepResult step)
        {
            string cssClass = StepCssClass(step.Outcome);
            string message = step.Message.Length > 0
                ? $"<div class=\"message\">{HtmlEncode(step.Message)}</div>"
                : string.Empty;

            string fragment = $"<li class=\"{cssClass}\"><span class=\"outcome\">{step.Outcome}</span> {HtmlEncode(step.Name)}{message}</li>\n";

            return fragment;
        }

        private static string BuildFailuresSection(RunOutcome outcome)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("<section id=\"failures\">\n<h2>Each failure</h2>\n");

            IEnumerable<StepResult> failures = outcome.Phases
                .SelectMany(phase => phase.Steps)
                .Where(step => step.Outcome == StepOutcome.Failed);

            bool anyFailures = false;

            foreach (StepResult failure in failures)
            {
                anyFailures = true;

                builder.Append("<div class=\"failure\">\n");
                builder.Append($"<h3>{HtmlEncode(failure.Name)}</h3>\n");
                builder.Append($"<pre class=\"assertion\">{HtmlEncode(failure.Message)}</pre>\n");
                builder.Append(BuildScreenshotFragment(failure.ScreenshotPath));
                builder.Append(BuildTreeDumpFragment(failure.TreeDumpPath));
                builder.Append("</div>\n");
            }

            if (!anyFailures)
            {
                builder.Append("<p class=\"muted\">No failures.</p>\n");
            }

            builder.Append("</section>\n");

            string section = builder.ToString();

            return section;
        }

        private static string BuildScreenshotFragment(string screenshotPath)
        {
            string fragment = "<p class=\"muted\">No screenshot captured.</p>\n";

            try
            {

                if (screenshotPath.Length > 0 && File.Exists(screenshotPath))
                {
                    byte[] imageBytes = File.ReadAllBytes(screenshotPath);
                    string base64Image = Convert.ToBase64String(imageBytes);

                    fragment = $"<img class=\"screenshot\" src=\"data:image/png;base64,{base64Image}\" alt=\"Screenshot at failure\">\n";
                }

            }
            catch (Exception)
            {
                fragment = "<p class=\"muted\">Screenshot could not be embedded.</p>\n";
            }

            return fragment;
        }

        private static string BuildTreeDumpFragment(string treeDumpPath)
        {
            string fragment = "<p class=\"muted\">No tree dump captured.</p>\n";

            try
            {

                if (treeDumpPath.Length > 0 && File.Exists(treeDumpPath))
                {
                    string treeDumpText = File.ReadAllText(treeDumpPath);

                    fragment = $"<details class=\"tree-dump\"><summary>Automation tree</summary><pre>{HtmlEncode(treeDumpText)}</pre></details>\n";
                }

            }
            catch (Exception)
            {
                fragment = "<p class=\"muted\">Tree dump could not be embedded.</p>\n";
            }

            return fragment;
        }

        private static string BuildNotCoveredSection()
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("<section id=\"not-covered\">\n<h2>Not covered by this run</h2>\n");
            builder.Append("<p class=\"muted\">Populated by a later task in the plan — this suite cannot yet state its own boundaries.</p>\n");
            builder.Append("</section>\n");

            string section = builder.ToString();

            return section;
        }

        private static string BuildEnvironmentSection(RunEnvironment environment)
        {
            string elevatedLabel = environment.IsElevated ? "Yes" : "No";
            StringBuilder builder = new StringBuilder();

            builder.Append("<section id=\"environment\">\n<h2>Environment</h2>\n<ul>\n");
            builder.Append($"<li>App version before: {HtmlEncode(FormatOrUnknown(environment.AppVersionBefore))}</li>\n");
            builder.Append($"<li>App version after: {HtmlEncode(FormatOrUnknown(environment.AppVersionAfter))}</li>\n");
            builder.Append($"<li>OS build: {HtmlEncode(environment.OsBuild)}</li>\n");
            builder.Append($"<li>Primary monitor DPI scale: {FormatDpiScale(environment.PrimaryMonitorDpiScale)}</li>\n");
            builder.Append($"<li>Theme: {HtmlEncode(environment.Theme)}</li>\n");
            builder.Append($"<li>Chart colour scheme: {HtmlEncode(environment.ChartColourScheme)}</li>\n");
            builder.Append($"<li>Elevated: {elevatedLabel}</li>\n");
            builder.Append("</ul>\n</section>\n");

            string section = builder.ToString();

            return section;
        }

        private static string DetermineVerdict(RunOutcome outcome)
        {
            string verdict;

            if (outcome.Phases.Any(phase => phase.Aborted))
            {
                verdict = "Aborted";
            }
            else if (outcome.FailedCount > 0)
            {
                verdict = "Failed";
            }
            else
            {
                verdict = "Passed";
            }

            return verdict;
        }

        private static string StepCssClass(StepOutcome outcome)
        {
            string cssClass = outcome switch
            {
                StepOutcome.Passed => "passed",
                StepOutcome.Failed => "failed",
                StepOutcome.Skipped => "skipped",
                _ => "unknown"
            };

            return cssClass;
        }

        private static string FormatDuration(TimeSpan duration)
        {
            string formatted = $"{duration.TotalSeconds:0.0}s";

            return formatted;
        }

        private static string FormatOrUnknown(string value)
        {
            string formatted = value.Length > 0 ? value : "(unknown)";

            return formatted;
        }

        private static string FormatDpiScale(double scale)
        {
            string formatted = $"{scale * 100:0}%";

            return formatted;
        }

        private static string HtmlEncode(string value)
        {
            string encoded = WebUtility.HtmlEncode(value);

            return encoded;
        }
    }
}
