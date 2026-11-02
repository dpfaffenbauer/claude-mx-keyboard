namespace Loupedeck.ClaudeStatusPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading.Tasks;

    // Brings the terminal tab of a Claude session to the front.
    public static class TerminalFocus
    {
        // argv[1] = iTerm2 session unique id (the part after ':' in $ITERM_SESSION_ID)
        private const String ItermScript = """
            on run argv
                set targetId to item 1 of argv
                tell application id "com.googlecode.iterm2"
                    repeat with w in windows
                        repeat with t in tabs of w
                            repeat with s in sessions of t
                                if unique id of s is targetId then
                                    select w
                                    select t
                                    select s
                                    activate
                                    return "ok"
                                end if
                            end repeat
                        end repeat
                    end repeat
                end tell
                return "notfound"
            end run
            """;

        // argv[1] = /dev/ttysNNN
        private const String TerminalScript = """
            on run argv
                set targetTty to item 1 of argv
                tell application id "com.apple.Terminal"
                    repeat with w in windows
                        repeat with t in tabs of w
                            if tty of t is targetTty then
                                set selected of t to true
                                set index of w to 1
                                activate
                                return "ok"
                            end if
                        end repeat
                    end repeat
                end tell
                return "notfound"
            end run
            """;

        // Any other host app (PhpStorm & other JetBrains IDEs, VS Code, ...): raise the window
        // whose title names the session's folder, then bring the app to the front.
        // argv[1] = bundle id, argv[2..] = folder names, most specific first.
        // JetBrains titles start with the project ("my-app – main.ts"),
        // VS Code titles end with it ("main.ts — my-app").
        private const String RaiseWindowScript = """
            on run argv
                set bundleId to item 1 of argv
                set candidates to rest of argv
                tell application "System Events"
                    set procs to (application processes whose bundle identifier is bundleId)
                    if procs is {} then
                        tell application id bundleId to activate
                        return "launched"
                    end if
                    set proc to item 1 of procs
                    repeat with candidate in candidates
                        set c to candidate as text
                        repeat with w in windows of proc
                            set t to name of w
                            if t is c or t starts with (c & " ") or t ends with (" " & c) or t contains (" " & c & " ") then
                                perform action "AXRaise" of w
                                set frontmost of proc to true
                                return "ok"
                            end if
                        end repeat
                    end repeat
                    set frontmost of proc to true
                end tell
                return "activated"
            end run
            """;

        // $TERM_PROGRAM -> bundle id, for terminals without a scriptable tab model
        private static readonly Dictionary<String, String> AppBundles = new(StringComparer.OrdinalIgnoreCase)
        {
            ["vscode"] = "com.microsoft.VSCode",
            ["ghostty"] = "com.mitchellh.ghostty",
            ["WezTerm"] = "com.github.wez.wezterm",
            ["WarpTerminal"] = "dev.warp.Warp-Stable",
            ["iTerm.app"] = "com.googlecode.iterm2",
            ["Apple_Terminal"] = "com.apple.Terminal",
        };

        public static void Focus(ClaudeSession session) => Task.Run(() =>
        {
            try
            {
                if (!String.IsNullOrEmpty(session.Iterm)
                    && RunOsaScript(ItermScript, session.Iterm) == "ok")
                {
                    return;
                }

                if (session.TermProgram == "Apple_Terminal" && session.Tty is { Length: > 1 }
                    && RunOsaScript(TerminalScript, "/dev/" + session.Tty) == "ok")
                {
                    return;
                }

                var bundle = HostAppBundle(session);
                if (String.IsNullOrEmpty(bundle))
                {
                    PluginLog.Warning($"No terminal/app known for session {session.SessionId}");
                    return;
                }

                var arguments = new List<String> { bundle };
                arguments.AddRange(FolderCandidates(session.Cwd));
                var result = RunOsaScript(RaiseWindowScript, arguments.ToArray());
                if (result != "ok")
                {
                    PluginLog.Info($"Focus {bundle} for {session.DisplayName}: {result ?? "failed"}");
                    if (result == null)
                    {
                        Run("/usr/bin/open", "-b", bundle);
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, $"Could not focus session {session.SessionId}");
            }
        });

        // Bundle id of the app the session's terminal lives in: recorded by the hook
        // ($__CFBundleIdentifier, inherited from the app), else read from the claude
        // process environment, else guessed from $TERM_PROGRAM.
        private static String HostAppBundle(ClaudeSession session)
        {
            if (!String.IsNullOrEmpty(session.App))
            {
                return session.App;
            }

            if (session.Pid > 0)
            {
                var environment = Run("/bin/ps", "eww", "-o", "command=", "-p", session.Pid.ToString()) ?? "";
                foreach (var token in environment.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (token.StartsWith("__CFBundleIdentifier=", StringComparison.Ordinal))
                    {
                        return token["__CFBundleIdentifier=".Length..];
                    }
                }
            }

            return AppBundles.TryGetValue(session.TermProgram ?? "", out var bundle) ? bundle : null;
        }

        // "/Users/me/code/acme/my-app" -> my-app, acme, code (stops at the home folder)
        private static IEnumerable<String> FolderCandidates(String cwd)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('/');
            var path = (cwd ?? "").TrimEnd('/');
            while (path.Length > home.Length && path.StartsWith(home + "/", StringComparison.Ordinal))
            {
                yield return System.IO.Path.GetFileName(path);
                path = System.IO.Path.GetDirectoryName(path) ?? "";
            }
        }

        private static String RunOsaScript(String script, params String[] arguments)
        {
            var args = new List<String>();
            foreach (var line in script.Split('\n'))
            {
                args.Add("-e");
                args.Add(line);
            }

            args.AddRange(arguments);
            return Run("/usr/bin/osascript", args.ToArray());
        }

        private static String Run(String fileName, params String[] arguments)
        {
            var info = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info);
            if (process == null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                process.Kill();
                PluginLog.Warning($"{fileName} timed out");
                return null;
            }

            if (process.ExitCode != 0)
            {
                PluginLog.Warning($"{fileName} exited {process.ExitCode}: {error.Result.Trim()}");
            }

            return output.Result.Trim();
        }
    }
}
