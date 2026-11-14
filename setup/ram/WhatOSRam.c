#include <windows.h>
#include <shellapi.h>
#include <psapi.h>
#include <tlhelp32.h>
#include <stdio.h>

typedef LONG (NTAPI *NtSSI)(int, PVOID, ULONG);

static void priv(const wchar_t *n)
{
    HANDLE t; TOKEN_PRIVILEGES p = {1};
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES, &t)) return;
    LookupPrivilegeValueW(NULL, n, &p.Privileges[0].Luid);
    p.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
    AdjustTokenPrivileges(t, FALSE, &p, 0, NULL, NULL);
    CloseHandle(t);
}

static ULONGLONG avail(void)
{
    MEMORYSTATUSEX m = {sizeof m};
    GlobalMemoryStatusEx(&m);
    return m.ullAvailPhys;
}

static int elevated(void)
{
    HANDLE t; TOKEN_ELEVATION e = {0}; DWORD n;
    if (OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &t)) { GetTokenInformation(t, TokenElevation, &e, sizeof e, &n); CloseHandle(t); }
    return e.TokenIsElevated;
}

static void clean(void)
{
    priv(SE_PROF_SINGLE_PROCESS_NAME);
    priv(SE_INCREASE_QUOTA_NAME);
    priv(SE_DEBUG_NAME);
    HANDLE s = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    PROCESSENTRY32W pe = {sizeof pe};
    if (s != INVALID_HANDLE_VALUE && Process32FirstW(s, &pe)) do {
        HANDLE h = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pe.th32ProcessID);
        if (h) { EmptyWorkingSet(h); CloseHandle(h); }
    } while (Process32NextW(s, &pe));
    if (s != INVALID_HANDLE_VALUE) CloseHandle(s);
    NtSSI f = (NtSSI)GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "NtSetSystemInformation");
    if (f) {
        int c = 3; f(80, &c, sizeof c);
        c = 4; f(80, &c, sizeof c);
    }
}

static void notify(const wchar_t *msg)
{
    HWND w = CreateWindowW(L"STATIC", L"", 0, 0, 0, 0, 0, HWND_MESSAGE, NULL, NULL, NULL);
    NOTIFYICONDATAW n = {sizeof n};
    n.hWnd = w; n.uID = 1; n.uFlags = NIF_ICON | NIF_INFO | NIF_TIP;
    n.hIcon = LoadIconW(GetModuleHandleW(NULL), MAKEINTRESOURCEW(1));
    wcscpy(n.szTip, L"WhatOS");
    wcscpy(n.szInfoTitle, L"Clear RAM");
    wcsncpy(n.szInfo, msg, 255);
    n.dwInfoFlags = NIIF_USER | NIIF_LARGE_ICON;
    Shell_NotifyIconW(NIM_ADD, &n);
    Sleep(6000);
    Shell_NotifyIconW(NIM_DELETE, &n);
    DestroyWindow(w);
}

int WINAPI wWinMain(HINSTANCE h, HINSTANCE p, PWSTR cmd, int show)
{
    if (!elevated()) {
        ShellExecuteW(NULL, L"open", L"schtasks.exe", L"/run /tn WhatOSClearRAM", NULL, SW_HIDE);
        return 0;
    }
    ULONGLONG a = avail();
    clean();
    Sleep(500);
    ULONGLONG b = avail();
    wchar_t m[128];
    double fr = b > a ? (double)(b - a) / (1024.0 * 1024 * 1024) : 0;
    swprintf(m, 128, L"Freed %.1f GB. %.1f GB free now.", fr, (double)b / (1024.0 * 1024 * 1024));
    notify(m);
    return 0;
}
