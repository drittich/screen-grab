#!/usr/bin/env bash
#
# Build a dnf-installable ScreenGrab RPM on Fedora.
#
# Flow: publish a self-contained linux-x64 build -> stage the payload + .desktop +
# icon into a source tarball -> rpmbuild -bb against packaging/screengrab.spec.
#
# Prereqs (Fedora):  sudo dnf install dotnet-sdk-10.0 rpm-build
# Usage:             packaging/build-rpm.sh [version]
# Output:            packaging/dist/screengrab-<version>.x86_64.rpm
# Install:           sudo dnf install ./packaging/dist/screengrab-<version>.x86_64.rpm
#
set -euo pipefail

VERSION="${1:-1.0.0}"
RID="linux-x64"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
APP_PROJ="$REPO_ROOT/ScreenGrab.App/ScreenGrab.App.csproj"

WORK="$SCRIPT_DIR/build"
DIST="$SCRIPT_DIR/dist"
PUBLISH_DIR="$WORK/publish"
STAGE="$WORK/screengrab-$VERSION"

echo ">> Cleaning previous build"
rm -rf "$WORK"
mkdir -p "$PUBLISH_DIR" "$STAGE" "$DIST"

echo ">> Publishing self-contained $RID build (v$VERSION)"
dotnet publish "$APP_PROJ" \
    -c Release \
    -r "$RID" \
    -f net10.0 \
    --self-contained true \
    -p:Version="$VERSION" \
    -o "$PUBLISH_DIR"

if [ ! -f "$PUBLISH_DIR/screengrab" ]; then
    echo "!! Expected published executable '$PUBLISH_DIR/screengrab' not found" >&2
    exit 1
fi

echo ">> Staging payload"
mkdir -p "$STAGE/app"
cp -a "$PUBLISH_DIR/." "$STAGE/app/"
cp "$SCRIPT_DIR/screengrab.desktop" "$STAGE/screengrab.desktop"
cp "$REPO_ROOT/ScreenGrab.App/Assets/icon.png" "$STAGE/icon.png"

echo ">> Building source tarball"
TARBALL="$WORK/screengrab-$VERSION.tar.gz"
tar -C "$WORK" -czf "$TARBALL" "screengrab-$VERSION"

echo ">> Running rpmbuild"
RPMTOP="$WORK/rpmbuild"
mkdir -p "$RPMTOP"/{BUILD,BUILDROOT,RPMS,SOURCES,SPECS,SRPMS}
cp "$TARBALL" "$RPMTOP/SOURCES/"

rpmbuild \
    --define "_topdir $RPMTOP" \
    --define "app_version $VERSION" \
    -bb "$SCRIPT_DIR/screengrab.spec"

echo ">> Collecting RPM"
cp "$RPMTOP"/RPMS/x86_64/screengrab-*.rpm "$DIST/"

echo ""
echo ">> Done. RPM(s) in $DIST:"
ls -1 "$DIST"/*.rpm
echo ""
echo "Install with:  sudo dnf install ./$(realpath --relative-to="$REPO_ROOT" "$DIST")/screengrab-$VERSION-1.*.x86_64.rpm"
