namespace Loupedeck.ClaudeStatusPlugin
{
    using System;

    // One Claude Code session as written by hooks/keypad-status (~/.claude/keypad/<id>.json).
    public sealed class ClaudeSession
    {
        public String SessionId { get; set; } = "";

        // idle | working | waiting | done | error
        public String State { get; set; } = "";

        // permission | question | plan (only while waiting)
        public String Reason { get; set; } = "";

        private String _project = "";

        // folder name of the working directory (older status files carry it explicitly)
        public String Project
        {
            get => String.IsNullOrEmpty(this._project) ? System.IO.Path.GetFileName((this.Cwd ?? "").TrimEnd('/')) : this._project;
            set => this._project = value ?? "";
        }

        public String Cwd { get; set; } = "";

        public String Tool { get; set; } = "";

        public String Message { get; set; } = "";

        public Int32 Pid { get; set; }

        public String Tty { get; set; } = "";

        // unique id part of $ITERM_SESSION_ID (empty outside iTerm2)
        public String Iterm { get; set; } = "";

        public String TermProgram { get; set; } = "";

        // bundle id of the app hosting the terminal ($__CFBundleIdentifier), e.g. com.jetbrains.PhpStorm
        public String App { get; set; } = "";

        // unix seconds: session start / current state start
        public Int64 Started { get; set; }

        public Int64 Since { get; set; }

        // transcript_path from the hook (may be missing in older status files)
        public String Transcript { get; set; } = "";

        // Session name from the transcript, filled in by SessionStore.
        [System.Text.Json.Serialization.JsonIgnore]
        public String Title { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public String DisplayName => String.IsNullOrEmpty(this.Title) ? this.Project : this.Title;

        public String AckKey => $"{this.SessionId}:{this.Since}";

        // "since" as the hook wrote it (Since may be corrected from the transcript)
        [System.Text.Json.Serialization.JsonIgnore]
        public Int64 HookSince { get; set; }

        // a dismissed session comes back as soon as the hook reports a new state
        [System.Text.Json.Serialization.JsonIgnore]
        public String DismissKey => $"{this.SessionId}:{this.HookSince}";
    }
}
