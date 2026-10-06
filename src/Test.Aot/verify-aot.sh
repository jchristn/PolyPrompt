#!/usr/bin/env bash
# Verifies PolyPrompt under Native AOT for each target framework:
#   1. runs Test.Aot on the JIT with reflection enabled (the baseline),
#   2. publishes Test.Aot as a native binary (every trim and AOT warning is an error) and runs it,
#   3. checks both runs passed and sent byte-identical request bodies.
# Usage: ./verify-aot.sh [rid] [framework...]   e.g. ./verify-aot.sh osx-arm64 net10.0
set -euo pipefail

cd "$(dirname "$0")"
RID="${1:-$(dotnet --info | awk -F': *' '/^ *RID:/ {print $2; exit}')}"
shift || true
FRAMEWORKS=("${@:-net8.0 net10.0}")
FRAMEWORKS=(${FRAMEWORKS[@]})
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT

for TFM in "${FRAMEWORKS[@]}"; do
  echo "=== $TFM ($RID) ==="

  echo "--- JIT with reflection"
  dotnet run -c Release -f "$TFM" -p:PublishAot=false -- --digest-out "$OUT/jit-$TFM.txt" | grep -v -E ' (Debug|Info|Warn) \['

  echo "--- Native AOT publish"
  dotnet publish -c Release -f "$TFM" -r "$RID" -o "$OUT/native-$TFM" -nologo -v quiet

  echo "--- Native AOT run"
  "$OUT/native-$TFM/Test.Aot" --digest-out "$OUT/aot-$TFM.txt" | grep -v -E ' (Debug|Info|Warn) \['

  if [[ "$(cat "$OUT/jit-$TFM.txt")" != "$(cat "$OUT/aot-$TFM.txt")" ]]; then
    echo "Request digests differ between the JIT and Native AOT runs for $TFM." >&2
    exit 1
  fi

  echo "$TFM: native binary $(du -h "$OUT/native-$TFM/Test.Aot" | cut -f1), request digests match."
done

echo "Native AOT verification passed."
