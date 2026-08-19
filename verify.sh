#!/usr/bin/env bash
# VectorPilot canonical verification gate.
#
# THE single command that proves the repo is good. Any agent (or human) runs
# this instead of hand-rolling a throwaway script. Exit 0 = shippable.
#
#   ./verify.sh              full gate (build + warnings + full suite)
#   ./verify.sh <filter>     also run a focused xUnit filter for a change set
#
# Example: ./verify.sh "FullyQualifiedName~CanvasEditing"
set -uo pipefail

export PATH="$PATH:/c/Program Files/dotnet"
cd "$(dirname "$0")" || exit 1

FILTER="${1:-}"
FAIL=0

echo "=== workspace ==="
echo "branch:    $(git rev-parse --abbrev-ref HEAD)"
echo "HEAD:      $(git rev-parse --short HEAD)"
echo "pushed:    $(git rev-parse --short '@{u}' 2>/dev/null || echo 'no upstream')"
DIRTY="$(git status --porcelain | head -5)"
echo "dirty:     [${DIRTY:-clean}]"

echo
echo "=== build (Release — strictest, matches CI) ==="
BUILD_OUT="$(dotnet build VectorPilot.sln -c Release 2>&1)"
ERRORS="$(printf '%s' "$BUILD_OUT" | grep -cE 'error (CS|MC)[0-9]+')"
WARNINGS="$(printf '%s' "$BUILD_OUT" | grep -oE '^ +[0-9]+ Warning\(s\)' | grep -oE '[0-9]+' | head -1)"
echo "errors:    ${ERRORS}"
echo "warnings:  ${WARNINGS:-0}"
if [ "$ERRORS" -ne 0 ]; then
  printf '%s\n' "$BUILD_OUT" | grep -E 'error (CS|MC)[0-9]+' | sort -u | head -10
  FAIL=1
fi
if [ "${WARNINGS:-0}" -ne 0 ]; then
  echo "WARN: zero-warning policy violated"
  printf '%s\n' "$BUILD_OUT" | grep -E 'warning [A-Z]+[0-9]+' | sort -u | head -10
  FAIL=1
fi

if [ -n "$FILTER" ]; then
  echo
  echo "=== focused: $FILTER ==="
  FOCUS="$(dotnet test VectorPilot.sln -c Debug --filter "$FILTER" 2>&1)"
  printf '%s\n' "$FOCUS" | grep -E '^(Passed|Failed)!' | head -1
  printf '%s' "$FOCUS" | grep -qE '^Failed!' && FAIL=1
  # A filter that matches nothing is a typo, not a pass.
  if printf '%s' "$FOCUS" | grep -qE 'No test matches|Passed: +0,'; then
    echo "ERROR: filter matched no tests"
    FAIL=1
  fi
  printf '%s\n' "$FOCUS" | grep -A5 '\[FAIL\]' | head -20
fi

echo
echo "=== full suite ==="
TEST_OUT="$(dotnet test VectorPilot.sln -c Debug 2>&1)"
printf '%s\n' "$TEST_OUT" | grep -E '^(Passed|Failed)!' | head -1
if printf '%s' "$TEST_OUT" | grep -qE '^Failed!'; then
  FAIL=1
  printf '%s\n' "$TEST_OUT" | grep -A6 '\[FAIL\]' | head -40
fi

echo
if [ "$FAIL" -eq 0 ]; then
  echo "VERIFY PASS"
else
  echo "VERIFY FAIL"
fi
exit "$FAIL"
