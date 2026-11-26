#!/bin/bash
# 编译导演 Mod，产物在 Mod/1.6/Assemblies/MemorialDirector.dll
set -e
cd "$(dirname "$0")/Source/Director"
dotnet build -c Release -nologo -v q
ls -l ../../1.6/Assemblies/
