#include <windows.h>
#include <shellapi.h>
int WINAPI wWinMain(HINSTANCE h, HINSTANCE p, PWSTR cmd, int n) {
    wchar_t exe[MAX_PATH]; GetModuleFileNameW(NULL, exe, MAX_PATH);
    wchar_t *s = wcsrchr(exe, L'\\'); wcscpy(s + 1, L"WhatOS.exe");
    ShellExecuteW(NULL, L"runas", exe, cmd, NULL, SW_SHOWNORMAL);
    return 0;
}
