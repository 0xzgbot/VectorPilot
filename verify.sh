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
# MSB* (file locks, copy failures) are build failures too — not just CS/MC.
ERRORS="$(printf '%s' "$BUILD_OUT" | grep -cE 'error (CS|MC|MSB)[0-9]+')"
WARNINGS="$(printf '%s' "$BUILD_OUT" | grep -oE '^ +[0-9]+ Warning\(s\)' | grep -oE '[0-9]+' | head -1)"
echo "errors:    ${ERRORS}"
echo "warnings:  ${WARNINGS:-0}"
if [ "$ERRORS" -ne 0 ]; then
  printf '%s\n' "$BUILD_OUT" | grep -E 'error (CS|MC|MSB)[0-9]+' | sort -u | head -10
  echo "HINT: MSB302x means a running VectorPilot.exe is holding the DLLs — kill it first."
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
  FSUM="$(printf '%s\n' "$FOCUS" | grep -E '^(Passed|Failed)!' | head -1)"
  echo "${FSUM:-<no test summary — focused run did not execute>}"
  [ -z "$FSUM" ] && FAIL=1
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
SUMMARY="$(printf '%s\n' "$TEST_OUT" | grep -E '^(Passed|Failed)!' | head -1)"
echo "${SUMMARY:-<no test summary — suite did not run>}"
# No summary at all means the suite never ran (build break, lock, crash).
if [ -z "$SUMMARY" ]; then
  printf '%s\n' "$TEST_OUT" | grep -E 'error (CS|MC|MSB)[0-9]+' | sort -u | head -5
  FAIL=1
fi
if printf '%s' "$TEST_OUT" | grep -qE '^Failed!'; then
  FAIL=1
  printf '%s\n' "$TEST_OUT" | grep -A6 '\[FAIL\]' | head -40
fi

# A PARTIAL run is the dangerous green: "Passed! 1321" with no failures still printed
# VERIFY PASS, so a killed testhost or an aborted run looked like success. Bump MIN_TESTS
# as the suite grows; a stale floor is a weaker guard, not a wrong one.
MIN_TESTS=1400
TOTAL="$(printf '%s' "$SUMMARY" | sed -n 's/.*Total: *\([0-9]*\).*/\1/p')"
if [ -n "$TOTAL" ] && [ "$TOTAL" -lt "$MIN_TESTS" ]; then
  echo "PARTIAL RUN: only $TOTAL of >=$MIN_TESTS tests ran — killed testhost? Rebuild and re-run."
  FAIL=1
fi

echo
if [ "$FAIL" -eq 0 ]; then
  echo "VERIFY PASS"
else
  echo "VERIFY FAIL"
fi
exit "$FAIL"
