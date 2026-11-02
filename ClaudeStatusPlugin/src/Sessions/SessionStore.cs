namespace Loupedeck.ClaudeStatusPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;

    // Reads the status files written by the Claude Code hook, keeps a stable
    // session -> key slot mapping and notifies the actions once per second
    // (immediately when a file changes).
    public sealed class SessionStore
    {
        public const Int32 SlotCount = 9;

        private const Int32 TickMs = 1000;
        private const Int32 LivenessEveryTicks = 5;
        private static readonly TimeSpan OrphanAge = TimeSpan.FromHours(12);

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly Object _gate = new();
        private readonly Object _refreshGate = new();
        private readonly String[] _slots = new String[SlotCount];
        private readonly HashSet<String> _acknowledged = new();
        private readonly HashSet<String> _dismissed = new();
        private readonly Dictionary<String, TranscriptWatcher> _titles = new();
        private readonly Dictionary<String, DateTime> _transcriptRetry = new();
        private Dictionary<String, ClaudeSession> _sessions = new();

        private FileSystemWatcher _watcher;
        private Timer _timer;
        private Int64 _ticks;

        public static SessionStore Instance { get; } = new SessionStore();

        public static String StatusDirectory
        {
            get
            {
                var custom = Environment.GetEnvironmentVariable("CLAUDE_KEYPAD_DIR");
                return String.IsNullOrEmpty(custom)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "keypad")
                    : custom;
            }
        }

        // Raised after every refresh (at least once per second). Handlers decide what to redraw.
        public event Action Updated;

        public void Start()
        {
            this.Stop();
            Directory.CreateDirectory(StatusDirectory);

            this._watcher = new FileSystemWatcher(StatusDirectory, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            };
            this._watcher.Created += this.OnFileEvent;
            this._watcher.Changed += this.OnFileEvent;
            this._watcher.Deleted += this.OnFileEvent;
            this._watcher.Renamed += this.OnFileEvent;
            this._watcher.EnableRaisingEvents = true;

            this._timer = new Timer(_ => this.Refresh(), null, 0, TickMs);
            PluginLog.Info($"Watching {StatusDirectory}");
        }

        public void Stop()
        {
            this._watcher?.Dispose();
            this._watcher = null;
            this._timer?.Dispose();
            this._timer = null;
        }

        public SessionView GetSlot(Int32 index)
        {
            lock (this._gate)
            {
                if (index < 0 || index >= SlotCount || this._slots[index] is not { } id
                    || !this._sessions.TryGetValue(id, out var session))
                {
                    return null;
                }

                return this.ToView(session);
            }
        }

        public IReadOnlyList<SessionView> GetAll()
        {
            lock (this._gate)
            {
                return this._sessions.Values.OrderBy(s => s.Started).Select(this.ToView).ToList();
            }
        }

        // Pressing a finished (or failed) session's key turns it back to "idle".
        public void Acknowledge(ClaudeSession session)
        {
            lock (this._gate)
            {
                this._acknowledged.Add(session.AckKey);
            }

            this.RequestRefresh();
        }

        // Takes a session off the keypad until it does something new (next prompt, approval, ...).
        // For sessions that cannot simply be closed, e.g. in the Claude desktop app.
        public void Dismiss(ClaudeSession session)
        {
            lock (this._gate)
            {
                this._dismissed.Add(session.DismissKey);
            }

            PluginLog.Info($"Dismissed {session.DisplayName} from the keypad");
            this.RequestRefresh();
        }

        private SessionView ToView(ClaudeSession session)
        {
            var state = SessionView.Map(session.State);
            if ((state == SessionDisplayState.Done || state == SessionDisplayState.Error)
                && this._acknowledged.Contains(session.AckKey))
            {
                state = SessionDisplayState.Idle;
            }

            return new SessionView(session, state);
        }

        private void OnFileEvent(Object sender, FileSystemEventArgs e) => this.RequestRefresh();

        // Pull the next refresh forward (coalesces bursts of file events).
        private void RequestRefresh()
        {
            try
            {
                this._timer?.Change(60, TickMs);
            }
            catch (ObjectDisposedException)
            {
                // plugin is unloading
            }
        }

        private void Refresh()
        {
            if (!Monitor.TryEnter(this._refreshGate))
            {
                return;
            }

            try
            {
                var loaded = LoadSessions();
                if (++this._ticks % LivenessEveryTicks == 1)
                {
                    RemoveDeadSessions(loaded);
                }

                this.UpdateTitles(loaded);

                // archiving a desktop-app session is how it gets closed
                var running = loaded.Keys.ToHashSet();
                foreach (var archived in loaded.Values
                    .Where(s => s.App == ClaudeDesktop.BundleId && ClaudeDesktop.IsArchived(s.SessionId))
                    .ToList())
                {
                    loaded.Remove(archived.SessionId);
                }

                lock (this._gate)
                {
                    foreach (var dismissed in loaded.Values.Where(s => this._dismissed.Contains(s.DismissKey)).ToList())
                    {
                        loaded.Remove(dismissed.SessionId);
                    }

                    this._sessions = loaded;
                    this.AssignSlots();
                    this._acknowledged.RemoveWhere(key => !loaded.ContainsKey(key.Split(':')[0]));
                    this._dismissed.RemoveWhere(key => !running.Contains(key.Split(':')[0]));
                }

                this.Updated?.Invoke();
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Session refresh failed");
            }
            finally
            {
                Monitor.Exit(this._refreshGate);
            }
        }

        // Session name = /rename title, else Claude Code's generated title (from the transcript).
        private void UpdateTitles(Dictionary<String, ClaudeSession> sessions)
        {
            foreach (var id in this._titles.Keys.Where(id => !sessions.ContainsKey(id)).ToList())
            {
                this._titles.Remove(id);
            }

            foreach (var session in sessions.Values)
            {
                if (!this._titles.TryGetValue(session.SessionId, out var tracker))
                {
                    if (this._transcriptRetry.TryGetValue(session.SessionId, out var retryAt) && DateTime.UtcNow < retryAt)
                    {
                        continue;
                    }

                    var path = ResolveTranscript(session);
                    if (path == null)
                    {
                        // a brand-new session has no transcript yet
                        this._transcriptRetry[session.SessionId] = DateTime.UtcNow.AddSeconds(10);
                        continue;
                    }

                    this._transcriptRetry.Remove(session.SessionId);

                    tracker = new TranscriptWatcher(path);
                    this._titles[session.SessionId] = tracker;
                }

                tracker.Update();
                session.Title = tracker.Title;
                ApplyTranscriptTurn(session, tracker);
            }
        }

        // The hook can miss the end of a turn: Esc fires no Stop hook, and status files
        // written before the out-of-turn fix may still say "working". If the hook says
        // "working" but the transcript shows the turn is over (and has been quiet for a
        // moment, and nothing happened since the hook's last change), trust the transcript.
        private static void ApplyTranscriptTurn(ClaudeSession session, TranscriptWatcher tracker)
        {
            if (session.State != "working" || tracker.Turn is TurnState.Open or TurnState.Unknown)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now - tracker.LastWrite < 3 || tracker.LastWrite < session.Since)
            {
                return;
            }

            session.State = tracker.Turn == TurnState.Ended ? "done" : "idle";
            session.Reason = "";
            session.Since = tracker.LastWrite;
        }

        private static String ResolveTranscript(ClaudeSession session)
        {
            if (!String.IsNullOrEmpty(session.Transcript) && File.Exists(session.Transcript))
            {
                return session.Transcript;
            }

            // older status files without "transcript": ~/.claude/projects/<any>/<session>.jsonl
            var projects = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
            if (!Directory.Exists(projects))
            {
                return null;
            }

            return Directory.EnumerateDirectories(projects)
                .Select(dir => Path.Combine(dir, session.SessionId + ".jsonl"))
                .FirstOrDefault(File.Exists);
        }

        private void AssignSlots()
        {
            for (var i = 0; i < SlotCount; i++)
            {
                if (this._slots[i] != null && !this._sessions.ContainsKey(this._slots[i]))
                {
                    this._slots[i] = null;
                }
            }

            var unassigned = this._sessions.Values
                .Where(s => Array.IndexOf(this._slots, s.SessionId) < 0)
                .OrderBy(s => s.Started)
                .ThenBy(s => s.SessionId, StringComparer.Ordinal);

            foreach (var session in unassigned)
            {
                var free = Array.IndexOf(this._slots, null);
                if (free < 0)
                {
                    break;
                }

                this._slots[free] = session.SessionId;
            }
        }

        private static Dictionary<String, ClaudeSession> LoadSessions()
        {
            var result = new Dictionary<String, ClaudeSession>();
            if (!Directory.Exists(StatusDirectory))
            {
                return result;
            }

            foreach (var file in Directory.EnumerateFiles(StatusDirectory, "*.json"))
            {
                if (!file.EndsWith(".json", StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var session = JsonSerializer.Deserialize<ClaudeSession>(stream, JsonOptions);
                    if (!String.IsNullOrEmpty(session?.SessionId))
                    {
                        session.HookSince = session.Since;
                        result[session.SessionId] = session;
                    }
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                    // vanished between listing and reading, or not ours
                }
            }

            return result;
        }

        // Sessions whose claude process is gone (terminal closed, crash) never send SessionEnd.
        private static void RemoveDeadSessions(Dictionary<String, ClaudeSession> sessions)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var session in sessions.Values.ToList())
            {
                var dead = session.Pid > 0
                    ? !IsSessionProcessAlive(session)
                    : now - Math.Max(session.Since, session.Started) > OrphanAge.TotalSeconds;

                if (!dead)
                {
                    continue;
                }

                sessions.Remove(session.SessionId);
                foreach (var ext in new[] { ".json", ".meta" })
                {
                    try
                    {
                        File.Delete(Path.Combine(StatusDirectory, session.SessionId + ext));
                    }
                    catch (IOException)
                    {
                    }
                }

                PluginLog.Info($"Removed stale session {session.SessionId} ({session.Project})");
            }
        }

        // The native claude binary is named after its version (e.g. "2.1.316"), so the
        // process name says nothing. Instead: the process must exist and must have been
        // started before the session's first hook ran (otherwise the PID was reused).
        private static Boolean IsSessionProcessAlive(ClaudeSession session)
        {
            try
            {
                using var process = Process.GetProcessById(session.Pid);
                if (process.HasExited)
                {
                    return false;
                }

                var processStart = new DateTimeOffset(process.StartTime).ToUnixTimeSeconds();
                return session.Started <= 0 || processStart <= session.Started + 5;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}
