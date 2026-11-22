#!/bin/bash
# usage: build.sh <srcdir> <out.dll>   (needs .NET SDK 8: csc.dll from the SDK's Roslyn folder)
SRC=$1; OUT=$2
HERE=$(cd "$(dirname "$0")" && pwd)
CSC=$(ls /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll /usr/share/dotnet/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | head -1)
FILES=$(find $SRC -name '*.cs' -not -path '*/OpenVR/*' -not -name 'VrCameraSetup_CoreOpenVR.cs' -not -path '*/obj/*' -not -path '*/bin/*')
# LOADER=ML builds the MelonLoader version (default: BepInEx). ML: no BepInEx refs, MelonLoader's own 0Harmony.
LOADER=${LOADER:-BIE}
if [ "$LOADER" = "ML" ]; then
  REFS=$(for f in $HERE/ref/*.dll; do case $(basename $f) in BepInEx*|0Harmony.dll) ;; *) echo -n "-r:$f ";; esac; done; for f in $HERE/ref_ml/*.dll; do echo -n "-r:$f "; done)
else
  REFS=$(for f in $HERE/ref/*.dll; do echo -n "-r:$f "; done)
fi
# -pathmap: source paths embedded in the dll (CallerFilePath in logs) become "MimesisVR/..." instead of the build machine path
SRCABS=$(cd "$SRC" && pwd)
dotnet $CSC -nologo -noconfig -nostdlib -target:library -langversion:latest -unsafe -optimize+ -deterministic -nowarn:CS1701,CS1702,CS0618,CS8632 -define:MONO\;$LOADER\;OPENXR_BUILD -pathmap:"$SRCABS"=MimesisVR -out:$OUT $REFS $FILES
