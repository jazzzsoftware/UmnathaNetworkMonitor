using System.Diagnostics;
using Microsoft.Win32;

namespace NetworkMonitor.UITests.Driving
{
    // The app opens what it just exported: every path through `ShellLauncher.Open` hands the file
    // to whatever program the operator has registered for that extension — Excel for a .csv, a PDF
    // reader for a .pdf. That program is an uncontrolled external process appearing on the
    // operator's desktop mid-run, and it steals focus from the app under test.
    //
    // This resolves the handler for an extension, refuses to run when one is already open (see
    // FindPreExistingHandlerBlocker for the operator's ruling on why), and closes only what the
    // export itself opened. Task 10 moved it out of DevicesPhase, where it was hardcoded to .csv,
    // because the Reports page exports both a PDF and a CSV the same way.
    public static class ShellFileHandler
    {
        // ShellLauncher.Open's Process.Start returns almost immediately, but the handler process
        // can take a moment longer to appear in the process table.
        private static readonly TimeSpan HandlerAppearTimeout = TimeSpan.FromSeconds(10);

        // A window caption appears within a second or two of the handler starting; five seconds is
        // headroom for a heavyweight one, after which the process is left alone.
        private static readonly TimeSpan WindowTitleTimeout = TimeSpan.FromSeconds(5);

        // A handler asked to close politely either goes within a few seconds or is not going to.
        private static readonly TimeSpan HandlerCloseTimeout = TimeSpan.FromSeconds(5);

        // Resolves the real, per-user default handler rather than assuming a program by name:
        // Windows records the user's actual choice under FileExts\<ext>\UserChoice (falling back
        // to the class-registered default), and the handler's own registered open command names
        // the executable. Best-effort — an empty result disables both the precondition check and
        // the post-export close, which is the safe default: never guess a process name to close.
        public static string ResolveHandlerProcessName(string extension)
        {
            string processName = string.Empty;

            try
            {
                string progId = ReadProgId(extension);

                if (progId.Length > 0)
                {
                    string commandLine = ReadShellOpenCommand(progId);
                    string executablePath = ExtractExecutablePath(commandLine);

                    if (executablePath.Length > 0)
                    {
                        processName = Path.GetFileNameWithoutExtension(executablePath);
                    }

                }

            }
            catch (Exception exception)
            {
                Console.WriteLine($"ShellFileHandler: could not resolve the {extension} file association: {exception.Message}");
            }

            return processName;
        }

        // Operator's ruling (2026-08-20): closing only what an export opened is safe only if
        // nothing of that kind was already running before the click — Excel (and handlers like it)
        // can open a file as another window inside an existing process rather than starting a new
        // one, which would make "close only mine" impossible to promise honestly after the fact.
        // So this runs before Export is clicked, and a match is a precondition failure rather than
        // something to click through and guess about.
        public static string FindPreExistingHandlerBlocker(string handlerProcessName, string extension)
        {
            string blocker = string.Empty;

            if (handlerProcessName.Length > 0)
            {
                Process[] matchingProcesses = Process.GetProcessesByName(handlerProcessName);

                if (matchingProcesses.Length > 0)
                {
                    int[] processIds = new int[matchingProcesses.Length];

                    for (int index = 0; index < matchingProcesses.Length; index++)
                    {
                        processIds[index] = matchingProcesses[index].Id;
                        matchingProcesses[index].Dispose();
                    }

                    blocker =
                        $"A '{handlerProcessName}' process (the {extension} file handler) is already running "
                        + $"(pid(s) {string.Join(", ", processIds)}) before Export was even clicked. This step "
                        + "cannot promise it will close only what its own export opens if that handler might "
                        + "reuse this existing process instead of starting a new one — close it by hand first.";
                }

            }

            return blocker;
        }

        // The precondition check above guarantees this process name was not running before the
        // export click, so any instance found now was started by ShellLauncher.Open — closed by
        // name and window title together as a final sanity check, never by name alone.
        public static void CloseOpenedFile(string handlerProcessName, string filePath)
        {

            if (handlerProcessName.Length > 0)
            {
                string fileName = Path.GetFileName(filePath);
                Process[] matchingProcesses = WaitForHandlerProcesses(handlerProcessName);

                foreach (Process candidate in matchingProcesses)
                {

                    try
                    {
                        CloseSingleHandlerProcess(candidate, fileName);
                    }
                    catch (Exception exception)
                    {
                        Console.WriteLine($"ShellFileHandler: could not close process {candidate.Id} ('{handlerProcessName}'): {exception.Message}");
                    }
                    finally
                    {
                        candidate.Dispose();
                    }

                }

            }

        }

        private static Process[] WaitForHandlerProcesses(string handlerProcessName)
        {
            Process[] found = Array.Empty<Process>();

            try
            {
                Waits.Until(
                    () =>
                    {
                        found = Process.GetProcessesByName(handlerProcessName);

                        return found.Length > 0;
                    },
                    HandlerAppearTimeout,
                    $"a '{handlerProcessName}' process to appear after ShellLauncher.Open");
            }
            catch (TimeoutException)
            {
                // Nothing appeared -- nothing to close. Best-effort cleanup, not a requirement:
                // some environments may not launch a visible handler for every file type.
            }

            return found;
        }

        private static void CloseSingleHandlerProcess(Process candidate, string fileName)
        {
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            string windowTitle = WaitForWindowTitle(candidate, fileName, fileNameWithoutExtension);

            // Matched against the name with AND without its extension: PDF-XChange titles its
            // window "digest-export - PDF-XChange Editor", dropping the ".pdf" that Excel keeps
            // ("approved-devices-export.csv - Excel"). Checking only the full file name left a PDF
            // viewer open on the operator's desktop after a real run, holding focus into the next
            // phase, because the two-second window in which the title said nothing at all had also
            // been read as "not ours".
            bool titleNamesOurFile = windowTitle.Length > 0
                && (windowTitle.Contains(fileName, StringComparison.OrdinalIgnoreCase)
                    || windowTitle.Contains(fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase));

            if (titleNamesOurFile)
            {
                Console.WriteLine(
                    $"ShellFileHandler: closing '{candidate.ProcessName}' (pid {candidate.Id}, titled '{windowTitle}'), "
                    + $"opened by ShellLauncher.Open on '{fileName}'.");

                candidate.CloseMainWindow();

                bool exited = WaitForProcessExit(candidate, HandlerCloseTimeout);

                if (!exited)
                {
                    candidate.Kill();
                }

            }
            else
            {
                Console.WriteLine(
                    $"ShellFileHandler: found a '{candidate.ProcessName}' process (pid {candidate.Id}) after export, but its "
                    + $"window title ('{windowTitle}') does not name '{fileName}' — left alone rather than "
                    + "guessing it is the one this step opened.");
            }

        }

        // Waits for the handler's window to actually name the file, rather than for it merely to
        // have a title. Both weaker readings were tried against real runs and both left a PDF
        // viewer open on the operator's desktop: read once, the title was empty ("" — the process
        // was still starting); waited only for non-empty, it was "PDF-XChange Editor", the
        // application's own startup caption, which arrives before the document is loaded. Whatever
        // the last title seen was is returned, so the caller's message quotes what it really saw.
        private static string WaitForWindowTitle(Process process, string fileName, string fileNameWithoutExtension)
        {
            string title = string.Empty;

            try
            {
                Waits.Until(
                    () =>
                    {
                        process.Refresh();
                        title = process.MainWindowTitle;

                        bool namesOurFile = title.Contains(fileName, StringComparison.OrdinalIgnoreCase)
                            || title.Contains(fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase);

                        return namesOurFile;
                    },
                    WindowTitleTimeout,
                    $"the '{process.ProcessName}' window to be titled with the exported file's name");
            }
            catch (TimeoutException)
            {
                // Left as whatever was last seen: the caller treats a window that never names the
                // file as "not identifiably ours" and leaves it alone, which stays the safe answer.
            }

            return title;
        }

        // Waits.cs claims every wait in this suite routes through it; Process.WaitForExit(int) was
        // one of three places across the suite that did not (fix round 3, 2026-08-20).
        private static bool WaitForProcessExit(Process process, TimeSpan timeout)
        {
            bool exited;

            try
            {
                Waits.Until(() => process.HasExited, timeout, "the process to exit");
                exited = true;
            }
            catch (TimeoutException)
            {
                exited = false;
            }

            return exited;
        }

        private static string ReadProgId(string extension)
        {
            string progId = string.Empty;

            using (RegistryKey? userChoiceKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\" + extension + @"\UserChoice"))
            {

                if (userChoiceKey is not null)
                {
                    progId = userChoiceKey.GetValue("ProgId") as string ?? string.Empty;
                }

            }

            if (progId.Length == 0)
            {

                using (RegistryKey? classesKey = Registry.ClassesRoot.OpenSubKey(extension))
                {

                    if (classesKey is not null)
                    {
                        progId = classesKey.GetValue(null) as string ?? string.Empty;
                    }

                }

            }

            return progId;
        }

        private static string ReadShellOpenCommand(string progId)
        {
            string command = string.Empty;

            using (RegistryKey? commandKey = Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command"))
            {

                if (commandKey is not null)
                {
                    command = commandKey.GetValue(null) as string ?? string.Empty;
                }

            }

            return command;
        }

        private static string ExtractExecutablePath(string commandLine)
        {
            string executablePath = string.Empty;

            if (commandLine.Length > 0)
            {

                if (commandLine.StartsWith("\"", StringComparison.Ordinal))
                {
                    int closingQuoteIndex = commandLine.IndexOf('"', 1);

                    executablePath = closingQuoteIndex > 0 ? commandLine.Substring(1, closingQuoteIndex - 1) : commandLine;
                }
                else
                {
                    int spaceIndex = commandLine.IndexOf(' ');

                    executablePath = spaceIndex > 0 ? commandLine.Substring(0, spaceIndex) : commandLine;
                }

            }

            return executablePath;
        }
    }
}
