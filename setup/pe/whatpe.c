#ifndef UNICODE
#define UNICODE
#endif
#include <windows.h>
#include <winioctl.h>
#include <gdiplus.h>
#include <stdio.h>
#include <wchar.h>
#include <math.h>
#include <shlwapi.h>
#include <io.h>
#include <ctype.h>
#include <stdlib.h>

#define C_DESK 0xFF0B0F14
#define C_PANEL 0xFF18202B
#define C_LINE 0xFF263142
#define C_INK 0xFFE9EEF5
#define C_MUTED 0xFF8696AB
#define C_ACCENT 0xFF4C8DFF
#define C_ACCINK 0xFF06101F
#define C_OK 0xFF3FCB8A
#define C_DANGER 0xFFFF6B6B
#define C_TILE 0xFF2A3342

enum { V_RUN, V_DONE, V_ERR, V_AGAIN, V_DISK, V_CONFIRM, V_SCAN };
enum { F_H1, F_BODY, F_SMALL, F_MONO, F_CD, F_BRAND, F_BTN, F_LOGO, F_N };
#define NSTEP 5

static const wchar_t *STEP_T[NSTEP] = {L"Preparing the disk", L"Copying Windows", L"Applying your choices", L"Setting up boot", L"Cleaning up"};
static const wchar_t *STEP_S[NSTEP] = {L"Formatting the Windows partition", L"Writing the Windows image", L"Apps, services and settings", L"Boot menu entry", L"The installer partition is removed after the first sign-in"};
static const double STEP_W[NSTEP] = {3, 70, 17, 5, 5};

static HWND g_wnd;
static CRITICAL_SECTION g_cs;
static int g_force, g_view = V_RUN, g_step, g_fail = -1, g_cd = 10, g_demo, g_focus, g_hover = -1;
static double g_frac;
static DWORD g_t0, g_tick;
static wchar_t g_H = 0, g_ini[MAX_PATH], g_name[128] = L"Windows 11", g_diskinfo[160], g_applied[256] = L"", g_errcmd[512], g_tail[6][160];
static int g_tailn;
static int g_bios;
static wchar_t g_sl = L'S';
static int g_pick, g_usb, g_planned, g_whatos = 1, g_disk = 0, g_part = 3, g_efi = 1, g_index = 1;
static wchar_t g_fw[16] = L"UEFI", g_serial[64] = L"";
static GpFontFamily *g_fam[3];
static GpFont *g_font[F_N];
static double g_s = 1;
static RECT g_btn[64];
static int g_nbtn, g_act[64], g_arg[64];
static HANDLE g_log = INVALID_HANDLE_VALUE;
static ULONG_PTR g_gdip;

static void logw(const char *s, DWORD n)
{
    DWORD w;
    if (g_log != INVALID_HANDLE_VALUE) WriteFile(g_log, s, n, &w, NULL);
}

static void plog(const wchar_t *fmt, ...)
{
    wchar_t b[1024];
    char a[2048];
    va_list ap;
    va_start(ap, fmt);
    _vsnwprintf(b, 1023, fmt, ap);
    b[1023] = 0;
    va_end(ap);
    int n = WideCharToMultiByte(CP_UTF8, 0, b, -1, a, sizeof a, NULL, NULL);
    if (n > 1) logw(a, n - 1);
}

static void tail_add(const char *line, int n)
{
    while (n > 0 && (line[n - 1] == ' ' || line[n - 1] == '\r')) n--;
    int i = 0;
    while (i < n && line[i] == ' ') i++;
    if (i >= n) return;
    if (memchr(line + i, '%', n - i) && memchr(line + i, '[', n - i)) return;
    wchar_t w[160];
    int m = MultiByteToWideChar(CP_OEMCP, 0, line + i, n - i > 158 ? 158 : n - i, w, 159);
    w[m] = 0;
    EnterCriticalSection(&g_cs);
    if (g_tailn == 6) { memmove(g_tail[0], g_tail[1], sizeof g_tail[0] * 5); g_tailn = 5; }
    wcscpy(g_tail[g_tailn++], w);
    LeaveCriticalSection(&g_cs);
}

static void set_progress(int step, double frac)
{
    EnterCriticalSection(&g_cs);
    g_step = step;
    g_frac = frac < 0 ? 0 : frac > 1 ? 1 : frac;
    LeaveCriticalSection(&g_cs);
    PostMessage(g_wnd, WM_APP, 0, 0);
}

static double parse_pct(const char *b, int n)
{
    double best = -1;
    for (int i = 0; i < n; i++) {
        if (b[i] != '%') continue;
        int j = i;
        while (j > 0 && ((b[j - 1] >= '0' && b[j - 1] <= '9') || b[j - 1] == '.')) j--;
        if (j < i) best = atof(b + j);
    }
    return best;
}

static DWORD run(const wchar_t *cmd, int step)
{
    SECURITY_ATTRIBUTES sa = {sizeof sa, NULL, TRUE};
    HANDLE rd, wr;
    wchar_t line[1024];
    plog(L"\r\n> %ls\r\n", cmd);
    EnterCriticalSection(&g_cs);
    wcsncpy(g_errcmd, cmd, 511);
    LeaveCriticalSection(&g_cs);
    if (!CreatePipe(&rd, &wr, &sa, 0)) return GetLastError();
    SetHandleInformation(rd, HANDLE_FLAG_INHERIT, 0);
    STARTUPINFOW si = {sizeof si};
    PROCESS_INFORMATION pi;
    si.dwFlags = STARTF_USESTDHANDLES;
    si.hStdOutput = si.hStdError = wr;
    si.hStdInput = GetStdHandle(STD_INPUT_HANDLE);
    wcsncpy(line, cmd, 1023);
    line[1023] = 0;
    if (!CreateProcessW(NULL, line, NULL, NULL, TRUE, CREATE_NO_WINDOW, NULL, NULL, &si, &pi)) {
        DWORD e = GetLastError();
        CloseHandle(rd);
        CloseHandle(wr);
        plog(L"start failed %lu\r\n", e);
        return e ? e : 1;
    }
    CloseHandle(wr);
    char buf[4096], ln[512];
    int lnn = 0;
    DWORD n;
    while (ReadFile(rd, buf, sizeof buf, &n, NULL) && n) {
        logw(buf, n);
        if (step == 1) {
            double p = parse_pct(buf, n);
            if (p >= 0) set_progress(1, p / 100);
        }
        for (DWORD i = 0; i < n; i++) {
            if (buf[i] == '\n' || buf[i] == '\r') { tail_add(ln, lnn); lnn = 0; }
            else if (lnn < 511) ln[lnn++] = buf[i];
        }
    }
    tail_add(ln, lnn);
    CloseHandle(rd);
    WaitForSingleObject(pi.hProcess, INFINITE);
    DWORD code = 1;
    GetExitCodeProcess(pi.hProcess, &code);
    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);
    plog(L"exit %lu\r\n", code);
    return code;
}

static void norm(const wchar_t *s, wchar_t *o)
{
    for (; *s; s++)
        if (iswalnum(*s)) *o++ = towupper(*s);
    *o = 0;
}

static int serial_match(const wchar_t *dev)
{
    wchar_t a[128], b[128];
    norm(dev, a);
    norm(g_serial, b);
    size_t la = wcslen(a), lb = wcslen(b);
    if (la < 4 || lb < 4) return 0;
    return !wcsncmp(a, b, la < lb ? la : lb);
}

static int find_disk_by_serial(void)
{
    char seen[512] = "serials:";
    for (int d = 0; d < 32; d++) {
        wchar_t p[40];
        swprintf(p, 40, L"\\\\.\\PhysicalDrive%d", d);
        HANDLE h = CreateFileW(p, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_EXISTING, 0, NULL);
        if (h == INVALID_HANDLE_VALUE) continue;
        STORAGE_PROPERTY_QUERY q = {StorageDeviceProperty, PropertyStandardQuery};
        BYTE b[1024] = {0};
        DWORD r;
        if (DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, &q, sizeof q, b, sizeof b, &r, NULL)) {
            STORAGE_DEVICE_DESCRIPTOR *dd = (STORAGE_DEVICE_DESCRIPTOR *)b;
            if (dd->SerialNumberOffset && dd->SerialNumberOffset < r) {
                wchar_t s[128];
                MultiByteToWideChar(CP_ACP, 0, (char *)b + dd->SerialNumberOffset, -1, s, 128);
                wchar_t *a = s, *e;
                while (*a == ' ') a++;
                e = a + wcslen(a);
                while (e > a && e[-1] == ' ') *--e = 0;
                char t[80];
                snprintf(t, 80, " %d=[%ls]", d, a);
                if (strlen(seen) + strlen(t) < sizeof seen) strcat(seen, t);
                if (serial_match(a)) { CloseHandle(h); return d; }
            }
        }
        CloseHandle(h);
    }
    tail_add(seen, (int)strlen(seen));
    return -1;
}

static void read_diskinfo(void)
{
    wchar_t p[40];
    swprintf(p, 40, L"\\\\.\\PhysicalDrive%d", g_disk);
    HANDLE h = CreateFileW(p, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_EXISTING, 0, NULL);
    if (h == INVALID_HANDLE_VALUE) { swprintf(g_diskinfo, 160, L"%ls \x00b7 disk %d", g_name, g_disk); return; }
    STORAGE_PROPERTY_QUERY q = {StorageDeviceProperty, PropertyStandardQuery};
    BYTE b[1024] = {0};
    DWORD r;
    wchar_t m[96] = L"";
    if (DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, &q, sizeof q, b, sizeof b, &r, NULL)) {
        STORAGE_DEVICE_DESCRIPTOR *dd = (STORAGE_DEVICE_DESCRIPTOR *)b;
        if (dd->ProductIdOffset && dd->ProductIdOffset < r) MultiByteToWideChar(CP_ACP, 0, (char *)b + dd->ProductIdOffset, -1, m, 96);
    }
    CloseHandle(h);
    wchar_t *e = m + wcslen(m);
    while (e > m && e[-1] == ' ') *--e = 0;
    swprintf(g_diskinfo, 160, L"%ls \x00b7 disk %ls", g_name, *m ? m : L"0");
}

static void load_ini(void)
{
    for (wchar_t L = 'C'; L <= 'Z' && !g_H; L++) {
        wchar_t t[16];
        if (L == 'X') continue;
        swprintf(t, 16, L"%lc:\\whatos.tag", L);
        if (GetFileAttributesW(t) != INVALID_FILE_ATTRIBUTES) g_H = L;
    }
    if (!g_H) return;
    swprintf(g_ini, MAX_PATH, L"%lc:\\whatos\\whatpe.ini", g_H);
    HANDLE f = CreateFileW(g_ini, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL);
    if (f != INVALID_HANDLE_VALUE) {
        static char a[16384];
        static wchar_t w[16385];
        DWORD n = 0;
        ReadFile(f, a, sizeof a, &n, NULL);
        CloseHandle(f);
        if (n < 2 || (BYTE)a[0] != 0xFF || (BYTE)a[1] != 0xFE) {
            int o = (n >= 3 && (BYTE)a[0] == 0xEF && (BYTE)a[1] == 0xBB && (BYTE)a[2] == 0xBF) ? 3 : 0;
            int m = MultiByteToWideChar(CP_UTF8, 0, a + o, n - o, w + 1, 16384);
            w[0] = 0xFEFF;
            HANDLE t = CreateFileW(L"X:\\whatpe.ini", GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
            if (t != INVALID_HANDLE_VALUE) {
                DWORD wr;
                WriteFile(t, w, (m + 1) * sizeof(wchar_t), &wr, NULL);
                CloseHandle(t);
                wcscpy(g_ini, L"X:\\whatpe.ini");
            }
        }
    }
    GetPrivateProfileStringW(L"whatpe", L"name", g_name, g_name, 128, g_ini);
    GetPrivateProfileStringW(L"whatpe", L"applied", g_applied, g_applied, 256, g_ini);
    GetPrivateProfileStringW(L"whatpe", L"fw", g_fw, g_fw, 16, g_ini);
    GetPrivateProfileStringW(L"whatpe", L"serial", L"", g_serial, 64, g_ini);
    g_disk = GetPrivateProfileIntW(L"whatpe", L"disk", g_disk, g_ini);
    g_part = GetPrivateProfileIntW(L"whatpe", L"part", g_part, g_ini);
    g_efi = GetPrivateProfileIntW(L"whatpe", L"efi", g_efi, g_ini);
    g_index = GetPrivateProfileIntW(L"whatpe", L"index", g_index, g_ini);
    g_whatos = GetPrivateProfileIntW(L"whatpe", L"whatos", 1, g_ini);
    g_pick = GetPrivateProfileIntW(L"whatpe", L"pick", 0, g_ini);
    g_usb = GetPrivateProfileIntW(L"whatpe", L"usb", 0, g_ini);
}

static int smbios_uuid(wchar_t *o)
{
    DWORD n = GetSystemFirmwareTable('RSMB', 0, NULL, 0);
    if (!n) return 0;
    BYTE *b = malloc(n);
    if (!b || GetSystemFirmwareTable('RSMB', 0, b, n) != n) { free(b); return 0; }
    BYTE *p = b + 8, *e = b + 8 + *(DWORD *)(b + 4);
    int ok = 0;
    while (p + 4 <= e && !ok) {
        BYTE t = p[0], l = p[1];
        if (t == 1 && l >= 0x19 && p + 24 <= e) {
            BYTE *u = p + 8;
            swprintf(o, 40, L"%02X%02X%02X%02X-%02X%02X-%02X%02X-%02X%02X-%02X%02X%02X%02X%02X%02X", u[3], u[2], u[1], u[0], u[5], u[4], u[7], u[6], u[8], u[9], u[10], u[11], u[12], u[13], u[14], u[15]);
            ok = 1;
        }
        if (t == 127) break;
        BYTE *q = p + l;
        while (q + 1 < e && (q[0] || q[1])) q++;
        p = q + 2;
    }
    free(b);
    return ok;
}
static int same_pc(const wchar_t *f)
{
    char a[64] = {0};
    FILE *h = _wfopen(f, L"rb");
    if (!h) return 0;
    fread(a, 1, 63, h);
    fclose(h);
    wchar_t s[64], u[40];
    int i = 0;
    for (const char *p = a; *p && i < 63; p++) if (isxdigit((unsigned char)*p) || *p == '-') s[i++] = (wchar_t)toupper((unsigned char)*p);
    s[i] = 0;
    if (!smbios_uuid(u)) return 0;
    if (!wcscmp(u, L"FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF") || !wcscmp(u, L"00000000-0000-0000-0000-000000000000") || !wcscmp(u, L"03000200-0400-0500-0006-000700080009")) return 0;
    plog(L"pc uuid %ls, saved %ls\r\n", u, s);
    return !wcscmp(u, s);
}
#include "disk.inc"

static void flush_vol(wchar_t L)
{
    wchar_t p[16];
    swprintf(p, 16, L"\\\\.\\%lc:", L);
    HANDLE h = CreateFileW(p, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_EXISTING, 0, NULL);
    if (h == INVALID_HANDLE_VALUE) { plog(L"flush %lc: open failed %lu\r\n", L, GetLastError()); return; }
    BOOL ok = FlushFileBuffers(h);
    plog(L"flush %lc: %d\r\n", L, ok);
    CloseHandle(h);
}

static int fail(int step)
{
    EnterCriticalSection(&g_cs);
    g_fail = step;
    g_view = V_ERR;
    LeaveCriticalSection(&g_cs);
    plog(L"\r\nFAILED at step %d\r\n", step + 1);
    if (g_H) {
        wchar_t d[MAX_PATH];
        swprintf(d, MAX_PATH, L"%lc:\\whatos\\whatpe.log", g_H);
        FlushFileBuffers(g_log);
        CopyFileW(L"X:\\whatpe.log", d, FALSE);
    }
    PostMessage(g_wnd, WM_APP, 0, 0);
    return 0;
}

static DWORD WINAPI worker_demo(LPVOID a)
{
    int fail_at = (int)(INT_PTR)a;
    for (int s = 0; s < NSTEP; s++) {
        int n = s == 1 ? 200 : 25;
        for (int i = 0; i <= n; i++) {
            set_progress(s, (double)i / n);
            if (s == 1 && fail_at && i == 82) {
                tail_add("Error: 1392", 11);
                tail_add("The file or directory is corrupted and unreadable.", 50);
                wcscpy(g_errcmd, L"dism /Apply-Image /ImageFile:W:\\sources\\install.wim /Index:1 /ApplyDir:T:\\");
                return fail(1);
            }
            Sleep(40);
        }
    }
    EnterCriticalSection(&g_cs);
    g_view = V_DONE;
    g_cd = 10;
    LeaveCriticalSection(&g_cs);
    PostMessage(g_wnd, WM_APP, 1, 0);
    return 0;
}

static DWORD WINAPI worker(LPVOID a)
{
    (void)a;
    wchar_t c[1024];
    static int inited;
    set_progress(0, 0);
    if (!inited) { run(L"wpeinit", 0); inited = 1; }
    if (!g_H) load_ini();
    if (!g_H) { tail_add("WhatOS installer partition not found (no whatos.tag)", 53); wcscpy(g_errcmd, L"search C:..Z:\\whatos.tag"); return fail(0); }
    if (!g_force && !g_usb) {
        wchar_t fl[64];
        swprintf(fl, 64, L"%lc:\\whatos\\installed.flag", g_H);
        if (GetFileAttributesW(fl) != INVALID_FILE_ATTRIBUTES) {
            EnterCriticalSection(&g_cs);
            g_view = V_AGAIN;
            LeaveCriticalSection(&g_cs);
            PostMessage(g_wnd, WM_APP, 0, 0);
            return 0;
        }
    }
    {
        FIRMWARE_TYPE ft = FirmwareTypeUnknown;
        g_bios = GetFirmwareType(&ft) && ft == FirmwareTypeBios;
        if (g_bios) wcscpy(g_fw, L"BIOS");
    }
    if (g_pick && !g_planned) {
        EnterCriticalSection(&g_cs);
        g_view = V_SCAN;
        LeaveCriticalSection(&g_cs);
        PostMessage(g_wnd, WM_APP, 0, 0);
        scan_disks();
        EnterCriticalSection(&g_cs);
        g_view = V_DISK;
        LeaveCriticalSection(&g_cs);
        PostMessage(g_wnd, WM_APP, 0, 0);
        return 0;
    }
    if (g_planned) {
        g_disk = g_dk[g_tdisk].num;
        read_diskinfo();
        if (!write_plan(L"X:\\f.txt")) return fail(0);
        plog(L"installer %lc: planned target disk %d type %d idx %d index %d\r\n", g_H, g_disk, g_ttype, g_tidx, g_index);
    } else {
        if (*g_serial) {
            int d = find_disk_by_serial();
            swprintf(g_errcmd, 512, L"find disk with serial %ls", g_serial);
            if (d < 0) { tail_add("Target disk serial not found", 28); return fail(0); }
            g_disk = d;
        }
        read_diskinfo();
        plog(L"installer %lc: disk %d part %d efi %d index %d fw %ls\r\n", g_H, g_disk, g_part, g_efi, g_index, g_fw);
        FILE *f = _wfopen(L"X:\\f.txt", L"w");
        if (!f) return fail(0);
        if (g_bios && g_efi == g_part) {
            fprintf(f, "select disk %d\nselect partition %d\nformat quick fs=ntfs label=Windows\nactive\nassign letter=T noerr\n", g_disk, g_part);
            g_sl = L'T';
        } else fprintf(f, "select disk %d\nselect partition %d\nformat quick fs=ntfs label=Windows\nassign letter=T noerr\nselect partition %d\nassign letter=S noerr\n", g_disk, g_part, g_efi);
        fclose(f);
    }
    {
        FILE *f = _wfopen(L"X:\\f.txt", L"r");
        char ln[256];
        plog(L"diskpart script:\r\n");
        while (f && fgets(ln, sizeof ln, f)) logw(ln, (DWORD)strlen(ln));
        if (f) fclose(f);
    }
    if (!g_usb) {
        wchar_t id[64] = L"", *b, *e;
        FILE *f;
        swprintf(c, 1024, L"%lc:\\whatos\\bcd.id", g_H);
        if ((f = _wfopen(c, L"r, ccs=UTF-8"))) { fgetws(id, 64, f); fclose(f); }
        if ((b = wcschr(id, L'{')) && (e = wcschr(b, L'}'))) {
            e[1] = 0;
            swprintf(c, 1024, L"bcdedit /default %ls", b);
            if (run(c, 0)) plog(L"could not make the installer the default boot entry\r\n");
        }
    }
    set_progress(0, 0.3);
    if (run(L"diskpart /s X:\\f.txt", 0)) return fail(0);
    if (GetFileAttributesW(L"T:\\") == INVALID_FILE_ATTRIBUTES || !(GetLogicalDrives() & (1u << (g_sl - L'A')))) { tail_add("T: or S: missing after diskpart", 31); return fail(0); }
    set_progress(1, 0);
    wchar_t img[64];
    swprintf(img, 64, L"%lc:\\sources\\install.wim", g_H);
    if (GetFileAttributesW(img) == INVALID_FILE_ATTRIBUTES) swprintf(img, 64, L"%lc:\\sources\\install.esd", g_H);
    swprintf(c, 1024, L"dism /Apply-Image /ImageFile:%ls /Index:%d /ApplyDir:T:\\", img, g_index);
    if (run(c, 1)) return fail(1);
    set_progress(2, 0);
    swprintf(c, 1024, L"%lc:\\whatos\\drivers\\uuid.txt", g_H);
    if (same_pc(c)) {
        swprintf(c, 1024, L"dism /Image:T:\\ /Add-Driver /Driver:%lc:\\whatos\\drivers\\d /Recurse", g_H);
        if (run(c, 2)) plog(L"add-driver had errors, continuing\r\n");
        swprintf(c, 1024, L"cmd /c xcopy /e /i /y /q %lc:\\whatos\\drivers\\wifi T:\\Windows\\Setup\\Scripts\\wifi", g_H);
        run(c, 2);
    }
    set_progress(2, 0.05);
    if (g_whatos) {
        CreateDirectoryW(L"T:\\Windows\\Panther", NULL);
        swprintf(c, 1024, L"%lc:\\whatos\\pe.xml", g_H);
        if (!CopyFileW(c, L"T:\\Windows\\Panther\\unattend.xml", FALSE)) { tail_add("copy pe.xml failed", 18); wcscpy(g_errcmd, c); return fail(2); }
        swprintf(c, 1024, L"%lc:\\whatos\\whatos-prep.ps1", g_H);
        CopyFileW(c, L"T:\\Windows\\Temp\\whatos-prep.ps1", FALSE);
        set_progress(2, 0.1);
        swprintf(c, 1024, L"cmd /c call %lc:\\whatos\\offline.cmd T %lc", g_H, g_H);
        run(c, 2);
    }
    CreateDirectoryW(L"T:\\Windows\\Setup", NULL);
    CreateDirectoryW(L"T:\\Windows\\Setup\\Scripts", NULL);
    swprintf(c, 1024, L"%lc:\\whatos\\post.ps1", g_H);
    if (!CopyFileW(c, L"T:\\Windows\\Setup\\Scripts\\whatos-post.ps1", FALSE)) { tail_add("copy post.ps1 failed", 20); wcscpy(g_errcmd, c); return fail(2); }
    FILE *sc = _wfopen(L"T:\\Windows\\Setup\\Scripts\\SetupComplete.cmd", L"a");
    if (!sc) { tail_add("write SetupComplete.cmd failed", 30); return fail(2); }
    fprintf(sc, "\npowershell -NoProfile -ExecutionPolicy Bypass -File %%WINDIR%%\\Setup\\Scripts\\whatos-post.ps1\n");
    fflush(sc);
    _commit(_fileno(sc));
    fclose(sc);
    plog(L"SetupComplete.cmd runs whatos-post.ps1\r\n");
    set_progress(3, 0);
    swprintf(c, 1024, L"bcdboot T:\\Windows /s %lc: /f %ls", g_sl, g_fw);
    if (run(c, 3)) return fail(3);
    set_progress(3, 0.6);
    swprintf(c, 1024, L"bcdedit /store %lc:\\%ls\\BCD /set {default} disabledynamictick yes", g_sl, g_bios ? L"Boot" : L"EFI\\Microsoft\\Boot");
    run(c, 3);
    set_progress(4, 0.5);
    flush_vol('T');
    if (g_sl != L'T') flush_vol(g_sl);
    plog(L"\r\nDONE\r\n");
    FlushFileBuffers(g_log);
    swprintf(c, 1024, L"%lc:\\whatos\\whatpe.log", g_H);
    CopyFileW(L"X:\\whatpe.log", c, FALSE);
    CopyFileW(L"X:\\whatpe.log", L"T:\\Windows\\Temp\\whatpe.log", FALSE);
    if (!g_usb) {
        swprintf(c, 1024, L"%lc:\\whatos\\installed.flag", g_H);
        CloseHandle(CreateFileW(c, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL));
    }
    set_progress(4, 1);
    EnterCriticalSection(&g_cs);
    g_view = V_DONE;
    g_cd = g_usb ? 30 : 10;
    LeaveCriticalSection(&g_cs);
    PostMessage(g_wnd, WM_APP, 1, 0);
    return 0;
}

static void start_work(void)
{
    EnterCriticalSection(&g_cs);
    g_view = V_RUN;
    g_fail = -1;
    g_step = 0;
    g_frac = 0;
    g_tailn = 0;
    LeaveCriticalSection(&g_cs);
    g_t0 = GetTickCount();
    g_focus = 0;
    CloseHandle(CreateThread(NULL, 0, g_demo ? worker_demo : worker, (LPVOID)(INT_PTR)(g_demo == 2), 0, NULL));
}

static void do_reboot(void)
{
    if (g_demo) { start_work(); return; }
    wchar_t c[] = L"wpeutil reboot";
    run(c, -1);
    ExitProcess(0);
}

static void open_console(void)
{
    STARTUPINFOW si = {sizeof si};
    PROCESS_INFORMATION pi;
    wchar_t c[] = L"cmd.exe";
    if (CreateProcessW(NULL, c, NULL, NULL, FALSE, CREATE_NEW_CONSOLE, NULL, NULL, &si, &pi)) { CloseHandle(pi.hProcess); CloseHandle(pi.hThread); }
}

static GpFontFamily *fam(const wchar_t *const *names)
{
    GpFontFamily *f = NULL;
    for (; *names; names++)
        if (GdipCreateFontFamilyFromName(*names, NULL, &f) == Ok) return f;
    GdipGetGenericFontFamilySansSerif(&f);
    return f;
}

static void make_fonts(void)
{
    static const wchar_t *body[] = {L"Segoe UI", L"Segoe UI Semilight", L"Tahoma", NULL};
    static const wchar_t *semi[] = {L"Segoe UI Semibold", L"Segoe UI", L"Tahoma", NULL};
    static const wchar_t *mono[] = {L"Consolas", L"Lucida Console", NULL};
    if (!g_fam[0]) {
        g_fam[0] = fam(body);
        g_fam[1] = fam(semi);
        g_fam[2] = fam(mono);
    }
    static const struct { int fam; float px; int style; } spec[F_N] = {
        {1, 28, 0}, {0, 15, 0}, {0, 12.5f, 0}, {2, 12, 0}, {1, 44, 0}, {1, 14, 0}, {1, 15, 0}, {1, 11, 0}};
    for (int i = 0; i < F_N; i++) {
        if (g_font[i]) GdipDeleteFont(g_font[i]);
        GdipCreateFont(g_fam[spec[i].fam], (REAL)(spec[i].px * g_s), spec[i].style ? FontStyleBold : FontStyleRegular, UnitPixel, &g_font[i]);
    }
}

static GpStringFormat *g_fmt;

static float text_h(GpGraphics *g, const wchar_t *s, int font, float w)
{
    RectF lay = {0, 0, w, 10000}, out;
    GdipMeasureString(g, s, -1, g_font[font], &lay, g_fmt, &out, NULL, NULL);
    return out.Height;
}

static float text_w(GpGraphics *g, const wchar_t *s, int font)
{
    RectF lay = {0, 0, 10000, 10000}, out;
    GdipMeasureString(g, s, -1, g_font[font], &lay, g_fmt, &out, NULL, NULL);
    return out.Width;
}

static float text(GpGraphics *g, const wchar_t *s, int font, ARGB col, float x, float y, float w, int align)
{
    GpSolidFill *b;
    GdipCreateSolidFill(col, &b);
    float h = text_h(g, s, font, w);
    RectF r = {x, y, w, h + 2};
    GdipSetStringFormatAlign(g_fmt, align == 0 ? StringAlignmentNear : align == 1 ? StringAlignmentCenter : StringAlignmentFar);
    GdipDrawString(g, s, -1, g_font[font], &r, g_fmt, (GpBrush *)b);
    GdipSetStringFormatAlign(g_fmt, StringAlignmentNear);
    GdipDeleteBrush((GpBrush *)b);
    return h;
}

static void rrect_path(GpPath *p, float x, float y, float w, float h, float r)
{
    float d = r * 2;
    if (d > h) d = h;
    if (d > w) d = w;
    GdipAddPathArc(p, x, y, d, d, 180, 90);
    GdipAddPathArc(p, x + w - d, y, d, d, 270, 90);
    GdipAddPathArc(p, x + w - d, y + h - d, d, d, 0, 90);
    GdipAddPathArc(p, x, y + h - d, d, d, 90, 90);
    GdipClosePathFigure(p);
}

static void rrect(GpGraphics *g, float x, float y, float w, float h, float r, ARGB fill, ARGB stroke, float sw)
{
    GpPath *p;
    GdipCreatePath(FillModeAlternate, &p);
    rrect_path(p, x, y, w, h, r);
    if (fill) {
        GpSolidFill *b;
        GdipCreateSolidFill(fill, &b);
        GdipFillPath(g, (GpBrush *)b, p);
        GdipDeleteBrush((GpBrush *)b);
    }
    if (stroke) {
        GpPen *pn;
        GdipCreatePen1(stroke, sw, UnitPixel, &pn);
        GdipDrawPath(g, pn, p);
        GdipDeletePen(pn);
    }
    GdipDeletePath(p);
}

static float button(GpGraphics *g, int idx, const wchar_t *label, float x, float y, int primary)
{
    float S = (float)g_s, tw = text_w(g, label, F_BTN), w = tw + 36 * S, h = 40 * S;
    int hot = g_hover == idx;
    ARGB fill = primary ? (hot ? 0xFF6BA1FF : C_ACCENT) : (hot ? C_PANEL : 0);
    rrect(g, x, y, w, h, 8 * S, fill, primary ? 0 : C_LINE, 1 * S);
    if (g_focus == idx) rrect(g, x - 3 * S, y - 3 * S, w + 6 * S, h + 6 * S, 10 * S, 0, C_ACCENT, 2 * S);
    float th = text_h(g, label, F_BTN, w);
    text(g, label, F_BTN, primary ? C_ACCINK : C_INK, x, y + (h - th) / 2, w, 1);
    SetRect(&g_btn[idx], (int)x, (int)y, (int)(x + w), (int)(y + h));
    g_act[idx] = idx;
    g_arg[idx] = 0;
    if (idx + 1 > g_nbtn) g_nbtn = idx + 1;
    return w;
}

static GpBitmap *g_logo;

static void load_logo(void)
{
    HRSRC r = FindResourceW(NULL, MAKEINTRESOURCEW(1), (LPCWSTR)RT_RCDATA);
    if (!r) return;
    IStream *st = SHCreateMemStream((const BYTE *)LockResource(LoadResource(NULL, r)), SizeofResource(NULL, r));
    if (!st) return;
    GdipCreateBitmapFromStream(st, &g_logo);
    st->lpVtbl->Release(st);
}

static void logo(GpGraphics *g, float x, float y, float sz)
{
    if (!g_logo) return;
    GdipSetInterpolationMode(g, InterpolationModeHighQualityBicubic);
    GdipDrawImageRect(g, (GpImage *)g_logo, x, y, sz, sz);
}

static void start_work(void);
#include "diskui.inc"

static double overall(int step, double frac)
{
    double acc = 0;
    for (int i = 0; i < step; i++) acc += STEP_W[i];
    return acc + STEP_W[step] * frac;
}

static void paint(HDC hdc, int W, int H)
{
    GpGraphics *g;
    GdipCreateFromHDC(hdc, &g);
    GdipSetSmoothingMode(g, SmoothingModeAntiAlias);
    GdipSetTextRenderingHint(g, TextRenderingHintAntiAliasGridFit);
    GdipSetPixelOffsetMode(g, PixelOffsetModeHalf);
    GdipGraphicsClear(g, C_DESK);
    {
        GpPath *p;
        GpPathGradient *pg;
        GdipCreatePath(FillModeAlternate, &p);
        GdipAddPathEllipse(p, -W * 0.1f, -H * 0.9f, W * 1.2f, H * 1.8f);
        GdipCreatePathGradientFromPath(p, &pg);
        ARGB edge = C_DESK;
        int n = 1;
        GdipSetPathGradientCenterColor(pg, 0xFF141C28);
        GdipSetPathGradientSurroundColorsWithCount(pg, &edge, &n);
        GdipFillPath(g, (GpBrush *)pg, p);
        GdipDeleteBrush((GpBrush *)pg);
        GdipDeletePath(p);
    }
    float S = (float)g_s;
    int view, step, fail_s, cd, tn;
    double frac;
    wchar_t tail[6][160], errcmd[512];
    EnterCriticalSection(&g_cs);
    view = g_view; step = g_step; frac = g_frac; fail_s = g_fail; cd = g_cd; tn = g_tailn;
    memcpy(tail, g_tail, sizeof tail);
    wcscpy(errcmd, g_errcmd);
    LeaveCriticalSection(&g_cs);
    g_nbtn = 0;

    float pad = 28 * S;
    logo(g, pad, pad, 26 * S);
    text(g, L"WhatOS", F_BRAND, C_INK, pad + 34 * S, pad + 3 * S, 200 * S, 0);
    if (*g_diskinfo) text(g, g_diskinfo, F_MONO, C_MUTED, W / 2.0f, pad + 3 * S, W / 2.0f - pad, 2);

    float cw = 540 * S, cx = (W - cw) / 2, y;
    wchar_t b[256];
    if (view == V_RUN) {
        float hh = 40 * S + 30 * S + NSTEP * 54 * S + 30 * S + 30 * S;
        y = (H - hh) / 2;
        y += text(g, L"Installing WhatOS", F_H1, C_INK, cx, y, cw, 0) + 6 * S;
        y += text(g, L"Keep the PC plugged in. It restarts on its own when this is done.", F_BODY, C_MUTED, cx, y, cw, 0) + 18 * S;
        DWORD now = GetTickCount();
        for (int i = 0; i < NSTEP; i++) {
            float rh = 54 * S, dy = y + rh / 2, d = 16 * S, dx = cx + 3 * S;
            ARGB tc = i <= step ? C_INK : C_MUTED;
            if (i < step) {
                GpSolidFill *f;
                GdipCreateSolidFill(C_OK, &f);
                GdipFillEllipse(g, (GpBrush *)f, dx, dy - d / 2, d, d);
                GdipDeleteBrush((GpBrush *)f);
                GpPen *pn;
                GdipCreatePen1(C_DESK, 2 * S, UnitPixel, &pn);
                GdipSetPenLineJoin(pn, LineJoinRound);
                GdipSetPenStartCap(pn, LineCapRound);
                GdipSetPenEndCap(pn, LineCapRound);
                PointF ck[3] = {{dx + d * 0.28f, dy + d * 0.02f}, {dx + d * 0.44f, dy + d * 0.18f}, {dx + d * 0.74f, dy - d * 0.16f}};
                GdipDrawLines(g, pn, ck, 3);
                GdipDeletePen(pn);
            } else {
                GpPen *pn;
                GdipCreatePen1(i == step ? C_ACCENT : C_LINE, 1.6f * S, UnitPixel, &pn);
                if (i == step) {
                    GdipSetPenStartCap(pn, LineCapRound);
                    GdipSetPenEndCap(pn, LineCapRound);
                    GdipDrawArc(g, pn, dx, dy - d / 2, d, d, (REAL)((now % 1000) * 0.36), 270);
                } else
                    GdipDrawEllipse(g, pn, dx, dy - d / 2, d, d);
                GdipDeletePen(pn);
            }
            float tx = cx + 34 * S, tw = cw - 34 * S - 70 * S;
            float h1 = text_h(g, STEP_T[i], F_BODY, tw), h2 = text_h(g, STEP_S[i], F_SMALL, tw);
            float ty = dy - (h1 + h2) / 2;
            text(g, STEP_T[i], F_BODY, tc, tx, ty, tw, 0);
            text(g, g_usb && i == 4 ? L"Unplug the USB stick when the PC restarts" : STEP_S[i], F_SMALL, C_MUTED, tx, ty + h1, tw, 0);
            b[0] = 0;
            if (i < step) wcscpy(b, L"done");
            else if (i == step && i == 1) swprintf(b, 32, L"%d%%", (int)(frac * 100));
            if (*b) {
                float bh = text_h(g, b, F_MONO, 70 * S);
                text(g, b, F_MONO, C_MUTED, cx + cw - 70 * S, dy - bh / 2, 70 * S, 2);
            }
            y += rh;
        }
        y += 16 * S;
        double p = overall(step, frac);
        rrect(g, cx, y, cw, 8 * S, 4 * S, C_PANEL, 0, 0);
        if (p > 0.5) rrect(g, cx, y, (float)(cw * p / 100), 8 * S, 4 * S, C_ACCENT, 0, 0);
        y += 18 * S;
        swprintf(b, 32, L"%d%%", (int)p);
        text(g, b, F_MONO, C_MUTED, cx, y, cw / 2, 0);
        double el = (now - g_t0) / 1000.0;
        if (p < 8 || el < 20) wcscpy(b, L"estimating time");
        else {
            int m = (int)ceil(el * (100 - p) / p / 60);
            if (m <= 1) wcscpy(b, L"about 1 min left");
            else swprintf(b, 64, L"about %d min left", m);
        }
        text(g, b, F_MONO, C_MUTED, cx + cw / 2, y, cw / 2, 2);
        text(g, L"Don't turn off the PC", F_SMALL, C_MUTED, pad, H - pad - 16 * S, W / 2.0f, 0);
        swprintf(b, 64, L"step %d of %d", step + 1, NSTEP);
        text(g, b, F_SMALL, C_MUTED, W / 2.0f, H - pad - 16 * S, W / 2.0f - pad, 2);
    } else if (view == V_DONE) {
        y = H / 2.0f - 170 * S;
        y += text(g, L"Files are in place", F_H1, C_INK, cx, y, cw, 0) + 6 * S;
        y += text(g, L"Restarting into Windows. Windows then runs its normal first-time setup (region, keyboard, your account). A local account works, no Microsoft sign-in required.", F_BODY, C_MUTED, cx, y, cw, 0) + 22 * S;
        swprintf(b, 16, L"%d", cd);
        float ch = text(g, b, F_CD, C_ACCENT, cx, y, 80 * S, 0);
        float lh = text_h(g, L"seconds until restart", F_BODY, cw), nw = text_w(g, b, F_CD);
        text(g, cd == 1 ? L"second until restart" : L"seconds until restart", F_BODY, C_MUTED, cx + nw + 14 * S, y + (ch - lh) / 2 + 4 * S, cw, 0);
        y += ch + 20 * S;
        if (*g_applied) {
            float bh = text_h(g, g_applied, F_SMALL, cw - 32 * S) + text_h(g, L"Applied", F_SMALL, cw) + 28 * S;
            rrect(g, cx, y, cw, bh, 10 * S, C_PANEL, C_LINE, 1 * S);
            float yy = y + 14 * S;
            yy += text(g, L"Applied", F_SMALL, C_INK, cx + 16 * S, yy, cw - 32 * S, 0);
            text(g, g_applied, F_SMALL, C_MUTED, cx + 16 * S, yy + 2 * S, cw - 32 * S, 0);
            y += bh + 20 * S;
        }
        if (g_usb) {
            float uh = text_h(g, L"Unplug the USB stick now, so the PC starts Windows from the disk.", F_BODY, cw - 32 * S) + 24 * S;
            rrect(g, cx, y, cw, uh, 10 * S, 0xFF2A2412, 0xFFFFB547, 1 * S);
            text(g, L"Unplug the USB stick now, so the PC starts Windows from the disk.", F_BODY, 0xFFFFE2A8, cx + 16 * S, y + 12 * S, cw - 32 * S, 0);
            y += uh + 20 * S;
        }
        button(g, 0, L"Restart now", cx, y, 1);
        text(g, L"Don't turn off the PC", F_SMALL, C_MUTED, pad, H - pad - 16 * S, W / 2.0f, 0);
        text(g, L"done", F_SMALL, C_MUTED, W / 2.0f, H - pad - 16 * S, W / 2.0f - pad, 2);
    } else if (view == V_DISK) {
        paint_disk(g, W, H);
    } else if (view == V_CONFIRM) {
        paint_confirm(g, W, H);
    } else if (view == V_SCAN) {
        y = H / 2.0f - 40 * S;
        text(g, L"Looking at the drives in this PC\x2026", F_H1, C_INK, cx, y, cw, 0);
    } else if (view == V_AGAIN) {
        y = H / 2.0f - 120 * S;
        y += text(g, L"WhatOS is already installed", F_H1, C_INK, cx, y, cw, 0) + 6 * S;
        y += text(g, L"This PC started the installer again. Restart to open Windows. Install again erases the Windows partition and starts over.", F_BODY, C_MUTED, cx, y, cw, 0) + 24 * S;
        float bw = button(g, 0, L"Restart into Windows", cx, y, 1);
        button(g, 1, L"Install again", cx + bw + 10 * S, y, 0);
        text(g, L"Shift+F10 opens a console", F_SMALL, C_MUTED, pad, H - pad - 16 * S, W / 2.0f, 0);
    } else {
        y = H / 2.0f - 210 * S;
        int fs = fail_s < 0 ? 0 : fail_s;
        swprintf(b, 256, L"%ls stopped", STEP_T[fs]);
        y += text(g, b, F_H1, C_INK, cx, y, cw, 0) + 6 * S;
        const wchar_t *msg = fs == 0 ? L"The disk could not be prepared. Nothing was copied yet." :
                             fs == 1 ? L"Windows could not be written to the disk. The old Windows partition was already formatted, so this install needs to finish." :
                             L"Windows is copied, but a later step failed. The PC needs this step to finish before it can start.";
        y += text(g, msg, F_BODY, C_MUTED, cx, y, cw, 0) + 18 * S;
        const wchar_t *what = L"Try again runs the install once more from the start. If it stops at the same place, the drive or the install files may be damaged: restart and use the WhatOS USB stick if you have one, or open the console to check the disk.";
        float bh = text_h(g, what, F_SMALL, cw - 32 * S) + text_h(g, L"What you can do", F_SMALL, cw) + 28 * S;
        rrect(g, cx, y, cw, bh, 10 * S, C_PANEL, C_DANGER, 1 * S);
        float yy = y + 14 * S;
        yy += text(g, L"What you can do", F_SMALL, C_INK, cx + 16 * S, yy, cw - 32 * S, 0);
        text(g, what, F_SMALL, C_MUTED, cx + 16 * S, yy + 2 * S, cw - 32 * S, 0);
        y += bh + 16 * S;
        wchar_t lg[1400];
        swprintf(lg, 1400, L"%ls", errcmd);
        for (int i = 0; i < tn; i++) { wcscat(lg, L"\n"); wcscat(lg, tail[i]); }
        if (g_H) { wchar_t t[64]; swprintf(t, 64, L"\nlog saved: %lc:\\whatos\\whatpe.log", g_H); wcscat(lg, t); }
        y += text(g, lg, F_MONO, C_MUTED, cx, y, cw, 0) + 18 * S;
        float bw = button(g, 0, L"Try again", cx, y, 1);
        button(g, 1, L"Open console", cx + bw + 10 * S, y, 0);
        text(g, L"Shift+F10 opens a console", F_SMALL, C_MUTED, pad, H - pad - 16 * S, W / 2.0f, 0);
        swprintf(b, 64, L"step %d of %d", fs + 1, NSTEP);
        text(g, b, F_SMALL, C_MUTED, W / 2.0f, H - pad - 16 * S, W / 2.0f - pad, 2);
    }
    GdipDeleteGraphics(g);
}

static void press(int i)
{
    if (i >= 0 && i < g_nbtn && g_act[i] >= 100) { disk_press(g_act[i], g_arg[i]); return; }
    if (g_view == V_DONE && i == 0) do_reboot();
    else if (g_view == V_ERR && i == 0) start_work();
    else if (g_view == V_ERR && i == 1) open_console();
    else if (g_view == V_AGAIN && i == 0) do_reboot();
    else if (g_view == V_AGAIN && i == 1) { g_force = 1; start_work(); }
}

static int hit(int x, int y)
{
    POINT pt = {x, y};
    for (int i = 0; i < g_nbtn; i++)
        if (PtInRect(&g_btn[i], pt)) return i;
    return -1;
}

static LRESULT CALLBACK wndproc(HWND h, UINT m, WPARAM w, LPARAM l)
{
    switch (m) {
    case WM_SIZE: {
        int H = HIWORD(l);
        g_s = H / 760.0;
        if (g_s < 0.8) g_s = 0.8;
        make_fonts();
        InvalidateRect(h, NULL, FALSE);
        return 0;
    }
    case WM_ERASEBKGND:
        return 1;
    case WM_PAINT: {
        PAINTSTRUCT ps;
        HDC dc = BeginPaint(h, &ps);
        RECT r;
        GetClientRect(h, &r);
        HDC mdc = CreateCompatibleDC(dc);
        HBITMAP bm = CreateCompatibleBitmap(dc, r.right, r.bottom), old = SelectObject(mdc, bm);
        paint(mdc, r.right, r.bottom);
        BitBlt(dc, 0, 0, r.right, r.bottom, mdc, 0, 0, SRCCOPY);
        SelectObject(mdc, old);
        DeleteObject(bm);
        DeleteDC(mdc);
        EndPaint(h, &ps);
        return 0;
    }
    case WM_APP:
        if (w == 1) { g_focus = 0; SetTimer(h, 2, 1000, NULL); }
        InvalidateRect(h, NULL, FALSE);
        return 0;
    case WM_TIMER:
        if (w == 2) {
            if (g_view != V_DONE) { KillTimer(h, 2); return 0; }
            if (--g_cd <= 0) { KillTimer(h, 2); do_reboot(); }
        }
        if (w == 1 && g_view != V_RUN) return 0;
        InvalidateRect(h, NULL, FALSE);
        return 0;
    case WM_MOUSEMOVE: {
        int o = g_hover;
        g_hover = hit((short)LOWORD(l), (short)HIWORD(l));
        if (o != g_hover) InvalidateRect(h, NULL, FALSE);
        SetCursor(LoadCursor(NULL, g_hover >= 0 ? IDC_HAND : IDC_ARROW));
        return 0;
    }
    case WM_SETCURSOR:
        if (LOWORD(l) == HTCLIENT) { SetCursor(LoadCursor(NULL, g_hover >= 0 ? IDC_HAND : IDC_ARROW)); return TRUE; }
        break;
    case WM_LBUTTONUP: {
        int i = hit((short)LOWORD(l), (short)HIWORD(l));
        if (i >= 0) press(i);
        return 0;
    }
    case WM_KEYDOWN:
        if (w == VK_F10 && (GetKeyState(VK_SHIFT) & 0x8000)) { open_console(); return 0; }
        if (w == VK_TAB && g_nbtn) { g_focus = (g_focus + 1) % g_nbtn; InvalidateRect(h, NULL, FALSE); return 0; }
        if ((w == VK_RETURN || w == VK_SPACE) && g_nbtn) { press(g_focus); return 0; }
        if (w == VK_ESCAPE && g_demo) { PostQuitMessage(0); return 0; }
        break;
    case WM_SYSKEYDOWN:
        if (w == VK_F10 && (GetKeyState(VK_SHIFT) & 0x8000)) { open_console(); return 0; }
        if (w == VK_F4) return 0;
        break;
    case WM_CLOSE:
        if (g_demo) DestroyWindow(h);
        return 0;
    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProc(h, m, w, l);
}

int WINAPI wWinMain(HINSTANCE inst, HINSTANCE prev, PWSTR cmd, int show)
{
    (void)prev; (void)show;
    SetProcessDPIAware();
    if (wcsstr(cmd, L"/demoagain")) { g_demo = 1; g_view = V_AGAIN; }
    if (wcsstr(cmd, L"/demoerr")) g_demo = 2;
    else if (wcsstr(cmd, L"/demo")) g_demo = 1;
    InitializeCriticalSection(&g_cs);
    GdiplusStartupInput gi = {1, NULL, FALSE, FALSE};
    GdiplusStartup(&g_gdip, &gi, NULL);
    GdipStringFormatGetGenericTypographic(&g_fmt);
    { GpStringFormat *t; GdipCloneStringFormat(g_fmt, &t); g_fmt = t; GdipSetStringFormatFlags(g_fmt, StringFormatFlagsMeasureTrailingSpaces); }
    load_logo();
    AddFontResourceExW(L"X:\\Windows\\Fonts\\segoeui.ttf", FR_PRIVATE, 0);
    if (!g_demo) g_log = CreateFileW(L"X:\\whatpe.log", FILE_APPEND_DATA, FILE_SHARE_READ, NULL, OPEN_ALWAYS, 0, NULL);
    else {
        wcscpy(g_diskinfo, L"Windows 11 Pro 26H2 \x00b7 disk Samsung SSD 980 1TB");
        wcscpy(g_applied, L"Windows 11 Pro \x00b7 Defender removed \x00b7 3 extra apps \x00b7 WhatOS power plan");
    }
    WNDCLASSW wc = {0};
    wc.lpfnWndProc = wndproc;
    wc.hInstance = inst;
    wc.hCursor = LoadCursor(NULL, IDC_ARROW);
    wc.lpszClassName = L"WhatPE";
    RegisterClassW(&wc);
    int sw = GetSystemMetrics(SM_CXSCREEN), sh = GetSystemMetrics(SM_CYSCREEN);
    g_wnd = CreateWindowExW(g_demo ? 0 : WS_EX_TOPMOST, L"WhatPE", L"WhatOS", WS_POPUP | WS_VISIBLE, 0, 0, sw, sh, NULL, NULL, inst, NULL);
    SetTimer(g_wnd, 1, 33, NULL);
    if (g_view != V_AGAIN) start_work();
    MSG msg;
    while (GetMessage(&msg, NULL, 0, 0)) {
        TranslateMessage(&msg);
        DispatchMessage(&msg);
    }
    GdiplusShutdown(g_gdip);
    return 0;
}
