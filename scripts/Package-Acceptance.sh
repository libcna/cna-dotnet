#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd -- "$script_dir/.." && pwd)
template_root=${CNA_TEMPLATE_ROOT:-"$repo_root/../cna-dotnet-template"}
dotnet_command=${DOTNET_COMMAND:-dotnet}
native_directory=${CNA_ACCEPTANCE_NATIVE_DIRECTORY:-}
package_version=${CNA_PACKAGE_VERSION:-0.1.0-local.1}
output_root=

while (($# > 0)); do
  case "$1" in
    --native-directory)
      native_directory=${2:?--native-directory requires a path}
      shift 2
      ;;
    --package-version)
      package_version=${2:?--package-version requires a value}
      shift 2
      ;;
    --output)
      output_root=${2:?--output requires a path}
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

# The experiment is evidence-scoped to one RID per host: linux-x64, and since CNA
# plans/plan_apple_m4.md AM4-227 osx-arm64. Everything that differs between them is decided here --
# the library suffix and SDL soname the package carries, how the library says it looks beside
# itself, the video driver a windowless run uses and how a wrong-architecture library is made.
case "$(uname -s)/$(uname -m)" in
  Linux/x86_64)
    rid=linux-x64
    library_suffix=so
    sdl_library=libSDL3.so.0
    windowless_video_driver=offscreen
    wrong_architecture_flags=(-m32 -nostdlib)
    ;;
  Darwin/arm64)
    rid=osx-arm64
    library_suffix=dylib
    sdl_library=libSDL3.0.dylib
    # SDL's offscreen driver creates its contexts through EGL, which macOS does not have.
    windowless_video_driver=dummy
    wrong_architecture_flags=(-arch x86_64)
    ;;
  *)
    echo "This native-package experiment is evidence-scoped to linux-x64 and osx-arm64; this host is $(uname -s)/$(uname -m)." >&2
    exit 2
    ;;
esac
# The installed CNACApi component, not a build tree: `cmake --install <tree> --component CNACApi
# --prefix <dir>` and pass <dir>/lib. A build-tree library carries an absolute RUNPATH into that
# tree, so a package made from it loads only on the machine that built it.
if [[ -z "$native_directory" || ! -f "$native_directory/libcna_c_api.$library_suffix" ]]; then
  echo "Pass --native-directory with the lib directory of an installed, ABI-matched $rid CNACApi component." >&2
  exit 2
fi
native_directory=$(cd -- "$native_directory" && pwd -P)
native_library="$native_directory/libcna_c_api.$library_suffix"
if [[ "$library_suffix" == so ]]; then
  native_runpath=$(readelf -d "$native_library" | sed -n 's/.*(RUNPATH).*\[\(.*\)\]/\1/p')
  expected_runpath='$ORIGIN'
else
  native_runpath=$(otool -l "$native_library" | awk '/LC_RPATH/ { getline; getline; print $2 }')
  expected_runpath='@loader_path'
fi
if [[ "$native_runpath" != "$expected_runpath" ]]; then
  echo "The native library's run path is '$native_runpath', not $expected_runpath; install the CNACApi component instead of packing a build tree." >&2
  exit 2
fi
if [[ ! -f "$template_root/.template.config/template.json" ]]; then
  echo "CNA template checkout was not found at: $template_root" >&2
  exit 2
fi

cleanup_output=0
if [[ -z "$output_root" ]]; then
  # Never /tmp: this is the shared consumer-fixture directory, kept until the next run replaces it.
  output_root="$repo_root/build-consumer/package-acceptance"
  rm -rf "$output_root"
  mkdir -p "$output_root"
else
  # Not `realpath -m`, which macOS's realpath lacks: the directory does not exist yet.
  case "$output_root" in
    /*) output_root="${output_root%/}" ;;
    *) output_root="$PWD/${output_root%/}" ;;
  esac
  if [[ -e "$output_root" ]]; then
    echo "Acceptance output already exists: $output_root" >&2
    exit 2
  fi
  mkdir -p "$output_root"
fi

work_root="$output_root/work"
feed_root="$output_root/feed"
logs_root="$output_root/logs"
mkdir -p "$work_root" "$feed_root" "$logs_root"
trap 'if [[ "$cleanup_output" == 1 ]]; then rm -rf "$output_root"; fi' EXIT

export DOTNET_CLI_HOME="$work_root/dotnet-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_AUDIT_MODE=direct

"$dotnet_command" clean "$repo_root/CNA.sln" -c Release -m:1 -v:quiet
"$dotnet_command" build "$repo_root/CNA.sln" -c Release --no-restore -m:1

abi_compatibility_root="$work_root/abi-compatibility"
DOTNET_COMMAND="$dotnet_command" "$script_dir/Verify-NativeAbiCompatibility.sh" \
  --configuration Release --native-library "$native_library" --output "$abi_compatibility_root"

pack_project()
{
  local project=$1
  shift
  "$dotnet_command" pack "$repo_root/$project" -c Release --no-restore -m:1 \
    -p:CnaPackageAcceptance=true -p:CnaPackageVersion="$package_version" \
    -o "$feed_root" "$@"
}

pack_project src/CNA.Interop/CNA.Interop.csproj \
  -p:CnaNativeRid="$rid" -p:CnaNativeDirectory="$native_directory"
pack_project src/CNA.Framework/CNA.Framework.csproj
pack_project src/CNA.XnaCompat/CNA.XnaCompat.csproj

for package_id in CNA.Interop CNA.Framework CNA.XnaCompat; do
  package="$feed_root/$package_id.$package_version.nupkg"
  symbols="$feed_root/$package_id.$package_version.snupkg"
  [[ -f "$package" && -f "$symbols" ]] || { echo "Missing package output for $package_id." >&2; exit 1; }
  entries=$(unzip -Z1 "$package")
  for required in "lib/net8.0/$package_id.dll" "lib/net8.0/$package_id.xml" LICENSE NOTICE.md README.md; do
    if ! grep -Fxq "$required" <<<"$entries"; then
      echo "$package_id package is missing $required." >&2
      exit 1
    fi
  done
  if ! unzip -Z1 "$symbols" | grep -Fxq "lib/net8.0/$package_id.pdb"; then
    echo "$package_id symbol package is missing its portable PDB." >&2
    exit 1
  fi
done

interop_entries=$(unzip -Z1 "$feed_root/CNA.Interop.$package_version.nupkg")
for required in "runtimes/$rid/native/libcna_c_api.$library_suffix" "runtimes/$rid/native/$sdl_library"; do
  if ! grep -Fxq "$required" <<<"$interop_entries"; then
    echo "CNA.Interop package is missing $required." >&2
    exit 1
  fi
done

DOTNET_COMMAND="$dotnet_command" CNA_DOTNET_ROOT="$repo_root" \
  "$template_root/scripts/verify-template.sh" --mode development
DOTNET_COMMAND="$dotnet_command" CNA_DOTNET_ROOT="$repo_root" \
  "$template_root/scripts/verify-template.sh" --mode package \
    --package-feed "$feed_root" --package-version "$package_version"

consumer_root="$work_root/IsolatedConsumer"
"$dotnet_command" new install "$template_root"
"$dotnet_command" new cna-game --name IsolatedConsumer --output "$consumer_root" \
  --consumerMode Package --cnaPackageVersion "$package_version"
"$dotnet_command" new nugetconfig --output "$work_root"
config_file="$work_root/nuget.config"
"$dotnet_command" nuget remove source nuget --configfile "$config_file"
"$dotnet_command" nuget add source "$feed_root" --name cna-local --configfile "$config_file"
"$dotnet_command" restore "$consumer_root/IsolatedConsumer.csproj" --configfile "$config_file" \
  --packages "$work_root/packages"
"$dotnet_command" build "$consumer_root/IsolatedConsumer.csproj" -c Release --no-restore -m:1

# grep rather than rg, which a macOS host does not ship.
if grep -n -E 'CnaDotnetRoot|CNA_DOTNET_ROOT|ProjectReference' "$consumer_root/IsolatedConsumer.csproj"; then
  echo "Isolated package consumer contains source-reference configuration." >&2
  exit 1
fi
# The checkout's source tree: a default output directory is build-consumer/ inside the checkout, so
# the checkout root alone would match the consumer's own paths (rg skipped that git-ignored
# directory and so read nothing).
if grep -r -n -F --include='*.csproj' --include='project.assets.json' "$repo_root/src/" "$consumer_root"; then
  echo "Isolated package consumer contains a CNA.NET source checkout path." >&2
  exit 1
fi

# The package's build/ targets turn an XNA project's <XnaProfile> into the RuntimeProfile resource
# GraphicsDeviceManager reads (CSX-081). Built a second time with the property set, into a separate
# directory, the package consumer must carry the line XNA's own build writes.
profile_output="$work_root/xnaprofile-hidef"
"$dotnet_command" build "$consumer_root/IsolatedConsumer.csproj" -c Release --no-restore -m:1 \
  -p:XnaProfile=HiDef -p:OutputPath="$profile_output/" -p:IntermediateOutputPath="$work_root/xnaprofile-obj/"
if ! grep -a -q 'Windows.v4.0.HiDef' "$profile_output/IsolatedConsumer.dll"; then
  echo "A package consumer with XnaProfile=HiDef has no Microsoft.Xna.Framework.RuntimeProfile resource." >&2
  exit 1
fi

consumer_output="$consumer_root/bin/Release/net8.0"
consumer_dll="$consumer_output/IsolatedConsumer.dll"
packaged_native="$consumer_output/runtimes/$rid/native/libcna_c_api.$library_suffix"
[[ -f "$consumer_dll" && -f "$packaged_native" ]] || {
  echo "The isolated consumer output is missing its managed or packaged native runtime." >&2
  exit 1
}

runtime_dir="$work_root/runtime"
mkdir -p "$runtime_dir"
chmod 700 "$runtime_dir"

run_consumer()
{
  local log=$1
  shift
  env -u CNA_NATIVE_LIBRARY -u CNA_NATIVE_DIR \
    XDG_RUNTIME_DIR="$runtime_dir" SDL_AUDIODRIVER=dummy SDL_VIDEODRIVER="$windowless_video_driver" \
    "$dotnet_command" "$consumer_dll" "$@" >"$log" 2>&1
}

run_consumer "$logs_root/frames-60.log" --frames 60
run_consumer "$logs_root/frames-600.log" --frames 600
grep -Fq 'drew 60 frames' "$logs_root/frames-60.log"
grep -Fq 'drew 600 frames' "$logs_root/frames-600.log"

mv "$packaged_native" "$work_root/libcna_c_api.saved.$library_suffix"
if run_consumer "$logs_root/missing-native.log" --frames 1; then
  echo "Missing-native diagnostic case unexpectedly succeeded." >&2
  exit 1
fi
mv "$work_root/libcna_c_api.saved.$library_suffix" "$packaged_native"
grep -Fq 'No CNA C API native library was found' "$logs_root/missing-native.log"
grep -Fq 'Platform/RID:' "$logs_root/missing-native.log"

cc "${wrong_architecture_flags[@]}" -shared -fPIC -Wall -Wextra -Werror \
  "$script_dir/package-fixtures/wrong_architecture.c" -o "$work_root/wrong-architecture.$library_suffix"
if CNA_NATIVE_LIBRARY="$work_root/wrong-architecture.$library_suffix" \
   "$dotnet_command" "$consumer_dll" --frames 1 >"$logs_root/wrong-architecture.log" 2>&1; then
  echo "Wrong-architecture diagnostic case unexpectedly succeeded." >&2
  exit 1
fi
grep -Fq 'wrong architecture or binary format' "$logs_root/wrong-architecture.log"
grep -Fq 'Platform/RID:' "$logs_root/wrong-architecture.log"

cc -shared -fPIC -Wall -Wextra -Werror "$script_dir/package-fixtures/wrong_abi.c" \
  -o "$work_root/wrong-abi.$library_suffix"
if CNA_NATIVE_LIBRARY="$work_root/wrong-abi.$library_suffix" CNA_NATIVE_DIR=/deliberately/ignored \
   XDG_RUNTIME_DIR="$runtime_dir" SDL_AUDIODRIVER=dummy SDL_VIDEODRIVER="$windowless_video_driver" \
   "$dotnet_command" "$consumer_dll" --frames 1 >"$logs_root/wrong-abi.log" 2>&1; then
  echo "Wrong-ABI diagnostic case unexpectedly succeeded." >&2
  exit 1
fi
grep -Fq 'implements C ABI 1.0.0' "$logs_root/wrong-abi.log"
consumer_abi=$(jq -r .consumerAbi "$repo_root/eng/cna-native-abi-policy.json")
grep -Fq "consumer ABI $consumer_abi" "$logs_root/wrong-abi.log"
grep -Fq 'explicit CNA_NATIVE_LIBRARY' "$logs_root/wrong-abi.log"

if CNA_NATIVE_LIBRARY="$abi_compatibility_root/fixtures/missing-required-symbol.$library_suffix" \
   "$dotnet_command" "$consumer_dll" --frames 1 >"$logs_root/missing-symbol.log" 2>&1; then
  echo "Missing-symbol diagnostic case unexpectedly succeeded." >&2
  exit 1
fi
grep -Fq "required symbol 'cna_game_destroy' is missing" "$logs_root/missing-symbol.log"

if CNA_NATIVE_LIBRARY="$work_root/does-not-exist.$library_suffix" \
   "$dotnet_command" "$consumer_dll" --frames 1 >"$logs_root/invalid-explicit-path.log" 2>&1; then
  echo "Invalid explicit-path diagnostic case unexpectedly succeeded." >&2
  exit 1
fi
grep -Fq 'CNA_NATIVE_LIBRARY selected' "$logs_root/invalid-explicit-path.log"
grep -Fq 'no fallback is attempted' "$logs_root/invalid-explicit-path.log"

conflict_dir="$work_root/conflict"
mkdir -p "$conflict_dir"
cp "$packaged_native" "$conflict_dir/libcna_c_api.$library_suffix"
cp "$packaged_native" "$conflict_dir/libcna-native.$library_suffix"
if env -u CNA_NATIVE_LIBRARY CNA_NATIVE_DIR="$conflict_dir" \
   "$dotnet_command" "$consumer_dll" --frames 1 >"$logs_root/conflict.log" 2>&1; then
  echo "Conflicting-library diagnostic case unexpectedly succeeded." >&2
  exit 1
fi
grep -Fq 'Conflicting CNA native libraries were found' "$logs_root/conflict.log"

# An explicit override is a whole native directory too: the library finds SDL beside itself.
explicit_dir="$work_root/explicit-native"
mkdir -p "$explicit_dir"
cp "$(dirname "$packaged_native")"/* "$explicit_dir/"
valid_override="$explicit_dir/libcna_c_api.$library_suffix"
CNA_NATIVE_LIBRARY="$valid_override" CNA_NATIVE_DIR=/deliberately/ignored \
  XDG_RUNTIME_DIR="$runtime_dir" SDL_AUDIODRIVER=dummy SDL_VIDEODRIVER="$windowless_video_driver" \
  "$dotnet_command" "$consumer_dll" --frames 60 >"$logs_root/explicit-override.log" 2>&1
grep -Fq 'drew 60 frames' "$logs_root/explicit-override.log"

jq -n \
  --arg version "$package_version" \
  --arg rid "$rid" \
  --arg nativeLibrary "libcna_c_api.$library_suffix" \
  --arg sdlLibrary "$sdl_library" \
  --arg nativeSource "$native_directory" \
  --arg interop "CNA.Interop.$package_version.nupkg" \
  --arg framework "CNA.Framework.$package_version.nupkg" \
  --arg compat "CNA.XnaCompat.$package_version.nupkg" \
  '{
    schemaVersion: 1,
    status: "passed",
    qualifiedEvidenceScope: ($rid + " local experiment only"),
    packageVersion: $version,
    nativeSource: $nativeSource,
    packages: [$interop, $framework, $compat],
    contents: ["managed DLLs", "XML documentation", "LICENSE", "NOTICE.md", "README.md", "portable PDB symbol packages", ("runtimes/" + $rid + "/native/" + $nativeLibrary), ("runtimes/" + $rid + "/native/" + $sdlLibrary)],
    isolatedRestore: "passed",
    isolatedBuild: "passed",
    sourceOrSiblingPaths: "absent",
    packagedNativeWithoutEnvironment: "passed",
    frames60: "passed",
    frames600: "passed",
    missingNativeDiagnostic: "passed",
    wrongArchitectureDiagnostic: "passed",
    wrongAbiDiagnostic: "passed",
    missingSymbolDiagnostic: "passed",
    invalidExplicitPathDiagnostic: "passed",
    conflictingLibrariesDiagnostic: "passed",
    explicitOverridePrecedence: "passed",
    nativeAbiPolicy: "cna-cs-native-abi/1",
    nativeAbiCompatibilityFixtures: "2 accepted / 12 rejected",
    nativeAbiSelectedLibrary: "passed",
    published: false,
    supportedRidClaim: false
  }' >"$output_root/acceptance-report.json"

echo "PACKAGE_ACCEPTANCE_STATUS=passed"
echo "PACKAGE_ACCEPTANCE_OUTPUT=$output_root"
echo "PACKAGE_FILES=CNA.Interop.$package_version.nupkg,CNA.Framework.$package_version.nupkg,CNA.XnaCompat.$package_version.nupkg"
echo "PACKAGE_NATIVE_ENV_REQUIRED=no"
echo "PACKAGE_XNA_PROFILE_RESOURCE=passed"
echo "PACKAGE_FRAMES_60=passed"
echo "PACKAGE_FRAMES_600=passed"
echo "PACKAGE_OVERRIDE_PRECEDENCE=passed"
