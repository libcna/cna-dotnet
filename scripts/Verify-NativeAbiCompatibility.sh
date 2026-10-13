#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd -- "$script_dir/.." && pwd)
dotnet_command=${DOTNET_COMMAND:-dotnet}
configuration=Release
native_library=
output_root=
no_build=0
update_fixture_symbols=0

while (($# > 0)); do
  case "$1" in
    --configuration)
      configuration=${2:?--configuration requires a value}
      shift 2
      ;;
    --native-library)
      native_library=${2:?--native-library requires a path}
      shift 2
      ;;
    --output)
      output_root=${2:?--output requires a path}
      shift 2
      ;;
    --no-build)
      no_build=1
      shift
      ;;
    --update-fixture-symbols)
      update_fixture_symbols=1
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

# CNA plan_apple_m4.md AM4-216: the fixtures are ordinary shared libraries, so the gate runs wherever
# one can be compiled and loaded -- Linux (`.so`) and macOS (`.dylib`, which the resolver loads by
# path just the same).
case "$(uname -s)" in
  Linux) fixture_suffix=so ;;
  Darwin) fixture_suffix=dylib ;;
  *)
    echo "The shared-library fixture gate runs on Linux and macOS; this host is $(uname -s)." >&2
    exit 2
    ;;
esac

cleanup_output=0
if [[ -z "$output_root" ]]; then
  output_root=$(mktemp -d)
  cleanup_output=1
else
  # Not `realpath -m`: macOS's realpath has no -m, and creating the directory first makes the
  # plain resolution valid everywhere.
  mkdir -p "$output_root"
  output_root=$(cd -- "$output_root" && pwd -P)
fi
trap 'if [[ "$cleanup_output" == 1 ]]; then rm -rf "$output_root"; fi' EXIT

fixtures_root="$output_root/fixtures"
logs_root="$output_root/logs"
mkdir -p "$fixtures_root" "$logs_root"

probe_project="$repo_root/tools/native-abi-probe/CNA.NativeAbiProbe.csproj"
if [[ "$no_build" == 0 ]]; then
  "$dotnet_command" build "$probe_project" -c "$configuration" -m:1
fi
probe_dll="$repo_root/tools/native-abi-probe/bin/$configuration/net8.0/CNA.NativeAbiProbe.dll"
if [[ ! -f "$probe_dll" ]]; then
  echo "Native ABI probe was not built: $probe_dll" >&2
  exit 2
fi

# The four names excluded here are not exemptions: the fixture handles them specially -- they are
# the routes the probe itself calls -- so they are declared by hand in cna_abi_fixture.c rather than
# stubbed from this list, and added back to the count reported at the end.
extract_declared_symbols()
{
  perl -0777 -ne '
    while (/internal\s+static\s+(?:unsafe\s+)?partial\s+\S+\s+(cna_[a-z0-9_]+)\s*\(/g) { $symbols{$1}=1 }
    END { print join("\n", grep { $_ ne "cna_get_abi_version" &&
                                  $_ ne "cna_error_get_last_message_size" &&
                                  $_ ne "cna_touch_capabilities_init" &&
                                  $_ ne "cna_game_destroy" } sort keys %symbols), "\n" }
  ' "$repo_root"/src/CNA.Interop/Native*.cs
}

declared_symbols=$(extract_declared_symbols)

if [[ "$update_fixture_symbols" == 1 ]]; then
  printf 'CNA_ABI_FIXTURE_STUB(%s)\n' $declared_symbols > "$script_dir/abi-fixtures/required_symbols.inc"
  echo "Regenerated $(basename "$script_dir")/abi-fixtures/required_symbols.inc from CNA.Interop.Native."
fi

fixture_symbols=$(sed -n 's/^CNA_ABI_FIXTURE_STUB(\(cna_[a-z0-9_]*\))$/\1/p' \
  "$script_dir/abi-fixtures/required_symbols.inc")
if [[ "$declared_symbols" != "$fixture_symbols" ]]; then
  # Saying how to fix it, because the fix is a perl one-liner nobody should retype. The check itself
  # stays a check: --update-fixture-symbols is a deliberate act, not something a failing run does on
  # the caller's behalf.
  echo "The native ABI fixture symbol inventory is stale relative to CNA.Interop.Native." >&2
  echo "Regenerate it with: scripts/Verify-NativeAbiCompatibility.sh --update-fixture-symbols" >&2
  diff <(printf '%s\n' "$fixture_symbols") <(printf '%s\n' "$declared_symbols") \
    | sed -n '1,20p' >&2 || true
  exit 1
fi

compile_fixture()
{
  local name=$1
  shift
  cc -std=c11 -shared -fPIC -Wall -Wextra -Werror \
    -I "$script_dir/abi-fixtures" "$@" \
    "$script_dir/abi-fixtures/cna_abi_fixture.c" -o "$fixtures_root/$name.$fixture_suffix"
}

run_accept()
{
  local name=$1
  if ! env -u CNA_NATIVE_DIR CNA_NATIVE_LIBRARY="$fixtures_root/$name.$fixture_suffix" \
      "$dotnet_command" "$probe_dll" >"$logs_root/$name.log" 2>&1; then
    echo "ABI fixture '$name' was rejected but should be accepted." >&2
    sed -n '1,100p' "$logs_root/$name.log" >&2
    exit 1
  fi
  grep -Fq 'CNA_ABI_PROBE_STATUS=accepted' "$logs_root/$name.log"
}

run_reject()
{
  local name=$1
  local diagnostic=$2
  if env -u CNA_NATIVE_DIR CNA_NATIVE_LIBRARY="$fixtures_root/$name.$fixture_suffix" \
      "$dotnet_command" "$probe_dll" >"$logs_root/$name.log" 2>&1; then
    echo "ABI fixture '$name' was accepted but should be rejected." >&2
    exit 1
  fi
  grep -Fq 'CNA_ABI_PROBE_STATUS=rejected' "$logs_root/$name.log"
  grep -Fq "$diagnostic" "$logs_root/$name.log"
}

compile_fixture exact-0.47.0 -DCNA_ABI_FIXTURE_VERSION=0x00002F00U
compile_fixture exact-0.47.0-extra-symbol -DCNA_ABI_FIXTURE_VERSION=0x00002F00U \
  -DCNA_ABI_FIXTURE_EXTRA_SYMBOL
compile_fixture retired-0.8.0 -DCNA_ABI_FIXTURE_VERSION=0x00000800U
compile_fixture retired-0.21.0 -DCNA_ABI_FIXTURE_VERSION=0x00001500U
compile_fixture retired-0.44.0 -DCNA_ABI_FIXTURE_VERSION=0x00002C00U
compile_fixture skipped-0.45.0 -DCNA_ABI_FIXTURE_VERSION=0x00002D00U
compile_fixture retired-0.46.0 -DCNA_ABI_FIXTURE_VERSION=0x00002E00U
compile_fixture unreviewed-0.48.0 -DCNA_ABI_FIXTURE_VERSION=0x00003000U
compile_fixture missing-required-symbol -DCNA_ABI_FIXTURE_VERSION=0x00002F00U \
  -DCNA_ABI_FIXTURE_MISSING_REQUIRED_SYMBOL
compile_fixture changed-required-signature -DCNA_ABI_FIXTURE_VERSION=0x00002F00U \
  -DCNA_ABI_FIXTURE_CHANGED_SIGNATURE
compile_fixture incompatible-major-1.0.0 -DCNA_ABI_FIXTURE_VERSION=0x00010000U
compile_fixture structurally-incompatible-0.47.0 -DCNA_ABI_FIXTURE_VERSION=0x00002F00U \
  -DCNA_ABI_FIXTURE_INCOMPATIBLE_STRUCT
compile_fixture malformed-metadata-0.0.0 -DCNA_ABI_FIXTURE_VERSION=0x00000000U
compile_fixture unreadable-metadata -DCNA_ABI_FIXTURE_UNREADABLE_METADATA

run_accept exact-0.47.0
run_accept exact-0.47.0-extra-symbol
# A generation this consumer used to accept long ago. It is refused on the version rule alone,
# before the routes it does not export are ever looked for -- which is the point: retiring a matrix
# entry has to be enforced, not merely written down.
run_reject retired-0.8.0 'experimental ABI 0.8.0 is not in the audited compatibility matrix'
# 0.21.0 was retired by the 0.35.0 admission, and it stays the strongest of the retirement controls
# for routes removed: every route this consumer imported then still exists with the same prototype.
run_reject retired-0.21.0 'experimental ABI 0.21.0 is not in the audited compatibility matrix'
# 0.44.0 was retired by the 0.46.0 admission: every route this consumer imports is unchanged since,
# so only the version rule refuses it, exactly the case a floor-based policy would get wrong.
run_reject retired-0.44.0 'experimental ABI 0.44.0 is not in the audited compatibility matrix'
# A generation this consumer crossed without ever admitting it.
run_reject skipped-0.45.0 'experimental ABI 0.45.0 is not in the audited compatibility matrix'
# 0.46.0 is the generation retired by this admission, the neighbour below. Every route this consumer
# imports is 0.47.0's -- the step removed the modern GPU layer, which this consumer no longer names.
run_reject retired-0.46.0 'experimental ABI 0.46.0 is not in the audited compatibility matrix'
# The neighbour above: newer is not evidence of anything while the major is zero.
run_reject unreviewed-0.48.0 'experimental ABI 0.48.0 is not in the audited compatibility matrix'
run_reject missing-required-symbol "required symbol 'cna_game_destroy' is missing"
run_reject changed-required-signature "failed required signature/shape probe 'cna_error_get_last_message_size'"
run_reject incompatible-major-1.0.0 'major 1 differs from consumer major 0'
run_reject structurally-incompatible-0.47.0 "failed required signature/shape probe 'cna_touch_capabilities_init'"
run_reject malformed-metadata-0.0.0 'metadata encodes 0.0.0'
run_reject unreadable-metadata "required symbol 'cna_get_abi_version' is missing"

selected_native_status=not-run
if [[ -n "$native_library" ]]; then
  native_library=$(realpath "$native_library")
  if ! env -u CNA_NATIVE_DIR CNA_NATIVE_LIBRARY="$native_library" \
      "$dotnet_command" "$probe_dll" >"$logs_root/selected-native.log" 2>&1; then
    echo "The selected CNA native library failed the ABI contract." >&2
    sed -n '1,100p' "$logs_root/selected-native.log" >&2
    exit 1
  fi
  selected_native_status=passed
fi

# Counted rather than written down, for the reason recorded below the stdout line: this field said
# 854 while the resolver had long since grown past it, so the report stated a number nobody had
# measured. It is now the same expression the stdout line uses.
required_symbol_count=$(( $(printf '%s\n' "$declared_symbols" | grep -c .) + 4 ))

jq -n \
  --arg selectedNative "$selected_native_status" \
  --argjson requiredSymbolCount "$required_symbol_count" \
  '{
    schemaVersion: 1,
    policyVersion: "cna-cs-native-abi/1",
    status: "passed",
    consumerAbi: "0.47.0",
    requiredSymbolCount: $requiredSymbolCount,
    accepted: ["exact-0.47.0", "exact-0.47.0-extra-symbol"],
    rejected: ["retired-0.8.0", "retired-0.21.0", "retired-0.44.0", "skipped-0.45.0", "retired-0.46.0", "unreviewed-0.48.0", "missing-required-symbol", "changed-required-signature", "incompatible-major-1.0.0", "structurally-incompatible-0.47.0", "malformed-metadata-0.0.0", "unreadable-metadata"],
    selectedNative: $selectedNative
  }' >"$output_root/abi-compatibility-report.json"

echo "CNA_ABI_POLICY=cna-cs-native-abi/1"
# Counted, not written down. The literal that used to sit here said 854 while the resolver had long
# since grown past it, and a report that states a number it did not measure is worse than one that
# omits it. The four names excluded from the fixture inventory above are still required by the
# resolver -- they are handled specially by the fixture, not exempted -- so they are added back.
echo "CNA_ABI_REQUIRED_SYMBOLS=$(( $(printf '%s\n' "$declared_symbols" | grep -c .) + 4 ))"
echo "CNA_ABI_FIXTURES_ACCEPTED=2"
echo "CNA_ABI_FIXTURES_REJECTED=12"
echo "CNA_ABI_SELECTED_NATIVE=$selected_native_status"
echo "CNA_ABI_COMPATIBILITY_STATUS=passed"
