#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_command="${DOTNET_COMMAND:-dotnet}"
output_directory="$repository_root/artifacts/cna-snapshots"
force=0
pure_only=0

while (($#)); do
  case "$1" in
    --output)
      # Not `realpath -m`, which macOS's realpath lacks (CNA plans/plan_apple_m4.md AM4-225): the
      # directory may not exist yet, so an absolute path is made by hand.
      case "$2" in
        /*) output_directory="${2%/}" ;;
        *) output_directory="$PWD/${2%/}" ;;
      esac
      shift 2
      ;;
    --force)
      force=1
      shift
      ;;
    --pure-only)
      pure_only=1
      shift
      ;;
    --help|-h)
      echo "Usage: $0 [--output directory] [--force] [--pure-only]"
      echo "Full capture requires CNA_NATIVE_LIBRARY or CNA_NATIVE_DIR and a usable display."
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

if [[ -e "$output_directory" ]]; then
  if [[ "$force" != 1 ]]; then
    echo "Output directory already exists; pass --force to replace it: $output_directory" >&2
    exit 2
  fi
  case "$output_directory" in
    /|"$repository_root"|"$(dirname "$repository_root")")
      echo "Refusing unsafe output directory: $output_directory" >&2
      exit 2
      ;;
  esac
  rm -rf -- "$output_directory"
fi

capture_root="$(mktemp -d)"
trap 'rm -rf -- "$capture_root"' EXIT
mkdir -p "$capture_root/output"

# DOTNET_CLI_HOME also moves NuGet's global package folder, and the builds below restore: without
# the first line every project's obj/project.assets.json would name a folder the trap deletes, and
# the next --no-restore build in this checkout -- Package-Acceptance.sh's -- fails with NETSDK1064.
export NUGET_PACKAGES="${NUGET_PACKAGES:-${DOTNET_CLI_HOME:-$HOME}/.nuget/packages}"
export DOTNET_CLI_HOME="$capture_root/dotnet-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

behavior_tool="$repository_root/tools/behavior-corpus/CNA.BehaviorCorpus.csproj"

manifest_value() {
  "$dotnet_command" run --project "$behavior_tool" -c Release --no-build -- get "$@"
}

# A probe writes its observations to a named file rather than to standard output, because it
# shares standard output with the native library loaded underneath it and some CNA renderers
# write there: measured, the graphics probe redirected to a file produced 166 lines under
# OPENGLES3 and 167 under Vulkan, whose backend prints a capability banner at device creation,
# and the corpus validator rejected the second as a count mismatch. Naming the file makes a
# snapshot depend on the probe alone.
run_probe() {
  local probe_id="$1"
  local project="$2"
  local snapshot_file="$3"
  local raw_file="$capture_root/$probe_id.raw.txt"

  # `${runner[@]+...}`: bash 3.2 (macOS's /bin/bash) treats an empty array as unset under set -u.
  ${runner[@]+"${runner[@]}"} "$dotnet_command" run --project "$project" -c Release --no-build -- \
    --output "$raw_file"
  "$dotnet_command" run --project "$behavior_tool" -c Release --no-build -- \
    validate --probe "$probe_id" --input "$raw_file" \
    --output "$capture_root/output/$snapshot_file"
}

runner=()
if [[ -z "${DISPLAY:-}" ]] && command -v xvfb-run >/dev/null 2>&1; then
  runner=(xvfb-run -a)
fi

cd "$repository_root"
"$dotnet_command" build "$behavior_tool" -c Release -m:1
compile_project="$(manifest_value --probe compile --field sourceProject)"
compile_snapshot="$(manifest_value --probe compile --field expectedSnapshotFilename)"
graphics_project="$(manifest_value --probe graphics --field sourceProject)"
graphics_snapshot="$(manifest_value --probe graphics --field expectedSnapshotFilename)"
runtime_project="$(manifest_value --probe runtime --field sourceProject)"
runtime_snapshot="$(manifest_value --probe runtime --field expectedSnapshotFilename)"
combined_snapshot="$(manifest_value --field candidateCombinedSnapshotFilename)"

"$dotnet_command" build "$compile_project" -c Release -m:1
run_probe compile \
  "$compile_project" \
  "$compile_snapshot"

if [[ "$pure_only" == 0 ]]; then
  if [[ -z "${CNA_NATIVE_LIBRARY:-}" && -z "${CNA_NATIVE_DIR:-}" ]]; then
    echo "Set CNA_NATIVE_LIBRARY or CNA_NATIVE_DIR for a full CNA snapshot." >&2
    exit 2
  fi

  "$dotnet_command" build "$graphics_project" -c Release -m:1
  "$dotnet_command" build "$runtime_project" -c Release -m:1
  export XNA_GRAPHICS_PROBE_DRAW_VALIDATION=1
  export XNA_GRAPHICS_PROBE_DESTRUCTIVE_LIFECYCLE=1
  export XNA_GRAPHICS_PROBE_UNSAFE_CONSTRUCTORS=1
  export SDL_AUDIODRIVER="${SDL_AUDIODRIVER:-dummy}"
  export XDG_DATA_HOME="$capture_root/xdg-data"

  run_probe graphics "$graphics_project" "$graphics_snapshot"

  run_probe runtime "$runtime_project" "$runtime_snapshot"

  "$dotnet_command" run --project "$behavior_tool" -c Release --no-build -- combine \
    --input "compile=$capture_root/output/$compile_snapshot" \
    --input "graphics=$capture_root/output/$graphics_snapshot" \
    --input "runtime=$capture_root/output/$runtime_snapshot" \
    --output "$capture_root/output/$combined_snapshot"
fi

mkdir -p "$(dirname "$output_directory")"
mv "$capture_root/output" "$output_directory"
echo "CNA snapshot captured in $output_directory"
