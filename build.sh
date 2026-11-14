#!/bin/bash
set -e
cd "$(dirname "$0")/setup/pe"
x86_64-w64-mingw32-windres whatpe.rc -O coff -o whatpe.res
x86_64-w64-mingw32-gcc -O2 -municode -mwindows -o whatpe.exe whatpe.c whatpe.res -lgdiplus -lshlwapi -lole32
cd ../link
x86_64-w64-mingw32-windres WhatOSLink.rc -O coff -o WhatOSLink.res
x86_64-w64-mingw32-gcc -O2 -municode -mwindows -o WhatOSLink.exe WhatOSLink.c WhatOSLink.res -lshell32
cd ../ram
x86_64-w64-mingw32-windres WhatOSRam.rc -O coff -o WhatOSRam.res
x86_64-w64-mingw32-gcc -O2 -municode -mwindows -o WhatOSRam.exe WhatOSRam.c WhatOSRam.res -lpsapi -lshell32
cd ../game
${DOTNET:-$HOME/.dotnet/dotnet} build -c Release -nologo -v q -o out
cd ../../app
./build.sh
