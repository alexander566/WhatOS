#!/bin/bash
set -e
cd "$(dirname "$0")"
mkdir -p ui
python3 - <<'P'
import json,codecs
p=json.load(open('ui_parts.json'))
t=open('index.tpl.html').read().replace('/*CSS*/',p['css']).replace('/*BI*/',p['bi']).replace('/*LOGO*/',p['logo'])
open('ui/index.html','w').write(t)
for f in ['payload/prepare.ps1','payload/finish.ps1']:
    b=open(f,'rb').read()
    if not b.startswith(codecs.BOM_UTF8): open(f,'wb').write(codecs.BOM_UTF8+b)
P
cp ../setup/pe/whatpe.exe payload/
cp ../setup/pe/logo.png payload/
cp ../setup/pe/logo.png ui/
cp ../setup/link/WhatOSLink.exe payload/
cp ../setup/wallpaper/whatos-wallpaper.jpg ../setup/wallpaper/whatos.theme payload/
${DOTNET:-$HOME/.dotnet/dotnet} build -c Release -nologo -v q -o out 2>&1 | grep -E "error|Warn|warn|Elapsed" | grep -v "^$" | head -20
ls -la out/WhatOS.exe
