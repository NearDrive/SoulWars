#!/usr/bin/env bash
set -euo pipefail

DOTNET=${DOTNET:-dotnet}

$DOTNET test -c Release --no-build --filter "Category=MVP7"
$DOTNET test -c Release --no-build --filter "Category=ReplayVerify"
$DOTNET test -c Release --no-build --filter "Category=Soak"
