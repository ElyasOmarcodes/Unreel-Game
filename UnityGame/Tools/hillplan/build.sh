#!/bin/bash
# Draws the Hill Village plan: compiles HillVillagePlan.cs with stand-ins for Unity's maths types, runs it,
# renders the JSON. Usage: Tools/hillplan/build.sh out.png [x0,z0,x1,z1]
set -e
here=$(cd "$(dirname "$0")" && pwd)
plan="$here/../../Assets/Editor/HillVillagePlan.cs"
work=$(mktemp -d)
FW=$(ls -d /usr/lib/dotnet/shared/Microsoft.NETCore.App/* | tail -1)
CSC=$(ls /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll | tail -1)
REFS=""
for f in System.Runtime System.Collections System.Linq System.Console System.Private.CoreLib netstandard System.Runtime.Extensions; do REFS="$REFS -r:$FW/$f.dll"; done
dotnet "$CSC" -nologo -nostdlib -optimize -out:"$work/plan.dll" $REFS "$here/Stubs.cs" "$here/Dump.cs" "$plan"
echo "{\"runtimeOptions\":{\"tfm\":\"net8.0\",\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":\"$(basename $FW)\"}}}" > "$work/plan.runtimeconfig.json"
dotnet "$work/plan.dll" "$work/plan.json"
python3 "$here/render.py" "$work/plan.json" "${1:-plan.png}" ${2:-}
