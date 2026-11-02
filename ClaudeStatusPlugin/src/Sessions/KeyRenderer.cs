namespace Loupedeck.ClaudeStatusPlugin
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    // Draws the LCD keys. Everything is relative to the key size the service asks for.
    //
    //   ┌──────────────┐  dark key, colour only as accent
    //   │ Logi MX      │
    //   │ Keypad Claude│  session name, word-wrapped, up to 3 lines
    //   │ Status       │
    //   │ läuft 3m     │  state + time, in the state colour
    //   │ ▂▂▂▂████▂▂▂▂ │  state bar (sweeps while Claude works)
    //   └──────────────┘
    //   Only "waiting" fills the whole key (amber, pulsing) – it needs you.
    public static class KeyRenderer
    {
        private const Int32 MaxTitleLines = 3;

        // average character width of the device font relative to its size
        // (measured on the MX Creative Keypad: ~0.48; a little slack for wide letters)
        private const Double CharWidthEm = 0.5;

        private const Int32 SweepSteps = 6;

        private static readonly BitmapColor KeyBackground = new(22, 22, 25);
        private static readonly BitmapColor TextColor = new(240, 240, 242);
        private static readonly BitmapColor DimText = new(150, 150, 158);

        // Claude coral for "working", green/red for done/error, amber for "needs you"
        private static readonly Theme IdleTheme = new(KeyBackground, new(175, 175, 182), new(115, 115, 124), new(58, 58, 66), new(58, 58, 66));
        private static readonly Theme WorkingTheme = new(KeyBackground, TextColor, new(236, 145, 115), new(217, 119, 87), new(70, 42, 34));
        private static readonly Theme DoneTheme = new(KeyBackground, TextColor, new(120, 222, 140), new(64, 192, 87), new(64, 192, 87));
        private static readonly Theme ErrorTheme = new(KeyBackground, TextColor, new(255, 140, 140), new(240, 62, 62), new(240, 62, 62));
        private static readonly Theme WaitingThemeA = new(new(250, 176, 5), new(28, 22, 8), new(70, 46, 0), new(250, 176, 5), new(250, 176, 5));
        private static readonly Theme WaitingThemeB = new(new(240, 140, 0), new(28, 22, 8), new(70, 40, 0), new(240, 140, 0), new(240, 140, 0));
        private static readonly ConcurrentDictionary<PluginImageSize, Boolean> LoggedSizes = new();

        public static Boolean BlinkPhase => DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 2 == 0;

        private static Int64 SweepPhase => DateTimeOffset.UtcNow.ToUnixTimeSeconds() % SweepSteps;

        // Changes whenever the rendered image would change -> drives ActionImageChanged.
        public static String SlotSignature(SessionView view)
        {
            if (view == null)
            {
                return "empty";
            }

            var animation = view.State switch
            {
                SessionDisplayState.Waiting => BlinkPhase ? 1 : 0,
                SessionDisplayState.Working => SweepPhase,
                _ => 0,
            };
            return $"{view.State}|{view.Session.DisplayName}|{StatusLine(view)}|{animation}";
        }

        public static BitmapImage RenderSlot(PluginImageSize size, SessionView view, Int32 slotNumber)
        {
            using var b = new BitmapBuilder(size);
            var w = b.Width;
            var h = b.Height;
            if (LoggedSizes.TryAdd(size, true))
            {
                PluginLog.Info($"Key image {size}: {w}x{h}");
            }

            if (view == null)
            {
                // no session in this slot: key stays dark
                b.Clear(BitmapColor.Black);
                return b.ToImage();
            }

            var theme = ThemeFor(view.State);
            b.Clear(theme.Background);
            DrawStateBar(b, view.State, theme);

            // session name: as large as possible while the whole name fits, vertically centred
            var areaTop = h * 7 / 100;
            var areaHeight = (h * 70 / 100) - areaTop;
            var (titleSize, lines) = FitTitle(view.Session.DisplayName, w, areaHeight, h);
            var lineHeight = LineHeight(titleSize);
            var y = areaTop + Math.Max(0, (areaHeight - (lines.Count * lineHeight)) / 2);
            foreach (var line in lines)
            {
                b.DrawText(line, 0, y, w, lineHeight, theme.Title, titleSize);
                y += lineHeight;
            }

            var statusSize = Math.Max(8, h * 14 / 100);
            b.DrawText(StatusLine(view), 0, h * 71 / 100, w, h * 19 / 100, theme.Status, statusSize);
            return b.ToImage();
        }

        // Bottom bar in the state colour; while working, a segment sweeps across a dim track
        // (one step per second) so a running session visibly "breathes".
        private static void DrawStateBar(BitmapBuilder b, SessionDisplayState state, Theme theme)
        {
            if (state == SessionDisplayState.Waiting)
            {
                return;
            }

            var w = b.Width;
            var barHeight = Math.Max(4, b.Height * 6 / 100);
            var y = b.Height - barHeight;
            b.FillRectangle(0, y, w, barHeight, theme.Track);

            if (state == SessionDisplayState.Working)
            {
                var segment = w * 2 / 5;
                var x = (Int32)((SweepPhase + 1) * (w + segment) / (SweepSteps + 1)) - segment;
                var left = Math.Max(0, x);
                var right = Math.Min(w, x + segment);
                if (right > left)
                {
                    b.FillRectangle(left, y, right - left, barHeight, theme.Accent);
                }
            }
        }

        // Largest font size (24% .. 11% of the key) at which the name fits in MaxTitleLines
        // lines without splitting words; below that, words are split and the rest cut with "…".
        private static (Int32 FontSize, List<String> Lines) FitTitle(String text, Int32 width, Int32 areaHeight, Int32 keyHeight)
        {
            var largest = keyHeight * 24 / 100;
            var smallest = Math.Max(9, keyHeight * 11 / 100);
            var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var longestWord = words.Length == 0 ? 0 : words.Max(word => word.Length);

            for (var size = largest; size >= smallest; size--)
            {
                var maxChars = MaxChars(width, size);
                if (longestWord > maxChars)
                {
                    continue;
                }

                var lines = Wrap(text, maxChars, Int32.MaxValue);
                if (lines.Count <= MaxTitleLines && lines.Count * LineHeight(size) <= areaHeight)
                {
                    return (size, lines);
                }
            }

            return (smallest, Wrap(text, MaxChars(width, smallest), MaxTitleLines));
        }

        private static Int32 LineHeight(Int32 fontSize) => fontSize * 6 / 5;

        public static BitmapImage RenderSummary(PluginImageSize size, Int32 waiting, Int32 done, Int32 working)
        {
            using var b = new BitmapBuilder(size);
            var w = b.Width;
            var h = b.Height;

            String count, label;
            SessionDisplayState state;
            if (waiting > 0)
            {
                (state, count, label) = (SessionDisplayState.Waiting, waiting.ToString(), waiting == 1 ? Strings.SummaryWaitingOne : Strings.SummaryWaitingMany);
            }
            else if (done > 0)
            {
                (state, count, label) = (SessionDisplayState.Done, done.ToString(), Strings.Done);
            }
            else if (working > 0)
            {
                (state, count, label) = (SessionDisplayState.Working, working.ToString(), Strings.SummaryWorking);
            }
            else
            {
                (state, count, label) = (SessionDisplayState.Idle, "–", Strings.SummaryQuiet);
            }

            var theme = ThemeFor(state);
            b.Clear(theme.Background);
            DrawStateBar(b, state, theme);
            var countColor = state is SessionDisplayState.Waiting or SessionDisplayState.Idle ? theme.Title : theme.Status;
            b.DrawText("Claude", 0, h * 8 / 100, w, h * 20 / 100, state == SessionDisplayState.Waiting ? theme.Status : DimText, Math.Max(9, h * 14 / 100));
            b.DrawText(count, 0, h * 27 / 100, w, h * 40 / 100, countColor, Math.Max(14, h * 36 / 100));
            b.DrawText(label, 0, h * 67 / 100, w, h * 20 / 100, theme.Status, Math.Max(9, h * 15 / 100));
            return b.ToImage();
        }

        public static String SummarySignature(Int32 waiting, Int32 done, Int32 working) =>
            $"{waiting}|{done}|{working}|{(waiting > 0 ? (BlinkPhase ? 1 : 0) : working > 0 && done == 0 ? SweepPhase : 0)}";

        private static Theme ThemeFor(SessionDisplayState state) => state switch
        {
            SessionDisplayState.Working => WorkingTheme,
            SessionDisplayState.Waiting => BlinkPhase ? WaitingThemeA : WaitingThemeB,
            SessionDisplayState.Done => DoneTheme,
            SessionDisplayState.Error => ErrorTheme,
            _ => IdleTheme,
        };

        // "working 3m", "approve 12m", "done 1h05", "ready"
        private static String StatusLine(SessionView view)
        {
            var label = view.State switch
            {
                SessionDisplayState.Working => Strings.Working,
                SessionDisplayState.Waiting => view.Session.Reason switch
                {
                    "permission" => Strings.Permission,
                    "question" => Strings.Question,
                    "plan" => Strings.Plan,
                    _ => Strings.Waiting,
                },
                SessionDisplayState.Done => Strings.Done,
                SessionDisplayState.Error => Strings.Error,
                _ => Strings.Idle,
            };

            if (view.State == SessionDisplayState.Idle || view.Session.Since <= 0)
            {
                return label;
            }

            var seconds = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - view.Session.Since);
            var elapsed = seconds < 60 ? "<1m"
                : seconds < 3600 ? $"{seconds / 60}m"
                : $"{seconds / 3600}h{(seconds % 3600) / 60:00}";
            return $"{label} {elapsed}";
        }

        private static Int32 MaxChars(Int32 width, Int32 fontSize) =>
            Math.Max(4, (Int32)(width * 0.92 / (fontSize * CharWidthEm)));

        // Greedy word wrap. Words longer than a line are split; if the text needs more
        // than maxLines, the last visible line ends with an ellipsis.
        private static List<String> Wrap(String text, Int32 maxChars, Int32 maxLines)
        {
            var words = new List<String>();
            foreach (var word in (String.IsNullOrWhiteSpace(text) ? "claude" : text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                for (var i = 0; i < word.Length; i += maxChars)
                {
                    words.Add(word.Substring(i, Math.Min(maxChars, word.Length - i)));
                }
            }

            var lines = new List<String>();
            var current = "";
            foreach (var word in words)
            {
                if (current.Length == 0)
                {
                    current = word;
                }
                else if (current.Length + 1 + word.Length <= maxChars)
                {
                    current += " " + word;
                }
                else
                {
                    lines.Add(current);
                    current = word;
                }
            }

            if (current.Length > 0)
            {
                lines.Add(current);
            }

            if (lines.Count > maxLines)
            {
                lines.RemoveRange(maxLines, lines.Count - maxLines);
                var last = lines[^1];
                lines[^1] = (last.Length < maxChars ? last : last[..(maxChars - 1)]) + "…";
            }

            return lines;
        }

        private readonly record struct Theme(
            BitmapColor Background,
            BitmapColor Title,
            BitmapColor Status,
            BitmapColor Accent,
            BitmapColor Track);
    }
}
