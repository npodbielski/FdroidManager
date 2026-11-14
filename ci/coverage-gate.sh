#!/usr/bin/env bash
# Coverage gate + per-class gap table;
# fails the job below the 95% line-coverage floor.
#
#   usage: bash ci/coverage-gate.sh [results-dir]     # default: ./test-results
#
# Reads every coverage.cobertura.*.xml in the directory (the unit and the
# component run each write one) and merges them: a source line counts as
# covered when any run hit it.
#
# Exits 0 when the merged line-rate is at or above GATE, 1 below.
set -euo pipefail
export LC_ALL=C

GATE=0.95
RESULTS_DIR="${1:-./test-results}"

FILES=""
for f in "$RESULTS_DIR"/coverage.cobertura.*.xml; do
  [ -e "$f" ] || continue
  FILES="$FILES $f"
done
if [ -z "$FILES" ]; then
  echo "[coverage] no cobertura XML found in $RESULTS_DIR" >&2
  exit 1
fi

# One awk pass over all part files. Cobertura writes each element on its own
# line; a source line is keyed file|number so the runs union instead of
# double-count. Classes accumulate the lines of their own block; a line first
# seen under a class stays attributed there in every run.
OUT=$(cat $FILES | sed 's|</class>|</class>\n|g' | awk '
  /<file name=/ { fn = $0; sub(/.*<file name="/, "", fn); sub(/".*/, "", fn) }
  /<class name=/ { cn = $0; sub(/.*<class name="/, "", cn); sub(/".*/, "", cn); sub(/^.*\./, "", cn); incls = 1 }
  incls && /<line / {
    h = "0"
    if (match($0, /hits="[0-9]+"/)) { h = substr($0, RSTART + 6, RLENGTH - 7) }
    n = ""
    if (match($0, /number="[0-9]+"/)) { n = substr($0, RSTART + 8, RLENGTH - 9) }
    k = fn "|" n
    if (!(k in seen)) { seen[k] = cn; tot[cn]++ }
    if (h + 0 > 0) { cov[k] = 1 }
  }
  /<\/class>/ { incls = 0 }
  END {
    t = 0; c = 0
    for (k in seen) {
      t++
      if (k in cov) { c++ } else { miss[seen[k]]++ }
    }
    printf "RATE %.6f\n", (t ? c / t : 0)
    for (name in miss) { printf "GAP %d %d %s\n", miss[name], tot[name], name }
  }
')

RATE=$(printf '%s\n' "$OUT" | awk '$1 == "RATE" { print $2 }')
RC=0
awk -v r="$RATE" -v gate="$GATE" 'BEGIN {
  if (r + 0 >= gate) { printf "[coverage] line-rate %s (gate: %s) ok\n", r, gate; exit 0 }
  printf "[coverage] line-rate %s (gate: %s) BELOW GATE\n", r, gate; exit 1
}' || RC=$?

# Feeds GitLab's coverage widget (the `coverage:` regex matches this line).
awk -v r="$RATE" 'BEGIN { printf "Total | %.2f%%\n", r * 100 }'

echo "[coverage top gaps by missed lines]"
printf '%s\n' "$OUT" | awk '$1 == "GAP" { printf "%d\t%d\t%s\n", $2, $3, $4 }' \
  | sort -rn | head -15 | awk -F'\t' '{ printf "  %-30s missed %4d/%4d\n", $3, $1, $2 }'

exit $RC
