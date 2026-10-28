#!/bin/sh
# PolyPrompt fake agent CLI. Installed under a harness name (codex, claude, gemini, ...) first on PATH, it records
# how it was invoked and plays the scenario named by POLYPROMPT_FAKE_CLI_SCRIPT from $POLYPROMPT_FAKE_CLI_DIR/scripts.
#
# Scenario directives, one per line:
#   out <text>        write <text> and a newline to stdout
#   outraw <text>     write <text> to stdout without a newline
#   err <text>        write <text> and a newline to stderr
#   stdin             read stdin to end of file and record it
#   readline          read one line of stdin and record it
#   sleep <seconds>   sleep
#   child <seconds>   start a background child that sleeps, and record its pid
#   spam <count>      write <count> 'x' characters to stdout without a newline
#   ifarg <text>      run the following lines only if one argument equals <text>, up to endif
#   endif             end an ifarg block
#   exit <code>       exit with <code>
#   # ...             comment

dir="${POLYPROMPT_FAKE_CLI_DIR:?POLYPROMPT_FAKE_CLI_DIR is not set}"
script="$dir/scripts/${POLYPROMPT_FAKE_CLI_SCRIPT:?POLYPROMPT_FAKE_CLI_SCRIPT is not set}.txt"
rec="$dir/record"
mkdir -p "$rec"

: > "$rec/argv"
for a in "$@"; do printf '%s\0' "$a" >> "$rec/argv"; done
printf '%s\n' "$*" >> "$rec/invocations"
pwd > "$rec/cwd"
printf '%s\n' "$$" > "$rec/pid"
printf '%s' "${POLYPROMPT_FAKE_CLI_ECHO:-}" > "$rec/echo"

if [ ! -f "$script" ]; then
  printf 'fake-cli: no scenario %s\n' "$script" >&2
  exit 97
fi

skip=0
exec 3< "$script"
while IFS= read -r line <&3 || [ -n "$line" ]; do
  cmd=${line%% *}
  if [ "$cmd" = "$line" ]; then arg=""; else arg=${line#* }; fi
  if [ "$skip" = 1 ] && [ "$cmd" != "endif" ]; then continue; fi
  case "$cmd" in
    out) printf '%s\n' "$arg" ;;
    outraw) printf '%s' "$arg" ;;
    err) printf '%s\n' "$arg" >&2 ;;
    stdin) cat > "$rec/stdin" ;;
    readline) IFS= read -r input; printf '%s\n' "$input" >> "$rec/stdin" ;;
    sleep) sleep "$arg" ;;
    child) sleep "$arg" & printf '%s\n' "$!" > "$rec/child.pid" ;;
    spam) head -c "$arg" /dev/zero | tr '\0' 'x' ;;
    ifarg) skip=1; for a in "$@"; do if [ "$a" = "$arg" ]; then skip=0; fi; done ;;
    endif) skip=0 ;;
    exit) exit "$arg" ;;
    ''|'#'*) ;;
    *) printf 'fake-cli: unknown directive %s\n' "$cmd" >&2; exit 98 ;;
  esac
done
exit 0
