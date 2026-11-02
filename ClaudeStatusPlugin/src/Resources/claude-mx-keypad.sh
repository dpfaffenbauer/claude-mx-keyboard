#!/bin/bash
# Claude MX Keypad - Claude Code hook
# https://github.com/dpfaffenbauer/claude-mx-keyboard
#
# Installed and registered in ~/.claude/settings.json by the "Claude Code Sessions"
# Logi plugin. Writes one status file per Claude Code session for the plugin:
#
#   ~/.claude/keypad/<session_id>.json   read by the plugin
#   ~/.claude/keypad/<session_id>.meta   hook-internal cache (pid, tty, start)
#
# Plain bash 3.2 (macOS /bin/bash), no jq or other dependencies. Claude Code sends
# compact JSON whose top-level fields (session_id, cwd, hook_event_name, tool_name)
# precede any nested data, so the first match of a key is the top-level one.
# Must never get in Claude's way: no stdout, always exit 0.

exec >/dev/null 2>&1
trap 'exit 0' ERR

dir="${CLAUDE_KEYPAD_DIR:-$HOME/.claude/keypad}"
mkdir -p "$dir" || exit 0

input=$(cat)
head=${input:0:8192}

# field <key>: raw JSON string value of the first "<key>":"..." (still JSON-escaped,
# so it can be written back into JSON verbatim)
field() {
  local re="\"$1\":\"(([^\"\\\\]|\\\\.)*)\""
  if [[ $head =~ $re ]]; then REPLY=${BASH_REMATCH[1]}; else REPLY=""; fi
}

field session_id;        sid=$REPLY
field hook_event_name;   event=$REPLY
field cwd;               cwd=$REPLY
field transcript_path;   transcript=$REPLY

# session ids are UUIDs; refuse anything that could escape the directory
[[ "$sid" =~ ^[A-Za-z0-9_-]+$ ]] || exit 0
json="$dir/$sid.json"
meta="$dir/$sid.meta"

if [[ "$event" == "SessionEnd" ]]; then
  rm -f "$json" "$meta"
  exit 0
fi

tool="" reason=""
case "$event" in
  SessionStart)
    field source
    # a compaction restarts the session mid-work; don't drop to idle then
    if [[ "$REPLY" == "compact" ]]; then state=working; else state=idle; fi ;;
  UserPromptSubmit|PreCompact)
    state=working ;;
  PreToolUse|PostToolUse)
    # tool calls made by plugins outside a turn (e.g. polling an MCP tool after
    # every Stop) carry a "toolu_plugin_" id - they don't mean Claude is working
    field tool_use_id
    [[ "$REPLY" == toolu_plugin_* ]] && exit 0
    field tool_name; tool=$REPLY
    state=working
    if [[ "$event" == "PreToolUse" ]]; then
      case "$tool" in
        AskUserQuestion) state=waiting; reason=question ;;
        ExitPlanMode)    state=waiting; reason=plan ;;
      esac
    fi ;;
  PermissionRequest)
    state=waiting; reason=permission ;;
  Notification)
    field notification_type
    case "$REPLY" in
      permission_prompt)  state=waiting; reason=permission ;;
      elicitation_dialog) state=waiting; reason=question ;;
      *) exit 0 ;;   # idle_prompt, auth_success, ... don't change the state
    esac ;;
  Stop)        state=done ;;
  StopFailure) state=error ;;
  *) exit 0 ;;
esac

# --- per-session constants, resolved once ------------------------------------
pid="" tty="" started="" prev="" prevreason=""
[[ -f "$meta" ]] && read -r pid tty started prev prevreason _ < "$meta"

if [[ -z "$started" ]]; then
  # walk up from the hook's shell to the claude process (pid + controlling tty)
  p=$PPID
  for _ in 1 2 3 4 5 6; do
    [[ -n "$p" && "$p" -gt 1 ]] || break
    read -r pp t c < <(ps -o ppid=,tty=,comm= -p "$p")
    if [[ "${c##*/}" == "claude" ]]; then pid=$p; tty=$t; break; fi
    p=$pp
  done
  [[ "$pid" =~ ^[0-9]+$ ]] || pid=0
  [[ "$tty" =~ ^[A-Za-z0-9]+$ ]] || tty=-
  started=$(date +%s)
fi

if [[ -f "$dir/.debug" ]]; then
  echo "$(date +%H:%M:%S) ${sid:0:8} $event tool=$tool prev=$prev -> $state" >> "$dir/debug.log"
fi

# hot path: PreToolUse/PostToolUse fire constantly while working
if [[ "$state" == "$prev" && "${reason:--}" == "${prevreason:--}" && -f "$json" ]]; then
  exit 0
fi

now=$(date +%s)
iterm=${ITERM_SESSION_ID#*:}
iterm=${iterm//[^A-Za-z0-9-]/}
term=${TERM_PROGRAM//[^A-Za-z0-9._-]/}
app=${__CFBundleIdentifier//[^A-Za-z0-9._-]/}

tmp="$json.$$"
printf '{"sessionId":"%s","state":"%s","reason":"%s","cwd":"%s","transcript":"%s","tool":"%s","pid":%s,"tty":"%s","iterm":"%s","termProgram":"%s","app":"%s","started":%s,"since":%s}\n' \
  "$sid" "$state" "$reason" "$cwd" "$transcript" "$tool" "$pid" "$tty" "$iterm" "$term" "$app" "$started" "$now" \
  > "$tmp" && mv -f "$tmp" "$json"

echo "$pid $tty $started $state ${reason:--} $now" > "$meta"
exit 0
