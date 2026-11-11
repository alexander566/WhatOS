#!/bin/bash
set -e
cd "$(dirname "$0")/setup/pe"
x86_64-w64-mingw32-windres whatpe.rc -O coff -o whatpe.res
x86_64-w64-mingw32-gcc -O2 -municode -mwindows -o whatpe.exe whatpe.c whatpe.res -lgdiplus -lshlwapi -lole32
cd ../../app
./build.sh
