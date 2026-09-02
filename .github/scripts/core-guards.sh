#!/usr/bin/env bash
#
# Cheap greps the compiler cannot do. Run from anywhere:
#     bash .github/scripts/core-guards.sh
#
# GUARD 1 - forbidden APIs in CCP.Core.
#   Every pattern below compiles cleanly in a net8.0 library and then throws, no-ops, or
#   silently returns null at runtime on a non-Windows head. The target framework cannot
#   catch them, so they are caught here instead.
#
# GUARD 2 - XAML clr-namespace tripwire.
#   276 clr-namespace refs in this repo, only 11 carry ";assembly=". A clr-namespace without
#   ";assembly=" resolves in the LOCAL assembly. Move a XAML-referenced type to CCP.Core and
#   the app still compiles, then dies at runtime with XamlParseException. Both directions are
#   checked: an assembly-less prefix must name a type defined under ConditioningControlPanel/,
#   and a ";assembly=CCP.Core" prefix must name one defined under CCP.Core/. Without the second
#   half, the remedy this script prints would itself create a permanent blind spot.
#
# Deliberately NOT a XAML parser. Known limits: XML comments are stripped, but matching is
# otherwise textual, so it does not understand nested types, and it accepts either "Foo" or
# "FooExtension" for "{prefix:Foo}" because WPF appends the Extension suffix for markup
# extensions. Both are fine - this is a tripwire for moved files, not a type resolver.

set -uo pipefail

# Anchor to the repo root. Without this, running from the wrong directory makes every grep
# fail with "No such file or directory" and the script would report success while checking
# nothing - a guard that fails open is worse than no guard.
cd "$(dirname "$0")/../.." || exit 1
for d in CCP.Core ConditioningControlPanel; do
  [ -d "$d" ] || { echo "FATAL: $d not found - wrong repo root?"; exit 1; }
done

FAIL=0
# grep exits 1 on no-match (the success case here) and 2 on error, so never use `set -e`,
# and never conflate the two.
GREP_OPTS=(-rnE '--include=*.cs' --exclude-dir=obj --exclude-dir=bin)

WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

# ---------------------------------------------------------------------------------------------
# BASELINE - EMPTY. Nothing is exempt; every pattern above is enforced on every line of CCP.Core.
#
# It held seven WPF pack:// URIs in CompanionDefinition.cs and SeasonRecap.cs. Those are gone: Core
# now stores a bare file name and the WPF head composes "pack://application:,,,/Resources/{id}",
# so the entries were deleted here in the same PR, as the ratchet below requires.
#
# The mechanism is kept for the next migration wave, unused. Rules if it is ever repopulated: exact
# "file:line" only - no globs, no directory or pattern exemptions - and the guard FAILS on an entry
# that stops matching, so a listed line is either fixed-and-deleted or line-shifted-and-corrected.
# An allowlist that cannot go stale is how it stays a ratchet instead of a graveyard.
# ---------------------------------------------------------------------------------------------
cat > "$WORK/baseline.txt" <<'EOF'
EOF
: > "$WORK/seen.txt"

echo "== Guard 1: forbidden APIs in CCP.Core =="

# Comments are not calls. A doc comment saying <see cref="App.TutorialBaseUrl"/>, or a note that
# "App.UserDataPath is %APPDATA%/... by convention", must not fail the build - a guard that cries
# wolf over prose gets deleted. This drops comment-only lines and trailing //-comments, but only
# a "//" that is OUTSIDE a string literal, so a real "pack://application:,,,/x.png" still trips.
# Limit: a violation sitting inside a /* ... */ body whose lines start with neither * nor /* is
# still reported. Rare, and erring loud there is the right direction.
strip_comments() {
  PATTERN="$1" perl -ne '
    next unless /^([^:]*:\d+:)(.*)$/;
    my ($prefix, $code) = ($1, $2);
    next if $code =~ m{^\s*(//|\*|/\*)};
    $code =~ s{^((?:[^"/]+|"(?:\\.|[^"\\])*"|/(?!/))*)//.*$}{$1};
    print "$prefix$code\n" if $code =~ /$ENV{PATTERN}/;
  '
}

check() {
  local pattern="$1" why="$2" hits status
  hits=$(grep "${GREP_OPTS[@]}" -- "$pattern" CCP.Core); status=$?
  if [ "$status" -gt 1 ]; then
    echo "FATAL: grep failed (exit $status) for pattern [$pattern]"; FAIL=1; return 0
  fi
  [ "$status" -eq 1 ] && return 0
  hits=$(printf '%s\n' "$hits" | strip_comments "$pattern")
  [ -z "$hits" ] && return 0
  # Split the hits into baselined (known, scheduled for removal) and new (fail).
  local new="" h key rest
  while IFS= read -r h; do
    [ -z "$h" ] && continue
    rest="${h#*:}"; key="${h%%:*}:${rest%%:*}"      # file:line
    if grep -qxF "$key" "$WORK/baseline.txt"; then
      echo "$key" >> "$WORK/seen.txt"
    else
      new+="$h"$'\n'
    fi
  done <<< "$hits"
  [ -z "$new" ] && return 0
  echo "FAIL [$pattern] $why"
  printf '%s' "$new" | sed 's/^/    /'
  FAIL=1
}

check 'DllImport'                 'P/Invoke into a Win32 DLL. Not portable; use a managed API or push it behind an interface implemented by the head.'
check 'pack://'                   'WPF pack URI. Only PresentationFramework resolves these; Core has no resource loader.'
check 'Microsoft\.Win32'          'Win32 interop / registry / shell dialogs. Windows-only namespace.'
check 'ProtectedData'             'DPAPI. Windows-only encryption; throws PlatformNotSupportedException elsewhere.'
check '\bRegistry\b'              'Windows registry. Does not exist on Linux/Android; use a settings abstraction.'
check '\bApp\.[A-Z]'              'The WPF App singleton. Core must not reach into the application head.'
check 'GetManifestResourceStream' 'Embedded-resource read. Resources live in the app assembly; this silently returns null from Core.'
check 'GetExecutingAssembly'      'Returns whichever assembly the code compiled into - moving the caller to Core silently changes what it finds.'
check 'typeof\([^)]*\)\.Assembly' 'Same trap as GetExecutingAssembly: assembly identity changes when the type moves.'
check 'Assembly\.GetEntryAssembly' 'Entry assembly is the head (or null when hosted). Core must not depend on who started the process.'
[ "$FAIL" -eq 0 ] && echo "OK - no forbidden APIs in CCP.Core ($(wc -l < "$WORK/baseline.txt") baselined, see BASELINE)"

# The ratchet. A baselined line that no longer matches means the fix landed, so the entry is dead
# weight - and dead entries are how an allowlist turns into a permanent exemption nobody rereads.
sort -u "$WORK/seen.txt" > "$WORK/seen-uniq.txt"
while IFS= read -r entry; do
  [ -z "$entry" ] && continue
  grep -qxF "$entry" "$WORK/seen-uniq.txt" && continue
  echo "FAIL stale BASELINE entry: $entry no longer matches a forbidden pattern."
  echo "    Either the fix landed (delete that line from the BASELINE block in $0),"
  echo "    or an edit above it shifted the line number (correct the number)."
  FAIL=1
done < "$WORK/baseline.txt"

echo
echo "== Guard 2: XAML clr-namespace tripwire =="

types_in() {
  grep -rhoE '\b(class|struct|interface|enum|record)[[:space:]]+[A-Za-z_][A-Za-z0-9_]*' \
    '--include=*.cs' --exclude-dir=obj --exclude-dir=bin "$1" | awk '{print $2}' | sort -u
}
types_in ConditioningControlPanel > "$WORK/app.txt"
types_in CCP.Core               > "$WORK/core.txt"
APP_TYPES=$(wc -l < "$WORK/app.txt")
[ "$APP_TYPES" -gt 0 ] || { echo "FATAL: found 0 types under ConditioningControlPanel/"; exit 1; }

XAML_REFS=0
while IFS= read -r f; do
  # Strip XML comments first: a commented-out <local:OldControl/> must not fail the build.
  perl -0777 -pe 's/<!--.*?-->//gs' "$f" > "$WORK/x.xaml"
  # prefix<TAB>expected-source, for the two prefix kinds we can resolve.
  grep -oE 'xmlns:[A-Za-z0-9_]+="clr-namespace:[^"]*"' "$WORK/x.xaml" | sort -u \
    | sed -E 's/^xmlns:([A-Za-z0-9_]+)="clr-namespace:[^;"]*(;assembly=([^"]*))?"/\1\t\3/' \
    > "$WORK/prefixes.txt"
  while IFS=$'\t' read -r p asm; do
    [ -z "$p" ] && continue
    case "$asm" in
      "")         expected="$WORK/app.txt";  where="ConditioningControlPanel/" ;;
      CCP.Core)   expected="$WORK/core.txt"; where="CCP.Core/" ;;
      # Third-party / BCL assemblies (PresentationFramework, mscorlib, SkiaSharp, ...) are
      # somebody else's problem and cannot be resolved from source.
      *)          continue ;;
    esac
    while IFS= read -r t; do
      [ -z "$t" ] && continue
      XAML_REFS=$((XAML_REFS + 1))
      grep -qxF "$t" "$expected" && continue
      grep -qxF "${t}Extension" "$expected" && continue
      echo "FAIL $f: '$p:$t' must resolve in $where but $t is not defined there."
      if [ -z "$asm" ]; then
        echo "    If it moved to CCP.Core, add ';assembly=CCP.Core' to that xmlns - otherwise the app throws XamlParseException at runtime."
      else
        echo "    The xmlns claims CCP.Core but the type is not there - drop ';assembly=CCP.Core' or move the type."
      fi
      FAIL=1
    done < <(grep -oE "\b${p}:[A-Za-z_][A-Za-z0-9_]*" "$WORK/x.xaml" | sed "s/^${p}://" | sort -u)
  done < "$WORK/prefixes.txt"
done < <(find ConditioningControlPanel -name '*.xaml' -not -path '*/obj/*' -not -path '*/bin/*')

[ "$XAML_REFS" -gt 0 ] || { echo "FATAL: matched 0 XAML type references - the extraction is broken"; exit 1; }
echo "checked $XAML_REFS XAML type references against $APP_TYPES app types / $(wc -l < "$WORK/core.txt") Core types"

echo
[ "$FAIL" -eq 0 ] && echo "All Core guards passed." || echo "Core guards FAILED."
exit "$FAIL"
