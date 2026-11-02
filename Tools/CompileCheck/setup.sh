#!/bin/bash
# One-time setup for compile-checking Deadfall C# without the Unity editor (e.g. in a Linux cloud container).
# Installs Mono, then downloads Roslyn, NUnit, and Unity reference assemblies from NuGet into ./.cache
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"; C="$HERE/.cache"; mkdir -p "$C"
command -v mono >/dev/null || (sudo apt-get update -qq && sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -qq mono-devel)
get() { id=$(echo "$1" | tr A-Z a-z); [ -d "$C/$id" ] && return; curl -sSL "https://api.nuget.org/v3-flatcontainer/$id/$2/$id.$2.nupkg" -o "$C/$id.nupkg"; mkdir -p "$C/$id"; (cd "$C/$id" && unzip -qo "../$id.nupkg"); }
get Microsoft.Net.Compilers.Toolset 4.8.0
get NUnit 3.14.0
get NUnitLite 3.14.0
get UnityEngine.Modules 2021.3.33
echo "Compile-check tools ready in $C"
