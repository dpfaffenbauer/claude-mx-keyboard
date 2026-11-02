namespace Loupedeck.ClaudeStatusPlugin
{
    using System;
    using System.Globalization;

    // Texts drawn on the keys and shown in Options+. English by default, German when
    // Options+ (or, as a fallback, macOS) runs in German.
    public static class Strings
    {
        private static Boolean _german = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";

        public static void Init(String language)
        {
            if (!String.IsNullOrEmpty(language))
            {
                _german = language.StartsWith("de", StringComparison.OrdinalIgnoreCase);
            }
        }

        public static String Working => _german ? "läuft" : "working";

        public static String Permission => _german ? "Freigabe" : "approve";

        public static String Question => _german ? "Frage" : "question";

        public static String Plan => _german ? "Plan ok?" : "plan ok?";

        public static String Waiting => _german ? "wartet" : "waiting";

        public static String Done => _german ? "fertig" : "done";

        public static String Error => _german ? "Fehler" : "error";

        public static String Idle => _german ? "bereit" : "ready";

        public static String SummaryWaitingOne => _german ? "wartet" : "waiting";

        public static String SummaryWaitingMany => _german ? "warten" : "waiting";

        public static String SummaryWorking => _german ? "laufen" : "working";

        public static String SummaryQuiet => _german ? "ruhig" : "quiet";

        public static String SessionGroup => "Claude Code";

        public static String SlotName(Int32 number) => $"Claude Session {number}";

        public static String NextWaitingName => _german ? "Nächste wartende Session" : "Next waiting session";

        public static String NextWaitingDescription => _german
            ? "Springt zur Claude-Code-Session, die am längsten auf dich wartet"
            : "Jumps to the Claude Code session that has been waiting for you the longest";

        public static String HooksMissing => _german
            ? "Claude-Code-Hooks fehlen – Plugin neu installieren oder Setup in der Anleitung ausführen."
            : "Claude Code hooks are missing - reinstall the plugin or run the setup from the guide.";

        public static String ClaudeCodeMissing => _german
            ? "Claude Code wurde auf diesem Mac nicht gefunden (~/.claude fehlt)."
            : "Claude Code was not found on this Mac (~/.claude is missing).";

        public static String SettingsUnreadable => _german
            ? "~/.claude/settings.json ist kein gültiges JSON – Hooks konnten nicht eingetragen werden."
            : "~/.claude/settings.json is not valid JSON - hooks could not be registered.";

        public static String Guide => _german ? "Anleitung" : "Guide";
    }
}
