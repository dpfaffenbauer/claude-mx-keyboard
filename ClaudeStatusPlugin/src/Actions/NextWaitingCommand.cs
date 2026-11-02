namespace Loupedeck.ClaudeStatusPlugin
{
    using System;
    using System.Linq;

    // Summary key: how many sessions wait for you; pressing it jumps to the
    // longest-waiting one (or, if none waits, to the oldest finished one).
    public class NextWaitingCommand : PluginDynamicCommand
    {
        private String _signature;

        public NextWaitingCommand()
            : base(displayName: Strings.NextWaitingName, description: Strings.NextWaitingDescription, groupName: Strings.SessionGroup)
        {
            SessionStore.Instance.Updated += this.OnSessionsUpdated;
        }

        protected override void RunCommand(String actionParameter)
        {
            var all = SessionStore.Instance.GetAll();
            var target = all.Where(v => v.State == SessionDisplayState.Waiting).OrderBy(v => v.Session.Since).FirstOrDefault()
                ?? all.Where(v => v.State is SessionDisplayState.Done or SessionDisplayState.Error).OrderBy(v => v.Session.Since).FirstOrDefault();

            if (target == null)
            {
                return;
            }

            TerminalFocus.Focus(target.Session);
            if (target.State is SessionDisplayState.Done or SessionDisplayState.Error)
            {
                SessionStore.Instance.Acknowledge(target.Session);
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var (waiting, done, working) = Count();
            return KeyRenderer.RenderSummary(imageSize, waiting, done, working);
        }

        private void OnSessionsUpdated()
        {
            var (waiting, done, working) = Count();
            var signature = KeyRenderer.SummarySignature(waiting, done, working);
            if (signature != this._signature)
            {
                this._signature = signature;
                this.ActionImageChanged();
            }
        }

        private static (Int32 Waiting, Int32 Done, Int32 Working) Count()
        {
            var all = SessionStore.Instance.GetAll();
            return (
                all.Count(v => v.State == SessionDisplayState.Waiting),
                all.Count(v => v.State is SessionDisplayState.Done or SessionDisplayState.Error),
                all.Count(v => v.State == SessionDisplayState.Working));
        }
    }
}
