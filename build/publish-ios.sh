#!/usr/bin/env bash
#
# publish-ios.sh — build the NativeAOT *shared* library for device + simulator,
# wrap each .dylib into a KestrelBackend.framework, bundle both into an
# .xcframework, and verify the native entry points.
#
# RUN THIS ON A MAC. NativeAOT-for-iOS cross-compiles through Apple clang + the
# iOS SDK; the dylib link / install_name_tool / xcframework steps cannot run on
# Windows or Linux.
#
# Why a shared .dylib (not a static .a): the documented .NET 9 "NativeAOT for
# iOS-like platforms" path produces a self-contained shared library by DEFAULT
# (no <NativeLib>, no extra mobile workload). Apple requires a .dylib to be
# packaged into a .framework to be consumed by an app; we do that here and
# Embed & Sign it in Xcode. Microsoft's docs explicitly do NOT cover consuming a
# *static* AOT .a on iOS (you'd hand-link ~8 order-sensitive runtime/PAL
# archives), and setting <NativeLib>Shared</NativeLib> *explicitly* on an Apple
# RID routes into the Mono `mobile-librarybuilder` toolchain (band-mismatched,
# breaks). So: do not set NativeLib at all — take the default dylib.
# Ref: learn.microsoft.com/dotnet/core/deploying/native-aot/ios-like-platforms/creating-and-consuming-custom-frameworks
#
# Prereqs:
#   • .NET 9 SDK            (dotnet --version → 9.x)
#   • iOS workload          (dotnet workload install ios   # 9.x band)
#   • Xcode + command line  (xcode-select -p)
#
# Usage:
#   build/publish-ios.sh                  # ships the raw TcpListener host (default)
#   USE_KESTREL=true build/publish-ios.sh # ATTEMPTS Kestrel — KNOWN to fail on iOS:
#                                         # NETSDK1082, there is no
#                                         # Microsoft.AspNetCore.App runtime pack for
#                                         # any ios-* RID. Kept only to document the
#                                         # gate; not a shippable configuration.
set -euo pipefail

# ── Resolve repo paths (script lives in <repo>/build) ────────────────────────
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJ="$REPO_ROOT/src/KestrelBackend/KestrelBackend.csproj"
HEADER="$REPO_ROOT/ios/EmbeddedKestrelApp/KestrelBackend.h"
ARTIFACTS="$REPO_ROOT/build/artifacts"
STAGE="$REPO_ROOT/build/frameworks"
XCFRAMEWORK="$ARTIFACTS/KestrelBackend.xcframework"
FW_NAME="KestrelBackend"

USE_KESTREL="${USE_KESTREL:-false}"
CONFIG="Release"
DEPLOY_TARGET="17.0"

# Device is always arm64. Simulator slice matches the host: Apple Silicon → arm64,
# Intel → x64.
DEVICE_RID="ios-arm64"
if [[ "$(uname -m)" == "arm64" ]]; then
  SIM_RID="iossimulator-arm64"
else
  SIM_RID="iossimulator-x64"
fi

echo "▶ UseKestrel=$USE_KESTREL  device=$DEVICE_RID  sim=$SIM_RID"

# ── Publish one RID → echo the path to the produced .dylib ───────────────────
publish_slice() {
  local rid="$1"
  local log="$REPO_ROOT/build/publish-$rid.log"

  # Do NOT pass -p:NativeLib here. The default native output for an AOT class lib
  # on an Apple RID is the self-contained KestrelBackend.dylib. Passing
  # -p:NativeLib globally also leaks into LegacyLib (ns2.0) → NETSDK1147
  # (mobile-librarybuilder required).
  dotnet publish "$PROJ" \
    -c "$CONFIG" \
    -r "$rid" \
    -p:UseKestrel="$USE_KESTREL" \
    >"$log" 2>&1 || { echo "✖ publish failed for $rid — see $log" >&2; exit 1; }

  local dy
  dy="$(find "$REPO_ROOT/src/KestrelBackend/bin/$CONFIG" -path "*/$rid/publish/$FW_NAME.dylib" -print -quit 2>/dev/null || true)"
  [[ -z "$dy" ]] && dy="$(find "$REPO_ROOT/src/KestrelBackend/bin/$CONFIG" -name "$FW_NAME.dylib" -path "*$rid*" -print -quit 2>/dev/null || true)"
  [[ -z "$dy" ]] && { echo "✖ could not locate $FW_NAME.dylib for $rid" >&2; exit 1; }
  echo "$dy"
}

# ── Verify the unmanaged entry points are exported + the dylib is self-contained
verify_symbols() {
  local dy="$1"
  # Capture the match count rather than `grep -q`: under `set -o pipefail`, grep -q
  # closes the pipe on its first match, nm then dies with SIGPIPE(141), and pipefail
  # propagates that 141 — a spurious "failure" even though the symbols ARE present.
  local found
  found="$(nm -gU "$dy" 2>/dev/null | grep -Eo '_kestrel_(start|stop)' | sort -u | wc -l | tr -d ' ')"
  if [[ "$found" -lt 2 ]]; then
    echo "✖ kestrel_start/kestrel_stop not both exported in $dy (matched $found/2)" >&2
    echo "  (NativeAOT prefixes C symbols with '_' on Apple platforms.)" >&2
    exit 1
  fi
  # Self-containment: a proper NativeAOT shared lib links the GC/runtime in, so there
  # must be ZERO undefined _Rh* (Redhawk runtime) symbols left dangling.
  local rh
  rh="$(nm -u "$dy" 2>/dev/null | grep -c '_Rh' || true)"
  if [[ "$rh" -ne 0 ]]; then
    echo "✖ $dy is NOT self-contained ($rh undefined _Rh* runtime symbols)" >&2
    exit 1
  fi
}

# ── Wrap a published .dylib into a flat iOS KestrelBackend.framework ──────────
# $1=rid  $2=dylib path  $3=CFBundleSupportedPlatforms value (iPhoneOS|iPhoneSimulator)
make_framework() {
  local rid="$1" dy="$2" platform="$3"
  local fwdir="$STAGE/$rid/$FW_NAME.framework"
  rm -rf "$fwdir"; mkdir -p "$fwdir"

  # The binary inside a framework is named after the framework (no .dylib suffix).
  cp "$dy" "$fwdir/$FW_NAME"
  # LC_ID_DYLIB = @rpath/… so dyld resolves the symbols at runtime from the
  # Embed & Sign'd copy inside the app's Frameworks/ dir.
  install_name_tool -id "@rpath/$FW_NAME.framework/$FW_NAME" "$fwdir/$FW_NAME" 2>/dev/null

  cat >"$fwdir/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key><string>en</string>
  <key>CFBundleExecutable</key><string>$FW_NAME</string>
  <key>CFBundleIdentifier</key><string>org.steveackley.$FW_NAME</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleName</key><string>$FW_NAME</string>
  <key>CFBundlePackageType</key><string>FMWK</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>MinimumOSVersion</key><string>$DEPLOY_TARGET</string>
  <key>CFBundleSupportedPlatforms</key><array><string>$platform</string></array>
</dict>
</plist>
PLIST

  echo "$fwdir"
}

echo "▶ publishing device slice (dylib)…"
DEVICE_DY="$(publish_slice "$DEVICE_RID")"; echo "  → $DEVICE_DY"
echo "▶ publishing simulator slice (dylib)…"
SIM_DY="$(publish_slice "$SIM_RID")";       echo "  → $SIM_DY"

echo "▶ verifying exported symbols + self-containment…"
verify_symbols "$DEVICE_DY"
verify_symbols "$SIM_DY"
echo "  ✓ kestrel_start/kestrel_stop exported; both slices self-contained"

echo "▶ packaging frameworks…"
DEVICE_FW="$(make_framework "$DEVICE_RID" "$DEVICE_DY" iPhoneOS)";       echo "  → $DEVICE_FW"
SIM_FW="$(make_framework "$SIM_RID" "$SIM_DY" iPhoneSimulator)";         echo "  → $SIM_FW"

echo "▶ creating xcframework…"
rm -rf "$XCFRAMEWORK"; mkdir -p "$ARTIFACTS"
xcodebuild -create-xcframework \
  -framework "$DEVICE_FW" \
  -framework "$SIM_FW" \
  -output "$XCFRAMEWORK"
echo "  ✓ $XCFRAMEWORK"

echo
echo "✅ done. Next: cd ios && xcodegen generate && open EmbeddedKestrel.xcodeproj"
