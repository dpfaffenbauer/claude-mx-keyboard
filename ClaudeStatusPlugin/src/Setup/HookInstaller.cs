namespace Loupedeck.ClaudeStatusPlugin
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text.Encodings.Web;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    public enum HookSetupState
    {
        ClaudeCodeMissing,  // no ~/.claude: Claude Code was never run on this Mac
        NotInstalled,
        Incomplete,         // script or some events missing
        Installed,
        SettingsUnreadable, // settings.json exists but is not valid JSON - never touched
    }

    // Installs the status hook into Claude Code:
    //   ~/.claude/hooks/claude-mx-keypad.sh        (embedded in the plugin)
    //   ~/.claude/settings.json  "hooks": { <event>: [ { "hooks": [ { command } ] } ] }
    // Only entries whose command is our script are ever added or removed; everything
    // else in settings.json is left as it is. A backup is written before the first change.
    public static class HookInstaller
    {
        public const String ScriptResource = "claude-mx-keypad.sh";

        public static readonly String[] Events =
        {
            "SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "PermissionRequest",
            "Notification", "Stop", "StopFailure", "PreCompact", "SessionEnd",
        };

        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private static readonly JsonDocumentOptions ReadOptions = new()
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        };

        public static String ClaudeDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

        public static String ScriptPath => Path.Combine(ClaudeDirectory, "hooks", ScriptResource);

        public static String SettingsPath => Path.Combine(ClaudeDirectory, "settings.json");

        private static String Command => ScriptPath.Contains(' ') ? $"\"{ScriptPath}\"" : ScriptPath;

        public static HookSetupState GetState()
        {
            if (!Directory.Exists(ClaudeDirectory))
            {
                return HookSetupState.ClaudeCodeMissing;
            }

            if (!TryReadSettings(out var settings))
            {
                return HookSetupState.SettingsUnreadable;
            }

            var registered = Events.Count(e => FindOurEntries(settings, e) > 0);
            if (registered == 0)
            {
                return HookSetupState.NotInstalled;
            }

            return registered == Events.Length && File.Exists(ScriptPath)
                ? HookSetupState.Installed
                : HookSetupState.Incomplete;
        }

        // Writes the script and registers every missing event. Idempotent.
        public static Boolean Install(Assembly assembly)
        {
            if (!Directory.Exists(ClaudeDirectory))
            {
                PluginLog.Warning("Claude Code not found (~/.claude missing), hooks not installed");
                return false;
            }

            WriteScript(assembly);

            if (!TryReadSettings(out var settings))
            {
                PluginLog.Error($"{SettingsPath} is not valid JSON, hooks not registered");
                return false;
            }

            var hooks = settings["hooks"] as JsonObject;
            if (hooks == null)
            {
                hooks = new JsonObject();
                settings["hooks"] = hooks;
            }

            var added = 0;
            foreach (var hookEvent in Events.Where(e => FindOurEntries(settings, e) == 0))
            {
                if (hooks[hookEvent] is not JsonArray groups)
                {
                    groups = new JsonArray();
                    hooks[hookEvent] = groups;
                }

                groups.Add(new JsonObject
                {
                    ["hooks"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "command",
                            ["command"] = Command,
                            ["timeout"] = 5,
                        },
                    },
                });
                added++;
            }

            if (added > 0)
            {
                WriteSettings(settings);
                PluginLog.Info($"Registered Claude Code hook for {added} events in {SettingsPath}");
            }

            return true;
        }

        // Keeps an installed script up to date with the plugin version (no settings change).
        public static void RefreshScript(Assembly assembly)
        {
            if (File.Exists(ScriptPath))
            {
                WriteScript(assembly);
            }
        }

        public static void Uninstall()
        {
            try
            {
                if (TryReadSettings(out var settings) && settings["hooks"] is JsonObject hooks)
                {
                    var removed = 0;
                    foreach (var hookEvent in hooks.Select(p => p.Key).ToList())
                    {
                        if (hooks[hookEvent] is not JsonArray groups)
                        {
                            continue;
                        }

                        foreach (var group in groups.OfType<JsonObject>().ToList())
                        {
                            if (group["hooks"] is not JsonArray entries)
                            {
                                continue;
                            }

                            foreach (var entry in entries.Where(IsOurEntry).ToList())
                            {
                                entries.Remove(entry);
                                removed++;
                            }

                            if (entries.Count == 0)
                            {
                                groups.Remove(group);
                            }
                        }

                        if (groups.Count == 0)
                        {
                            hooks.Remove(hookEvent);
                        }
                    }

                    if (removed > 0)
                    {
                        WriteSettings(settings);
                        PluginLog.Info($"Removed {removed} Claude Code hook entries");
                    }
                }

                File.Delete(ScriptPath);
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Could not remove Claude Code hooks");
            }
        }

        private static void WriteScript(Assembly assembly)
        {
            using var stream = assembly.GetManifestResourceStream(ScriptResource)
                ?? throw new InvalidOperationException($"Embedded resource {ScriptResource} missing");
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();

            Directory.CreateDirectory(Path.GetDirectoryName(ScriptPath));
            if (!File.Exists(ScriptPath) || File.ReadAllText(ScriptPath) != content)
            {
                var tmp = ScriptPath + ".tmp";
                File.WriteAllText(tmp, content);
                if (!OperatingSystem.IsWindows())
                {
                    const UnixFileMode Executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
                    File.SetUnixFileMode(tmp, Executable);
                }

                File.Move(tmp, ScriptPath, overwrite: true);
                PluginLog.Info($"Wrote {ScriptPath}");
            }
        }

        private static Int32 FindOurEntries(JsonObject settings, String hookEvent) =>
            (settings["hooks"]?[hookEvent] as JsonArray)?
                .OfType<JsonObject>()
                .Sum(group => (group["hooks"] as JsonArray)?.Count(IsOurEntry) ?? 0) ?? 0;

        private static Boolean IsOurEntry(JsonNode entry)
        {
            if (entry?["command"] is not JsonValue value || !value.TryGetValue<String>(out var command))
            {
                return false;
            }

            return command.Trim().Trim('"', '\'') == ScriptPath;
        }

        private static Boolean TryReadSettings(out JsonObject settings)
        {
            settings = null;
            if (!File.Exists(SettingsPath))
            {
                settings = new JsonObject();
                return true;
            }

            try
            {
                var text = File.ReadAllText(SettingsPath);
                settings = String.IsNullOrWhiteSpace(text)
                    ? new JsonObject()
                    : JsonNode.Parse(text, documentOptions: ReadOptions) as JsonObject;
                return settings != null;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                PluginLog.Warning(ex, $"Cannot read {SettingsPath}");
                return false;
            }
        }

        private static void WriteSettings(JsonObject settings)
        {
            var backup = SettingsPath + ".bak-claude-mx-keypad";
            if (File.Exists(SettingsPath) && !File.Exists(backup))
            {
                File.Copy(SettingsPath, backup);
            }

            var tmp = SettingsPath + ".tmp-claude-mx-keypad";
            File.WriteAllText(tmp, settings.ToJsonString(WriteOptions) + "\n");
            File.Move(tmp, SettingsPath, overwrite: true);
        }
    }
}
