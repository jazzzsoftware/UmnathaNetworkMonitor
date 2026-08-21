using System.Windows.Forms;
using NetworkMonitor.UITests.Runner;

namespace NetworkMonitor.UITests.Launcher
{
    // A small dialog for choosing which phases to run, shown by `--pick`. Everything it offers is
    // already available as command-line behaviour; this exists because ticking boxes beats
    // remembering flags when you are iterating on one phase and do not want to sit through the
    // rest.
    //
    // Every phase starts ticked and the run starts by itself after thirty seconds, so the dialog
    // costs nothing when you meant to run everything — it is a chance to deselect, not a gate.
    // Touching anything (ticking a box, or clicking in the list) stops the countdown and waits for
    // you, on the assumption that someone who has started choosing wants to finish choosing.
    //
    // Deliberately not the default: a run with no arguments stays non-interactive, so anything
    // scripted keeps working and nothing ever waits on a dialog nobody is watching.
    //
    // Phase 01 is always run and cannot be unticked — it is the phase that launches the app, and
    // every other phase asserts against the session it establishes. The list says so rather than
    // silently re-adding it.
    public static class PhasePicker
    {
        private const string DestructivePhaseName = "09 Update Lifecycle";
        private const int AutoStartSeconds = 30;

        // Not resizable, and sized to fit the longest phase name plus its note.
        private static readonly System.Drawing.Size DialogSize = new System.Drawing.Size(560, 470);

        public static IReadOnlyList<Phase>? Choose(IReadOnlyList<Phase> phases)
        {
            IReadOnlyList<Phase>? chosen = null;

            ApplicationConfiguration.Initialize();

            using (Form dialog = new Form())
            {
                dialog.Text = "Umnatha UI tests — choose phases";
                dialog.ClientSize = DialogSize;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterScreen;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.TopMost = true;

                Label heading = new Label
                {
                    Text = "Everything is selected. Untick anything you want to skip — 01 Launch always runs, because it starts the app the others drive.",
                    Left = 12,
                    Top = 12,
                    Width = DialogSize.Width - 24,
                    Height = 34
                };

                CheckedListBox phaseList = new CheckedListBox
                {
                    Left = 12,
                    Top = 52,
                    Width = DialogSize.Width - 24,
                    Height = DialogSize.Height - 160,
                    CheckOnClick = true,
                    IntegralHeight = false
                };

                foreach (Phase phase in phases)
                {
                    bool isLaunch = ReferenceEquals(phase, phases[0]);
                    bool isDestructive = string.Equals(phase.Name, DestructivePhaseName, StringComparison.Ordinal);
                    string label = BuildLabel(phase, isLaunch, isDestructive);

                    phaseList.Items.Add(label, true);
                }

                Label countdown = new Label
                {
                    Left = 12,
                    Top = phaseList.Bottom + 8,
                    Width = DialogSize.Width - 220,
                    Height = 46
                };

                Button runButton = new Button
                {
                    Text = "Run",
                    DialogResult = DialogResult.OK,
                    Left = DialogSize.Width - 190,
                    Top = DialogSize.Height - 40,
                    Width = 85
                };

                Button cancelButton = new Button
                {
                    Text = "Cancel",
                    DialogResult = DialogResult.Cancel,
                    Left = DialogSize.Width - 97,
                    Top = DialogSize.Height - 40,
                    Width = 85
                };

                dialog.Controls.Add(heading);
                dialog.Controls.Add(phaseList);
                dialog.Controls.Add(countdown);
                dialog.Controls.Add(runButton);
                dialog.Controls.Add(cancelButton);
                dialog.AcceptButton = runButton;
                dialog.CancelButton = cancelButton;

                AttachAutoStart(dialog, phaseList, countdown, phases);

                DialogResult result = dialog.ShowDialog();

                if (result == DialogResult.OK)
                {
                    chosen = BuildSelection(phases, phaseList);
                }

            }

            return chosen;
        }

        // The countdown and the rule that stops it. ItemCheck fires before the tick is applied, so
        // the timer is stopped on the intent rather than on the resulting state — the point is that
        // someone is interacting, not what they chose.
        private static void AttachAutoStart(Form dialog, CheckedListBox phaseList, Label countdown, IReadOnlyList<Phase> phases)
        {
            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 1000 };
            int secondsLeft = AutoStartSeconds;
            bool includesDestructive = phases.Any(phase => string.Equals(phase.Name, DestructivePhaseName, StringComparison.Ordinal));

            countdown.Text = BuildCountdownText(secondsLeft, includesDestructive);

            timer.Tick += (sender, args) =>
            {
                secondsLeft--;

                if (secondsLeft <= 0)
                {
                    timer.Stop();

                    dialog.DialogResult = DialogResult.OK;

                    dialog.Close();
                }
                else
                {
                    countdown.Text = BuildCountdownText(secondsLeft, includesDestructive);
                }

            };

            void StopCountdown(object? sender, EventArgs args)
            {
                timer.Stop();

                countdown.Text = "Waiting for you — press Run when ready.";
            }

            phaseList.ItemCheck += (sender, args) => StopCountdown(sender, args);
            phaseList.Click += StopCountdown;
            phaseList.KeyDown += (sender, args) => StopCountdown(sender, args);

            dialog.Shown += (sender, args) => timer.Start();
            dialog.FormClosed += (sender, args) => timer.Dispose();
        }

        private static string BuildCountdownText(int secondsLeft, bool includesDestructive)
        {
            string warning = includesDestructive
                ? $" This includes {DestructivePhaseName}, which uninstalls and reinstalls the app."
                : string.Empty;

            string text = $"Starting in {secondsLeft}s.{warning}";

            return text;
        }

        private static string BuildLabel(Phase phase, bool isLaunch, bool isDestructive)
        {
            string suffix;

            if (isLaunch)
            {
                suffix = "  (always runs)";
            }
            else if (isDestructive)
            {
                suffix = "  (destructive — uninstalls and reinstalls the app)";
            }
            else
            {
                suffix = string.Empty;
            }

            string label = $"{phase.Name}{suffix}";

            return label;
        }

        // The launch phase is added back whether or not it was ticked, and the order of the
        // original list is preserved: phases assert against what earlier ones left behind, so
        // running them in a different order would not mean anything.
        private static IReadOnlyList<Phase> BuildSelection(IReadOnlyList<Phase> phases, CheckedListBox phaseList)
        {
            List<Phase> selected = new List<Phase>();

            for (int index = 0; index < phases.Count; index++)
            {
                bool isLaunch = index == 0;
                bool ticked = phaseList.GetItemChecked(index);

                if (isLaunch || ticked)
                {
                    selected.Add(phases[index]);
                }

            }

            IReadOnlyList<Phase> result = selected;

            return result;
        }
    }
}
