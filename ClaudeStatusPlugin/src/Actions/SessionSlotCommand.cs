namespace Loupedeck.ClaudeStatusPlugin
{
    using System;
    using System.Collections.Generic;

    // "Claude Session 1..9": one key per running Claude Code session.
    // Shows the session name + state. A press focuses the session's terminal tab (and clears
    // a "done"/"error" marker); a double press takes a finished or idle session off the keypad
    // until it does something new.
    public class SessionSlotCommand : PluginDynamicCommand
    {
        private const Int64 DoublePressMs = 450;

        private readonly Dictionary<String, String> _signatures = new();
        private String _lastPressSession;
        private Int64 _lastPressAt;

        public SessionSlotCommand()
            : base()
        {
            for (var i = 1; i <= SessionStore.SlotCount; i++)
            {
                this.AddParameter(ParameterFor(i), Strings.SlotName(i), Strings.SessionGroup);
            }

            SessionStore.Instance.Updated += this.OnSessionsUpdated;
        }

        protected override void RunCommand(String actionParameter)
        {
            var view = SessionStore.Instance.GetSlot(IndexOf(actionParameter));
            if (view == null)
            {
                return;
            }

            var now = Environment.TickCount64;
            var isDoublePress = view.Session.SessionId == this._lastPressSession && now - this._lastPressAt <= DoublePressMs;
            this._lastPressSession = view.Session.SessionId;
            this._lastPressAt = now;
            if (isDoublePress)
            {
                // never hide a session that is working or needs you
                if (view.State is SessionDisplayState.Idle or SessionDisplayState.Done or SessionDisplayState.Error)
                {
                    SessionStore.Instance.Dismiss(view.Session);
                    this._lastPressSession = null;
                }

                return;
            }

            TerminalFocus.Focus(view.Session);
            if (view.State is SessionDisplayState.Done or SessionDisplayState.Error)
            {
                SessionStore.Instance.Acknowledge(view.Session);
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var index = IndexOf(actionParameter);
            return KeyRenderer.RenderSlot(imageSize, SessionStore.Instance.GetSlot(index), index + 1);
        }

        // The key image already carries the session name, so no caption on keys.
        // An empty string makes the service fall back to "Claude Session N"; a zero-width
        // space is neither empty nor whitespace and draws nothing. Lists keep the slot name.
        private const String InvisibleCaption = "\u200B";

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            imageSize == PluginImageSize.None ? Strings.SlotName(IndexOf(actionParameter) + 1) : InvisibleCaption;

        private void OnSessionsUpdated()
        {
            for (var i = 1; i <= SessionStore.SlotCount; i++)
            {
                var parameter = ParameterFor(i);
                var signature = KeyRenderer.SlotSignature(SessionStore.Instance.GetSlot(i - 1));
                if (!this._signatures.TryGetValue(parameter, out var previous) || previous != signature)
                {
                    this._signatures[parameter] = signature;
                    this.ActionImageChanged(parameter);
                }
            }
        }

        private static String ParameterFor(Int32 slotNumber) => $"slot{slotNumber}";

        private static Int32 IndexOf(String actionParameter) =>
            actionParameter != null && actionParameter.StartsWith("slot", StringComparison.Ordinal)
                && Int32.TryParse(actionParameter.AsSpan(4), out var n)
                ? n - 1
                : -1;
    }
}
