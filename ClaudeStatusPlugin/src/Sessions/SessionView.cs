namespace Loupedeck.ClaudeStatusPlugin
{
    using System;

    // A session as it should be shown right now (after acknowledgement).
    public sealed class SessionView
    {
        public SessionView(ClaudeSession session, SessionDisplayState state)
        {
            this.Session = session;
            this.State = state;
        }

        public ClaudeSession Session { get; }

        public SessionDisplayState State { get; }

        public static SessionDisplayState Map(String state) => state switch
        {
            "working" => SessionDisplayState.Working,
            "waiting" => SessionDisplayState.Waiting,
            "done" => SessionDisplayState.Done,
            "error" => SessionDisplayState.Error,
            _ => SessionDisplayState.Idle,
        };
    }
}
