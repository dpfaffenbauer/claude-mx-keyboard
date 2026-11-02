namespace Loupedeck.ClaudeStatusPlugin
{
    using System;
    using System.Reflection;

    // Shows the state of running Claude Code sessions on the MX Creative Keypad.
    // Data flow: Claude Code hook (Resources/claude-mx-keypad.sh) -> ~/.claude/keypad/*.json
    //            -> SessionStore -> SessionSlotCommand / NextWaitingCommand.
    public class ClaudeStatusPlugin : Plugin
    {
        public const String GuideUrl = "https://github.com/dpfaffenbauer/claude-mx-keyboard#setup";

        // plugin version the hooks were last set up for (setup runs once per version)
        private const String HooksSetupVersionSetting = "HooksSetupVersion";

        public ClaudeStatusPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
            this.InitStrings();
        }

        public override Boolean UsesApplicationApiOnly => true;

        public override Boolean HasNoApplication => true;

        private static String Version =>
            typeof(ClaudeStatusPlugin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0";

        public override void Load()
        {
            this.InitStrings();
            this.EnsureHooks();
            SessionStore.Instance.Start();
        }

        public override void Unload() => SessionStore.Instance.Stop();

        // Called by Logi Plugin Service right after installing the package.
        public override Boolean Install()
        {
            try
            {
                if (HookInstaller.Install(this.Assembly))
                {
                    this.SetPluginSetting(HooksSetupVersionSetting, Version, false);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Hook setup during install failed");
            }

            return true;
        }

        // Called right before the package is removed: take our hooks out of Claude Code.
        public override Boolean Uninstall()
        {
            HookInstaller.Uninstall();
            return true;
        }

        // Sets the hooks up once per plugin version (covers updates and development builds).
        // Within a version, hooks the user removed are not re-added - the plugin only reports it.
        private void EnsureHooks()
        {
            try
            {
                if (!this.TryGetPluginSetting(HooksSetupVersionSetting, out var setupFor) || setupFor != Version)
                {
                    if (HookInstaller.Install(this.Assembly))
                    {
                        this.SetPluginSetting(HooksSetupVersionSetting, Version, false);
                    }
                }
                else
                {
                    HookInstaller.RefreshScript(this.Assembly);
                }

                this.ReportHookState();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Hook setup failed");
                this.OnPluginStatusChanged(Loupedeck.PluginStatus.Error, ex.Message, GuideUrl, Strings.Guide);
            }
        }

        private void ReportHookState()
        {
            var state = HookInstaller.GetState();
            PluginLog.Info($"Claude Code hooks: {state}");
            switch (state)
            {
                case HookSetupState.Installed:
                    this.OnPluginStatusChanged(Loupedeck.PluginStatus.Normal, null);
                    break;
                case HookSetupState.ClaudeCodeMissing:
                    this.OnPluginStatusChanged(Loupedeck.PluginStatus.Warning, Strings.ClaudeCodeMissing, GuideUrl, Strings.Guide);
                    break;
                case HookSetupState.SettingsUnreadable:
                    this.OnPluginStatusChanged(Loupedeck.PluginStatus.Error, Strings.SettingsUnreadable, GuideUrl, Strings.Guide);
                    break;
                default:
                    this.OnPluginStatusChanged(Loupedeck.PluginStatus.Warning, Strings.HooksMissing, GuideUrl, Strings.Guide);
                    break;
            }
        }

        private void InitStrings()
        {
            try
            {
                Strings.Init(this.Localization?.LoupedeckLanguage);
            }
            catch (Exception)
            {
                // localization engine not ready yet - keep the system language
            }
        }
    }
}
