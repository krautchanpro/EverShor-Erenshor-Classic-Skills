#!/bin/bash
# ═══════════════════════════════════════════════════════════════
# ErenshorClassicSkills — Build & Install Script
# Run from the extracted ErenshorClassicSkills/ directory
# ═══════════════════════════════════════════════════════════════

# ── Configuration ─────────────────────────────────────────────
# Adjust these paths to match your system
STEAM_LIBRARY="$HOME/.local/share/Steam/steamapps/common"
# Flatpak Steam:
# STEAM_LIBRARY="$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common"

ERENSHOR_DIR="$STEAM_LIBRARY/Erenshor"
MANAGED_DIR="$ERENSHOR_DIR/Erenshor_Data/Managed"
BEPINEX_DIR="$ERENSHOR_DIR/BepInEx"
BEPINEX_CORE="$BEPINEX_DIR/core"
PLUGINS_DIR="$BEPINEX_DIR/plugins"

PROJECT_DIR="ErenshorSkills"
LIB_DIR="$PROJECT_DIR/lib"
OUTPUT_DLL="$PROJECT_DIR/bin/Debug/netstandard2.1/ErenshorClassicSkills.dll"

echo "═══════════════════════════════════════════"
echo "  ErenshorClassicSkills Build Script"
echo "═══════════════════════════════════════════"
echo ""

# ── Verify paths ─────────────────────────────────────────────
if [ ! -d "$ERENSHOR_DIR" ]; then
    echo "ERROR: Erenshor not found at $ERENSHOR_DIR"
    echo "Edit this script and set STEAM_LIBRARY to your Steam path."
    exit 1
fi

if [ ! -d "$BEPINEX_CORE" ]; then
    echo "ERROR: BepInEx not found at $BEPINEX_DIR"
    echo "Install BepInEx first:"
    echo "  https://thunderstore.io/c/erenshor/p/BepInEx/BepInExPack/"
    exit 1
fi

echo "✓ Erenshor: $ERENSHOR_DIR"
echo "✓ BepInEx:  $BEPINEX_DIR"
echo ""

# ── Copy DLLs to lib/ ────────────────────────────────────────
echo "Copying DLLs to lib/ ..."
mkdir -p "$LIB_DIR"

cp "$BEPINEX_CORE/BepInEx.dll"  "$LIB_DIR/" 2>/dev/null && echo "  ✓ BepInEx.dll"  || echo "  ✗ BepInEx.dll"
cp "$BEPINEX_CORE/0Harmony.dll" "$LIB_DIR/" 2>/dev/null && echo "  ✓ 0Harmony.dll" || echo "  ✗ 0Harmony.dll"

for dll in Assembly-CSharp.dll UnityEngine.dll UnityEngine.CoreModule.dll \
           UnityEngine.IMGUIModule.dll UnityEngine.UIModule.dll UnityEngine.InputLegacyModule.dll \
           UnityEngine.PhysicsModule.dll UnityEngine.TextRenderingModule.dll UnityEngine.JSONSerializeModule.dll \
           UnityEngine.ImageConversionModule.dll \
           UnityEngine.UI.dll \
           UnityEngine.AudioModule.dll \
           Unity.TextMeshPro.dll; do
    cp "$MANAGED_DIR/$dll" "$LIB_DIR/" 2>/dev/null && echo "  ✓ $dll" || echo "  ✗ $dll"
done
echo ""

# ── Build ─────────────────────────────────────────────────────
echo "Building..."
cd "$PROJECT_DIR" || exit 1
dotnet build -c Debug 2>&1
BUILD_RESULT=$?
cd ..

if [ $BUILD_RESULT -ne 0 ]; then
    echo ""
    echo "BUILD FAILED."
    echo "  - Check that all DLLs copied to lib/"
    echo "  - Install .NET SDK: sudo pacman -S dotnet-sdk"
    exit 1
fi

echo ""
echo "✓ Build successful"

# ── Install ──────────────────────────────────────────────────
echo "Installing to plugins/ ..."
mkdir -p "$PLUGINS_DIR"
cp "$OUTPUT_DLL" "$PLUGINS_DIR/ErenshorClassicSkills.dll" && \
    echo "✓ Installed to $PLUGINS_DIR" || \
    { echo "✗ Copy failed"; exit 1; }

mkdir -p "$PLUGINS_DIR/ClassicSkills/Icons"
echo "✓ Icons folder: $PLUGINS_DIR/ClassicSkills/Icons/"

echo ""
echo "═══════════════════════════════════════════"
echo "  DONE — Launch Erenshor through Steam"
echo ""
echo "  F8  Skills window     /skills  Chat overview"
echo "  F9  Forage            /smith   Smithing"
echo "  F10 Bind Wound        /bake    Baking"
echo "  F11 Sense Heading     /brew    Brewing"
echo "  M   Meditate          /fletch  Fletching"
echo "  ;   Beg               /jewel   Jewelcraft"
echo "                        /tailor  Tailoring"
echo "═══════════════════════════════════════════"
