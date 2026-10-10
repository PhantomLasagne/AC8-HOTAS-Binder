#!/usr/bin/env bash
# Build AC8-HOTAS-Binder.exe from WSL with the C# compiler that ships with Windows
# (.NET Framework 4.x). Windows users can run build.cmd instead.
#
#   ./build.sh            build AC8-HOTAS-Binder.exe
#   ./build.sh test       build, then run the read-only self-test against your AC8HOTAS folder
#   ./build.sh install    build, then copy the exe and docs into <AC8HOTAS>/AC8-HOTAS-Binder/
#   ./build.sh package    build, then create dist/AC8-HOTAS-Binder-<version>.zip
#
# Set AC8HOTAS_DIR if your AC8HOTAS mod folder isn't in the default Steam location.
set -euo pipefail
cd "$(dirname "$0")"

CSC=/mnt/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe
AC8HOTAS_DIR="${AC8HOTAS_DIR:-/mnt/c/Program Files (x86)/Steam/steamapps/common/ACE COMBAT 8/Game/Binaries/Win64/UE4SS/Mods/AC8HOTAS}"
EXE=AC8-HOTAS-Binder.exe
VERSION=$(grep -oP 'public const string Version = "\K[^"]+' src/Binder.cs)

win() { wslpath -w "$1"; }

build() {
    [ -x "$CSC" ] || { echo "csc.exe not found at $CSC" >&2; exit 1; }
    "$CSC" /nologo /target:winexe /platform:anycpu /optimize+ \
        "/win32icon:$(win src/icon.ico)" "/win32manifest:$(win src/app.manifest)" \
        /r:System.Windows.Forms.dll /r:System.Drawing.dll \
        "/out:$(win "$EXE")" "$(win src/Binder.cs)"
    echo "Built $EXE $VERSION"
}

case "${1:-build}" in
    build)
        build ;;
    test)
        build
        report=$(mktemp --suffix=.txt)
        # The exe is a GUI app, so run it through cmd /c start /wait to get its exit code.
        cmd.exe /c start /wait "" "$(win "$EXE")" --selftest "$(win "$AC8HOTAS_DIR")" "$(win "$report")" 2>/dev/null \
            && status=0 || status=$?
        tr -d '\r' < "$report"; rm -f "$report"
        exit "$status" ;;
    install)
        build
        dest="$AC8HOTAS_DIR/AC8-HOTAS-Binder"
        mkdir -p "$dest"
        cp "$EXE" README.txt LICENSE.txt "$dest/"
        echo "Installed to $dest" ;;
    package)
        build
        stage=$(mktemp -d)
        mkdir -p "$stage/AC8-HOTAS-Binder/src" dist
        cp "$EXE" README.txt LICENSE.txt build.cmd "$stage/AC8-HOTAS-Binder/"
        cp src/Binder.cs src/app.manifest src/icon.ico src/make-icon.ps1 "$stage/AC8-HOTAS-Binder/src/"
        zip_path="$PWD/dist/AC8-HOTAS-Binder-$VERSION.zip"
        rm -f "$zip_path"
        (cd "$stage" && zip -qr "$zip_path" AC8-HOTAS-Binder)
        rm -rf "$stage"
        echo "Packaged dist/AC8-HOTAS-Binder-$VERSION.zip" ;;
    *)
        sed -n '2,10p' "$0"; exit 1 ;;
esac
