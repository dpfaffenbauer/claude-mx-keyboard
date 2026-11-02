namespace Loupedeck.ClaudeStatusPlugin
{
    using System;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    // Sessions started in the Code tab of the Claude desktop app. The app keeps one record
    // per session in
    //   ~/Library/Application Support/Claude/claude-code-sessions/<account>/<org>/local_<id>.json
    // with "sessionId": "local_<id>" (the app's id), "cliSessionId" (the Claude Code session
    // id our hook sees) and "isArchived". claude://code/continue?session=local_<id> opens it.
    public static class ClaudeDesktop
    {
        public const String BundleId = "com.anthropic.claudefordesktop";

        // the format the app's URL handler accepts for ?session=
        private static readonly Regex LocalSessionId = new("^local_[A-Za-z0-9-]{1,64}$", RegexOptions.CultureInvariant);

        private static readonly ConcurrentDictionary<String, Record> Records = new();
        private static readonly ConcurrentDictionary<String, DateTime> NextLookup = new();

        private static String SessionsDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "Claude", "claude-code-sessions");

        public static Boolean TryGetSessionUrl(String cliSessionId, out String url)
        {
            var record = GetRecord(cliSessionId);
            url = record == null ? null : $"claude://code/continue?session={record.LocalId}";
            if (record == null)
            {
                PluginLog.Info($"No Claude desktop session found for {cliSessionId}");
            }

            return record != null;
        }

        // Archiving a session in the app is how it is "closed" - it then leaves the keypad.
        public static Boolean IsArchived(String cliSessionId) => GetRecord(cliSessionId)?.Archived == true;

        private static Record GetRecord(String cliSessionId)
        {
            if (String.IsNullOrEmpty(cliSessionId))
            {
                return null;
            }

            if (Records.TryGetValue(cliSessionId, out var cached))
            {
                try
                {
                    var modified = File.GetLastWriteTimeUtc(cached.Path);
                    if (File.Exists(cached.Path) && modified == cached.Modified)
                    {
                        return cached;
                    }

                    var fresh = Parse(cached.Path, cliSessionId);
                    if (fresh != null)
                    {
                        Records[cliSessionId] = fresh;
                        return fresh;
                    }
                }
                catch (IOException)
                {
                }

                Records.TryRemove(cliSessionId, out _);
            }

            // a record is written shortly after the session starts - don't rescan every second
            if (NextLookup.TryGetValue(cliSessionId, out var next) && DateTime.UtcNow < next)
            {
                return null;
            }

            var found = Find(cliSessionId);
            if (found == null)
            {
                NextLookup[cliSessionId] = DateTime.UtcNow.AddSeconds(10);
                return null;
            }

            NextLookup.TryRemove(cliSessionId, out _);
            Records[cliSessionId] = found;
            return found;
        }

        private static Record Find(String cliSessionId)
        {
            if (!Directory.Exists(SessionsDirectory))
            {
                return null;
            }

            foreach (var file in Directory.EnumerateFiles(SessionsDirectory, "local_*.json", SearchOption.AllDirectories))
            {
                var record = Parse(file, cliSessionId);
                if (record != null)
                {
                    return record;
                }
            }

            return null;
        }

        private static Record Parse(String file, String cliSessionId)
        {
            try
            {
                var modified = File.GetLastWriteTimeUtc(file);
                var text = File.ReadAllText(file);
                if (!text.Contains(cliSessionId, StringComparison.Ordinal))
                {
                    return null;
                }

                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.TryGetProperty("cliSessionId", out var cli) && cli.ValueKind == JsonValueKind.String
                    && cli.GetString() == cliSessionId
                    && root.TryGetProperty("sessionId", out var local) && local.ValueKind == JsonValueKind.String
                    && LocalSessionId.IsMatch(local.GetString()))
                {
                    var archived = root.TryGetProperty("isArchived", out var a) && a.ValueKind == JsonValueKind.True;
                    return new Record(file, modified, local.GetString(), archived);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // record being rewritten or unreadable - skip it
            }

            return null;
        }

        private sealed record Record(String Path, DateTime Modified, String LocalId, Boolean Archived);
    }
}
