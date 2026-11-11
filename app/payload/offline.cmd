@echo off
setlocal enabledelayedexpansion
set T=%1
set H=%2
set DEF=1& set XBOX=1& set STORE=1& set EDGE=1& set KEEPAPPS=Notepad Photos ScreenSketch WindowsTerminal WindowsCalculator Paint WindowsCamera
if exist %H%:\whatos\choices.cmd call %H%:\whatos\choices.cmd
echo choices DEF=%DEF% XBOX=%XBOX% STORE=%STORE% EDGE=%EDGE% KEEPAPPS=%KEEPAPPS%

reg load HKLM\OSYS %T%:\Windows\System32\config\SYSTEM >nul
set S=HKLM\OSYS\ControlSet001\Services
set N=0
for %%V in (MapsBroker CDPUserSvc TrkWks edgeupdate edgeupdatem WSAIFabricSvc whesvc DiagTrack PcaSvc InventorySvc Spooler PrintNotify SysMain WerSvc wercplsupport wisvc WdiServiceHost WdiSystemHost Wecsvc dmwappushservice RetailDemo WpcMonSvc PhoneSvc DPS TroubleshootingSvc DusmSvc SEMgrSvc SCardSvr ScDeviceEnum SCPolicySvc WMPNetworkSvc DisplayEnhancementService lmhosts CDPSvc WbioSrvc) do call :svc %%V 4
call :svc lfsvc 3
call :svc WaaSMedicSvc 4
call :svc UsoSvc 4
call :svc wuauserv 3
call :svc WinHttpAutoProxySvc 3
if "%XBOX%"=="0" for %%V in (XblAuthManager XblGameSave XboxNetApiSvc) do call :svc %%V 4
if "%DEF%"=="0" for %%V in (WinDefend WdBoot WdFilter WdNisDrv WdNisSvc Sense SecurityHealthService wscsvc MDCoreSvc webthreatdefsvc webthreatdefusersvc) do call :svc %%V 4
echo services off !N!
reg add HKLM\OSYS\ControlSet001\Control /v SvcHostSplitThresholdInKB /t REG_DWORD /d 4294967295 /f >nul
reg add HKLM\OSYS\ControlSet001\Control\Power\PowerThrottling /v PowerThrottlingOff /t REG_DWORD /d 1 /f >nul
set PF="HKLM\OSYS\ControlSet001\Control\Session Manager\Memory Management\PrefetchParameters"
reg add %PF% /v EnablePrefetcher /t REG_DWORD /d 0 /f >nul
reg add %PF% /v EnableSuperfetch /t REG_DWORD /d 0 /f >nul
reg add HKLM\OSYS\ControlSet001\Control\Power /v HibernateEnabled /t REG_DWORD /d 0 /f >nul
reg add HKLM\OSYS\ControlSet001\Control\Power /v HibernateEnabledDefault /t REG_DWORD /d 0 /f >nul
reg add "HKLM\OSYS\ControlSet001\Control\Session Manager\Power" /v HiberbootEnabled /t REG_DWORD /d 0 /f >nul
set PS=HKLM\OSYS\ControlSet001\Control\Power\User\PowerSchemes
reg add %PS% /v ActivePowerScheme /t REG_SZ /d 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c /f >nul
reg add %PS%\8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c\54533251-82be-4824-96c1-47b60b740d00\7b224883-b3cc-4d79-819f-8374152cbe7c /v ACSettingIndex /t REG_DWORD /d 90 /f >nul
reg add %PS%\8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c\48672f38-7a9a-4bb2-8bf8-3d85be19de4e\2bfc24f9-5ea2-4801-8213-3dbae01aa39d /v ACSettingIndex /t REG_DWORD /d 6 /f >nul
reg add %PS%\8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c\54533251-82be-4824-96c1-47b60b740d00\6c2993b0-8f48-481f-bcc6-00dd2742aa06 /v ACSettingIndex /t REG_DWORD /d 0 /f >nul
reg add %PS%\8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c\54533251-82be-4824-96c1-47b60b740d00\4e4450b3-6179-4e91-b8f1-5bb9938f81a1 /v ACSettingIndex /t REG_DWORD /d 0 /f >nul
reg add %PS%\8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c\54533251-82be-4824-96c1-47b60b740d00\36687f9e-e3a5-4dbf-b1dc-15eb381c6863 /v ACSettingIndex /t REG_DWORD /d 0 /f >nul
reg add %PS%\8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c\54533251-82be-4824-96c1-47b60b740d00\36687f9e-e3a5-4dbf-b1dc-15eb381c6864 /v ACSettingIndex /t REG_DWORD /d 0 /f >nul
reg add %PS%\8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c\7516b95f-f776-4464-8c53-06167f40cc99\aded5e82-b909-4619-9949-f5d71dac0bcb /v ACSettingIndex /t REG_DWORD /d 0 /f >nul
reg add %PS%\8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c\7516b95f-f776-4464-8c53-06167f40cc99\f1fbfde2-a960-4165-9f88-50667911ce96 /v ACSettingIndex /t REG_DWORD /d 0 /f >nul
reg unload HKLM\OSYS >nul
echo system hive done

mkdir %T%:\Windows\WhatOS 2>nul
copy /y %H%:\whatos\whatos-prep.ps1 %T%:\Windows\WhatOS\ >nul
copy /y %H%:\whatos\edge.ps1 %T%:\Windows\WhatOS\ >nul
copy /y %H%:\whatos\logo.png %T%:\Windows\WhatOS\ >nul
if exist %H%:\whatos\report.url copy /y %H%:\whatos\report.url %T%:\Windows\WhatOS\ >nul
if exist %H%:\whatos\ssh xcopy /e /i /y %H%:\whatos\ssh %T%:\Windows\WhatOS\ssh >nul
if exist %H%:\whatos\apps.txt copy /y %H%:\whatos\apps.ps1 %T%:\Windows\WhatOS\ >nul
if exist %H%:\whatos\apps.txt copy /y %H%:\whatos\apps.txt %T%:\Windows\WhatOS\ >nul
if exist %H%:\whatos\apps.txt echo apps queued
if "%EDGE%"=="0" (
 echo want> %T%:\Windows\WhatOS\edge.want
 mkdir %T%:\Windows\Setup\Scripts 2>nul
 echo schtasks /create /tn WhatOSEdge /ru SYSTEM /sc onlogon /rl highest /tr "powershell -NoProfile -ExecutionPolicy Bypass -File C:\Windows\WhatOS\edge.ps1" /f> %T%:\Windows\Setup\Scripts\SetupComplete.cmd
 echo edge removal scheduled
)
reg load HKLM\OS %T%:\Windows\System32\config\SOFTWARE >nul
set P=HKLM\OS\Policies\Microsoft
set AS="HKLM\OS\Microsoft\Active Setup\Installed Components\{7A1E5B2C-0A70-4D05-9E11-00000000A705}"
reg add %AS% /ve /t REG_SZ /d WhatOS /f >nul
reg add %AS% /v Version /t REG_SZ /d 1,0 /f >nul
reg add %AS% /v IsInstalled /t REG_DWORD /d 1 /f >nul
reg add %AS% /v StubPath /t REG_SZ /d "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File C:\Windows\WhatOS\whatos-prep.ps1" /f >nul
call :p "Windows\CloudContent" DisableWindowsConsumerFeatures 1
call :p "Windows\CloudContent" DisableCloudOptimizedContent 1
call :p "Windows\CloudContent" DisableConsumerAccountStateContent 1
call :p "Windows\CloudContent" DisableSoftLanding 1
call :p "Windows\WindowsCopilot" TurnOffWindowsCopilot 1
call :p "Windows\WindowsAI" DisableAIDataAnalysis 1
call :p "Dsh" AllowNewsAndInterests 0
call :p "Windows\Windows Search" DisableWebSearch 1
call :p "Windows\Windows Search" AllowCortana 0
call :p "Windows\AppPrivacy" LetAppsRunInBackground 2
call :p "Windows\DeliveryOptimization" DODownloadMode 99
call :p "Edge" StartupBoostEnabled 0
call :p "Edge" BackgroundModeEnabled 0
call :p "Windows\OneDrive" DisableFileSyncNGSC 1
call :p "Windows\GameDVR" AllowGameDVR 0
call :p "Windows\WindowsUpdate\AU" NoAutoRebootWithLoggedOnUsers 1
call :p "Windows\WindowsUpdate\AU" NoAutoUpdate 1
call :p "Windows\WindowsUpdate" TargetReleaseVersion 1
reg add "%P%\Windows\WindowsUpdate" /v ProductVersion /t REG_SZ /d "Windows 11" /f >nul
set DV=24H2
for /f "tokens=3" %%V in ('reg query "HKLM\OS\Microsoft\Windows NT\CurrentVersion" /v DisplayVersion 2^>nul ^| findstr DisplayVersion') do set DV=%%V
echo winver %DV%
reg add "%P%\Windows\WindowsUpdate" /v TargetReleaseVersionInfo /t REG_SZ /d %DV% /f >nul
reg add HKLM\OS\Microsoft\Windows\CurrentVersion\Policies\Explorer /v SettingsPageVisibility /t REG_SZ /d hide:windowsupdate /f >nul
call :p "Windows\System" EnableSmartScreen 0
reg add HKLM\OS\Microsoft\Windows\CurrentVersion\Explorer /v SmartScreenEnabled /t REG_SZ /d Off /f >nul
reg add HKLM\OS\Microsoft\PolicyManager\current\device\Start /v ConfigureStartPins /t REG_SZ /d "{\"pinnedList\":[]}" /f >nul
reg add HKLM\OS\Microsoft\PolicyManager\current\device\Start /v ConfigureStartPins_ProviderSet /t REG_DWORD /d 1 /f >nul
reg add HKLM\OS\Microsoft\Windows\CurrentVersion\Policies\System /v EnableFirstLogonAnimation /t REG_DWORD /d 0 /f >nul
if "%DEF%"=="0" call :p "Windows Defender" DisableAntiSpyware 1
if "%DEF%"=="0" reg delete HKLM\OS\Microsoft\Windows\CurrentVersion\Run /v SecurityHealth /f >nul 2>&1
reg add HKLM\OS\Microsoft\Windows\CurrentVersion\ReserveManager /v ShippedWithReserves /t REG_DWORD /d 0 /f >nul
reg add HKLM\OS\Microsoft\Windows\CurrentVersion\ReserveManager /v PassedPolicy /t REG_DWORD /d 0 /f >nul
reg unload HKLM\OS >nul
echo software hive done

reg load HKLM\DU %T%:\Users\Default\NTUSER.DAT >nul
set C=HKLM\DU\Software\Microsoft\Windows\CurrentVersion
for %%V in (ContentDeliveryAllowed FeatureManagementEnabled OemPreInstalledAppsEnabled PreInstalledAppsEnabled PreInstalledAppsEverEnabled SilentInstalledAppsEnabled SoftLandingEnabled SystemPaneSuggestionsEnabled SubscribedContentEnabled SubscribedContent-310093Enabled SubscribedContent-338388Enabled SubscribedContent-338389Enabled SubscribedContent-338393Enabled SubscribedContent-353694Enabled SubscribedContent-353696Enabled) do reg add %C%\ContentDeliveryManager /v %%V /t REG_DWORD /d 0 /f >nul
reg add %C%\UserProfileEngagement /v ScoobeSystemSettingEnabled /t REG_DWORD /d 0 /f >nul
reg add %C%\GameDVR /v AppCaptureEnabled /t REG_DWORD /d 0 /f >nul
reg add %C%\Explorer\Serialize /v StartupDelayInMSec /t REG_DWORD /d 0 /f >nul
reg add HKLM\DU\System\GameConfigStore /v GameDVR_Enabled /t REG_DWORD /d 0 /f >nul
reg delete %C%\Run /v OneDriveSetup /f >nul 2>&1
reg add "HKLM\DU\Control Panel\Mouse" /v MouseSpeed /t REG_SZ /d 0 /f >nul
reg add "HKLM\DU\Control Panel\Mouse" /v MouseThreshold1 /t REG_SZ /d 0 /f >nul
reg add "HKLM\DU\Control Panel\Mouse" /v MouseThreshold2 /t REG_SZ /d 0 /f >nul
reg add "HKLM\DU\Control Panel\Accessibility\StickyKeys" /v Flags /t REG_SZ /d 506 /f >nul
reg add "HKLM\DU\Control Panel\Accessibility\Keyboard Response" /v Flags /t REG_SZ /d 122 /f >nul
reg add "HKLM\DU\Control Panel\Accessibility\ToggleKeys" /v Flags /t REG_SZ /d 58 /f >nul
reg add HKLM\DU\Software\Microsoft\DirectX\UserGpuPreferences /v DirectXUserGlobalSettings /t REG_SZ /d "SwapEffectUpgradeEnable=1;VRROptimizeEnable=1;AutoHDREnable=0;" /f >nul
reg unload HKLM\DU >nul
echo default user hive done

set KEEP=DesktopAppInstaller VCLibs UI.Xaml NET.Native WindowsAppRuntime HEIF WebMedia VP9 AV1 Webp RawImage HEVC MPEG2 AVCEncoder DolbyAudio DolbyLaboratories NVIDIACorp AppUp.Intel AdvancedMicroDevices RealtekSemiconductor %KEEPAPPS%
if "%STORE%"=="1" set KEEP=!KEEP! WindowsStore StorePurchaseApp
if "%XBOX%"=="1" set KEEP=!KEEP! Xbox GamingApp
if "%DEF%"=="1" set KEEP=!KEEP! SecHealthUI
dism /Image:%T%:\ /Get-ProvisionedAppxPackages > X:\appx.txt
set R=0
copy /y X:\appx.txt %H%:\whatos\appx.txt >nul
for /f "tokens=3" %%P in ('findstr /b /c:"PackageName" X:\appx.txt') do call :rm %%P
echo appx removed !R!

call :del %T%:\Windows\System32\OneDriveSetup.exe
if "%DEF%"=="0" for %%D in ("%T%:\Program Files\Windows Defender" "%T%:\Program Files (x86)\Windows Defender" "%T%:\Program Files\Windows Defender Advanced Threat Protection" "%T%:\ProgramData\Microsoft\Windows Defender" "%T%:\Windows\System32\drivers\wd") do call :rd %%D
echo offline done
goto :eof

:svc
reg query %S%\%1 /v Start >nul 2>&1 || (echo   skip %1& goto :eof)
reg add %S%\%1 /v Start /t REG_DWORD /d %2 /f >nul && set /a N+=1
goto :eof
:p
reg add "%P%\%~1" /v %2 /t REG_DWORD /d %3 /f >nul
goto :eof
:rm
echo %1 | findstr /i "%KEEP%" >nul && (echo   keep %1& goto :eof)
dism /Image:%T%:\ /Remove-ProvisionedAppxPackage /PackageName:%1 >nul && (echo   removed %1& set /a R+=1)
goto :eof
:del
if not exist %1 goto :eof
takeown /f %1 >nul 2>&1
icacls %1 /grant *S-1-5-32-544:F >nul 2>&1
del /f /q %1
if exist %1 (echo   FAIL del %1) else (echo   deleted %1)
goto :eof
:rd
if not exist %1 goto :eof
takeown /f %1 /r /d y >nul 2>&1
icacls %1 /grant *S-1-5-32-544:F /t /c /q >nul 2>&1
rd /s /q %1
if exist %1 (echo   FAIL rd %1) else (echo   removed dir %1)
goto :eof
