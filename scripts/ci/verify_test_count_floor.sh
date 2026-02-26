#!/usr/bin/env bash
set -euo pipefail

mode="verify"
baseline_file="docs/ci/test_count_baseline.json"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --mode)
      mode="$2"
      shift 2
      ;;
    --baseline)
      baseline_file="$2"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

projects=(
  "Game.Core.Tests:tests/Game.Core.Tests/Game.Core.Tests.csproj"
  "Game.Server.Tests:tests/Game.Server.Tests/Game.Server.Tests.csproj"
)

count_tests() {
  local csproj="$1"
  dotnet test "$csproj" -c Release --no-build --list-tests | awk '
    BEGIN { in_list = 0; count = 0 }
    /^The following Tests are available:/ { in_list = 1; next }
    {
      if (in_list == 1) {
        if ($0 ~ /^[[:space:]]+\S/) {
          count++
          next
        }
        if ($0 !~ /^[[:space:]]*$/) {
          in_list = 0
        }
      }
    }
    END { print count }
  '
}

core_count=0
server_count=0

for entry in "${projects[@]}"; do
  name="${entry%%:*}"
  csproj="${entry#*:}"
  count="$(count_tests "$csproj")"
  echo "Discovered ${name}: ${count}"

  if [[ "$name" == "Game.Core.Tests" ]]; then
    core_count="$count"
  elif [[ "$name" == "Game.Server.Tests" ]]; then
    server_count="$count"
  fi
done

total_count=$((core_count + server_count))

echo "Discovered total: ${total_count}"

if [[ "$mode" == "report" ]]; then
  cat <<REPORT

Copy these values into ${baseline_file}:
{
  "Game.Core.Tests": ${core_count},
  "Game.Server.Tests": ${server_count},
  "minTotal": ${total_count}
}
REPORT
  exit 0
fi

if [[ ! -f "$baseline_file" ]]; then
  echo "Baseline file not found: ${baseline_file}" >&2
  exit 1
fi

read_baseline_value() {
  local key="$1"
  python - "$baseline_file" "$key" <<'PY'
import json
import sys
from pathlib import Path

path = Path(sys.argv[1])
key = sys.argv[2]
obj = json.loads(path.read_text(encoding="utf-8"))
value = obj.get(key)
if not isinstance(value, int):
    raise SystemExit(f"Baseline value for '{key}' must be an integer.")
print(value)
PY
}

baseline_core="$(read_baseline_value "Game.Core.Tests")"
baseline_server="$(read_baseline_value "Game.Server.Tests")"
baseline_total="$(read_baseline_value "minTotal")"

if (( baseline_core <= 0 || baseline_server <= 0 || baseline_total <= 0 )); then
  echo "Baseline has unset placeholder values (<= 0)."
  echo "Run this workflow with workflow_dispatch to print current discovered counts, then update ${baseline_file}."
  exit 0
fi

fail=0
if (( core_count < baseline_core )); then
  echo "ERROR: Game.Core.Tests discovered count (${core_count}) is below baseline floor (${baseline_core})." >&2
  fail=1
fi
if (( server_count < baseline_server )); then
  echo "ERROR: Game.Server.Tests discovered count (${server_count}) is below baseline floor (${baseline_server})." >&2
  fail=1
fi
if (( total_count < baseline_total )); then
  echo "ERROR: Total discovered tests (${total_count}) is below baseline floor (${baseline_total})." >&2
  fail=1
fi

if (( fail != 0 )); then
  exit 1
fi

echo "Test count floor guard passed."
