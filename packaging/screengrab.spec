# ScreenGrab RPM spec.
#
# This packages an already-published, self-contained .NET (linux-x64) build tree,
# so it needs no compiler/toolchain on the build host and no build-time network.
# The payload tarball is produced by packaging/build-rpm.sh, which also invokes
# rpmbuild against this spec. See that script for the full flow.
#
# The tarball %{SOURCE0} is expected to expand to a directory:
#   screengrab-%{version}/
#     app/            <- the `dotnet publish` output (contains the `screengrab` exe)
#     screengrab.desktop
#     icon.png        <- 256x256 app icon

Name:           screengrab
Version:        %{app_version}
Release:        1%{?dist}
Summary:        Capture a screen region, annotate, and copy or save it

License:        MIT
URL:            https://github.com/drittich/screengrab
Source0:        %{name}-%{version}.tar.gz

# Self-contained publish bundles the .NET runtime; the app still needs these at runtime.
Requires:       spectacle
Requires:       wl-clipboard

# The payload is a self-contained native x86-64 build.
ExclusiveArch:  x86_64

# The publish output ships its own bundled native libs; don't let rpmbuild's
# auto-dependency generator demand system versions of them, and skip debug packaging
# and the strip/build-id steps that mangle the bundled single-file host.
AutoReqProv:    no
%global debug_package %{nil}
%global __brp_strip %{nil}
%global __brp_strip_static_archive %{nil}
%global __brp_strip_comment_note %{nil}
%global __brp_check_rpaths %{nil}

%description
ScreenGrab is a tray utility that captures a screen region, lets you annotate it
with rounded rectangles and text boxes (with undo/redo), then copies the result to
the clipboard or saves a PNG to ~/Downloads/ScreenGrab.

On KDE Plasma (Wayland) it captures via Spectacle and copies images via wl-copy.
Bind Ctrl+Alt+F12 to `screengrab --capture` as a KDE custom shortcut to trigger it.

%prep
%setup -q

%install
rm -rf %{buildroot}

# App payload.
install -d %{buildroot}%{_libdir}/%{name}
cp -a app/. %{buildroot}%{_libdir}/%{name}/
chmod 0755 %{buildroot}%{_libdir}/%{name}/%{name}

# Launcher on PATH.
install -d %{buildroot}%{_bindir}
cat > %{buildroot}%{_bindir}/%{name} <<'EOF'
#!/bin/sh
exec %{_libdir}/screengrab/screengrab "$@"
EOF
chmod 0755 %{buildroot}%{_bindir}/%{name}

# Desktop entry.
install -d %{buildroot}%{_datadir}/applications
install -m 0644 screengrab.desktop %{buildroot}%{_datadir}/applications/%{name}.desktop

# Icon.
install -d %{buildroot}%{_datadir}/icons/hicolor/256x256/apps
install -m 0644 icon.png %{buildroot}%{_datadir}/icons/hicolor/256x256/apps/%{name}.png

%files
%{_libdir}/%{name}/
%{_bindir}/%{name}
%{_datadir}/applications/%{name}.desktop
%{_datadir}/icons/hicolor/256x256/apps/%{name}.png

%post
touch --no-create %{_datadir}/icons/hicolor &>/dev/null || :

%postun
if [ $1 -eq 0 ] ; then
    touch --no-create %{_datadir}/icons/hicolor &>/dev/null
    gtk-update-icon-cache %{_datadir}/icons/hicolor &>/dev/null || :
fi

%posttrans
gtk-update-icon-cache %{_datadir}/icons/hicolor &>/dev/null || :

%changelog
* Thu Sep 11 2026 D'Arcy Rittich <drittich@senseilabs.com> - 1.0.0-1
- Initial RPM packaging for the Avalonia/Skia cross-platform port.
