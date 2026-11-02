# End User License Agreement

**Session Status for Claude Code** – plugin for Logi Options+ / Logi Plugin Service

Last updated: 2 November 2026

This End User License Agreement ("Agreement") is between you and Dominik Pfaffenbauer ("Developer") and
covers the plugin "Session Status for Claude Code" (the "Software"), whether you get it from the Logitech
Marketplace or from [GitHub](https://github.com/dpfaffenbauer/claude-mx-keyboard). By installing or using
the Software you accept this Agreement. If you do not accept it, do not install or use the Software.

Logitech is not a party to this Agreement. Logitech's own terms apply to Logi Options+, the Logi Plugin
Service and the Logitech Marketplace.

## 1. License

The Software is open source and licensed under the [MIT License](LICENSE). You may use, copy, modify,
merge, publish, distribute, sublicense and sell copies of it under the terms of that license. Nothing in this
Agreement restricts the rights the MIT License grants you; where the two differ on those rights, the MIT
License prevails.

The Software is free of charge.

## 2. What the Software does on your computer

To show the status of your Claude Code sessions, the Software:

- writes a hook script to `~/.claude/hooks/claude-mx-keypad.sh`;
- adds entries for that script to the `"hooks"` section of `~/.claude/settings.json` and saves a backup of
  the file as `settings.json.bak-claude-mx-keypad` before its first change; no other setting is changed;
- writes small status files to `~/.claude/keypad/`;
- reads Claude Code session transcripts in `~/.claude/projects/` and the session records of the Claude desktop
  app on your computer, to show session names and states;
- uses AppleScript (iTerm2, Terminal, System Events) to bring a session's window to the front when you press
  a key. macOS asks for your permission before this works.

Uninstalling the Software through Logi Options+ removes the hook entries and the hook script again.

## 3. Privacy

The Software does not collect, store or transmit personal data to the Developer or to anyone else. It makes no
network connections and contains no analytics or telemetry. Everything it reads and writes stays on your
computer.

## 4. Trademarks and affiliation

The Software is an unofficial community project. It is not affiliated with, endorsed by or sponsored by
Anthropic PBC or Logitech. "Claude" and "Claude Code" are trademarks of Anthropic PBC. "Logitech", "Logi",
"MX" and "Options+" are trademarks of Logitech. All other trademarks belong to their owners. The names are
used only to describe what the Software works with.

## 5. No warranty

The Software is provided "as is" and "as available", without warranty of any kind, express or implied,
including the warranties of merchantability, fitness for a particular purpose and non-infringement. The
Developer does not warrant that the Software works without errors or interruptions, or that it stays
compatible with future versions of Claude Code, Logi Options+ or macOS.

## 6. Limitation of liability

To the maximum extent permitted by law, the Developer is not liable for any direct, indirect, incidental,
special or consequential damages arising from the use of, or the inability to use, the Software, including
loss of data or changes to configuration files. This does not limit liability for intent or gross negligence,
for personal injury, or any other liability that cannot be excluded or limited under applicable law.

## 7. Updates and support

The Developer may provide updates but is not obliged to. Support is voluntary and provided through
[GitHub issues](https://github.com/dpfaffenbauer/claude-mx-keyboard/issues).

## 8. Termination

This Agreement applies for as long as you use the Software. You can end it at any time by uninstalling the
Software. Your rights under the MIT License are not affected by termination.

## 9. Governing law

This Agreement is governed by the laws of Austria, excluding its conflict-of-law rules and the UN Convention
on Contracts for the International Sale of Goods. If you are a consumer, the mandatory consumer protection
laws of your country of residence remain unaffected.

If any provision of this Agreement is held invalid, the remaining provisions stay in effect.

## 10. Contact

Dominik Pfaffenbauer – <https://github.com/dpfaffenbauer/claude-mx-keyboard/issues>
