typedef struct {
    int num, kind, del, inst;
    ULONGLONG off, len;
    wchar_t letter, label[40], fs[16];
} Part;
typedef struct {
    int num, gpt, np, inst, efi;
    ULONGLONG size;
    wchar_t model[64];
    Part p[32];
} Disk;
typedef struct { ULONGLONG off, len; int hasdel; } Gap;

static Disk g_dk[16];
static int g_ndk, g_tdisk = -1, g_ttype = -1, g_tidx = -1;
static wchar_t g_plan[2048];

#define GB(x) ((double)(x) / 1e9)
#define MINWIN 30000000000ULL

static int guid_eq(const GUID *a, const char *s)
{
    wchar_t w[64];
    StringFromGUID2(a, w, 64);
    char n[64];
    WideCharToMultiByte(CP_ACP, 0, w, -1, n, 64, NULL, NULL);
    return !_stricmp(n, s);
}

static void scan_disks(void)
{
    wchar_t letdisk[26], lab[26][40], fsn[26][16];
    int letpart[26];
    for (int i = 0; i < 26; i++) {
        letdisk[i] = -1;
        letpart[i] = 0;
        lab[i][0] = fsn[i][0] = 0;
        if (i == 'X' - 'A') continue;
        wchar_t p[16];
        swprintf(p, 16, L"\\\\.\\%lc:", 'A' + i);
        HANDLE h = CreateFileW(p, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_EXISTING, 0, NULL);
        if (h == INVALID_HANDLE_VALUE) continue;
        STORAGE_DEVICE_NUMBER dn;
        DWORD r;
        if (DeviceIoControl(h, IOCTL_STORAGE_GET_DEVICE_NUMBER, NULL, 0, &dn, sizeof dn, &r, NULL) && dn.DeviceType == FILE_DEVICE_DISK) {
            letdisk[i] = (wchar_t)dn.DeviceNumber;
            letpart[i] = dn.PartitionNumber;
            wchar_t root[8];
            swprintf(root, 8, L"%lc:\\", 'A' + i);
            GetVolumeInformationW(root, lab[i], 40, NULL, NULL, NULL, fsn[i], 16);
        }
        CloseHandle(h);
    }
    g_ndk = 0;
    for (int d = 0; d < 16 && g_ndk < 16; d++) {
        wchar_t p[40];
        swprintf(p, 40, L"\\\\.\\PhysicalDrive%d", d);
        HANDLE h = CreateFileW(p, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_EXISTING, 0, NULL);
        if (h == INVALID_HANDLE_VALUE) continue;
        Disk *k = &g_dk[g_ndk];
        memset(k, 0, sizeof *k);
        k->num = d;
        DWORD r;
        DISK_GEOMETRY_EX ge;
        if (DeviceIoControl(h, IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, NULL, 0, &ge, sizeof ge, &r, NULL)) k->size = ge.DiskSize.QuadPart;
        STORAGE_PROPERTY_QUERY q = {StorageDeviceProperty, PropertyStandardQuery};
        BYTE b[1024] = {0};
        if (DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, &q, sizeof q, b, sizeof b, &r, NULL)) {
            STORAGE_DEVICE_DESCRIPTOR *dd = (STORAGE_DEVICE_DESCRIPTOR *)b;
            if (dd->ProductIdOffset && dd->ProductIdOffset < r) MultiByteToWideChar(CP_ACP, 0, (char *)b + dd->ProductIdOffset, -1, k->model, 64);
            wchar_t *e = k->model + wcslen(k->model);
            while (e > k->model && e[-1] == ' ') *--e = 0;
        }
        static BYTE lb[65536];
        if (DeviceIoControl(h, IOCTL_DISK_GET_DRIVE_LAYOUT_EX, NULL, 0, lb, sizeof lb, &r, NULL)) {
            DRIVE_LAYOUT_INFORMATION_EX *dl = (DRIVE_LAYOUT_INFORMATION_EX *)lb;
            k->gpt = dl->PartitionStyle == PARTITION_STYLE_GPT;
            for (DWORD i = 0; i < dl->PartitionCount && k->np < 32; i++) {
                PARTITION_INFORMATION_EX *pi = &dl->PartitionEntry[i];
                if (!pi->PartitionNumber || !pi->PartitionLength.QuadPart) continue;
                Part *t = &k->p[k->np];
                t->num = pi->PartitionNumber;
                t->off = pi->StartingOffset.QuadPart;
                t->len = pi->PartitionLength.QuadPart;
                t->kind = 0;
                if (k->gpt) {
                    if (guid_eq(&pi->Gpt.PartitionType, "{C12A7328-F81F-11D2-BA4B-00A0C93EC93B}")) t->kind = 1, k->efi = t->num;
                    else if (guid_eq(&pi->Gpt.PartitionType, "{E3C9E316-0B5C-4DB8-817D-F92DF00215AE}")) t->kind = 2;
                    else if (guid_eq(&pi->Gpt.PartitionType, "{DE94BBA4-06D1-4D40-A16A-BFD50179D6AC}")) t->kind = 3;
                    else if (!guid_eq(&pi->Gpt.PartitionType, "{EBD0A0A2-B9E5-4433-87C0-68B6B72699C7}")) t->kind = 4;
                } else {
                    BYTE ty = pi->Mbr.PartitionType;
                    if (ty == 0x05 || ty == 0x0F) continue;
                    if (ty == 0x27) t->kind = 3;
                    else if (ty == 0xEF) t->kind = 1;
                }
                for (int L = 0; L < 26; L++)
                    if (letdisk[L] == d && letpart[L] == t->num) {
                        t->letter = 'A' + L;
                        wcscpy(t->label, lab[L]);
                        wcscpy(t->fs, fsn[L]);
                        if (g_H && t->letter == g_H) t->inst = 1, k->inst = 1;
                    }
                k->np++;
            }
            for (int i = 0; i < k->np; i++)
                for (int j = i + 1; j < k->np; j++)
                    if (k->p[j].off < k->p[i].off) { Part x = k->p[i]; k->p[i] = k->p[j]; k->p[j] = x; }
        }
        CloseHandle(h);
        if (k->size > 8000000000ULL || k->inst) g_ndk++;
    }
    plog(L"scan: %d disks\r\n", g_ndk);
    for (int i = 0; i < g_ndk; i++) {
        plog(L" disk %d %ls %.1f GB gpt %d efi %d inst %d\r\n", g_dk[i].num, g_dk[i].model, GB(g_dk[i].size), g_dk[i].gpt, g_dk[i].efi, g_dk[i].inst);
        for (int j = 0; j < g_dk[i].np; j++) { Part *t = &g_dk[i].p[j]; plog(L"  part %d kind %d %lc: %ls %.1f GB off %llu\r\n", t->num, t->kind, t->letter ? t->letter : '-', t->label, GB(t->len), t->off); }
    }
}

static int gaps(Disk *k, Gap *out)
{
    int n = 0;
    ULONGLONG cur = 1048576, end = k->size > 1048576 ? k->size - 1048576 : 0;
    int hd = 0;
    for (int i = 0; i <= k->np; i++) {
        ULONGLONG st = i < k->np ? k->p[i].off : end;
        if (i < k->np && (k->p[i].del && k->p[i].kind == 0 && !k->p[i].inst)) { hd = 1; continue; }
        if (st > cur && st - cur >= 1000000000ULL) { out[n].off = cur; out[n].len = st - cur; out[n].hasdel = hd; n++; }
        hd = 0;
        if (i < k->np) cur = k->p[i].off + k->p[i].len;
    }
    return n;
}

static ULONGLONG target_len(void)
{
    if (g_tdisk < 0) return 0;
    Disk *k = &g_dk[g_tdisk];
    if (g_ttype == 2) return k->size;
    if (g_ttype == 0) return k->p[g_tidx].len;
    Gap gp[40];
    int n = gaps(k, gp);
    return g_tidx < n ? gp[g_tidx].len : 0;
}

static void target_name(wchar_t *o, int n)
{
    Disk *k = &g_dk[g_tdisk];
    if (g_ttype == 2) swprintf(o, n, L"Whole disk %ls (%.0f GB)", k->model, GB(k->size));
    else if (g_ttype == 0) { Part *t = &k->p[g_tidx]; swprintf(o, n, L"%lc: %ls (%.0f GB)", t->letter ? t->letter : '?', *t->label ? t->label : L"Drive", GB(t->len)); }
    else swprintf(o, n, L"Free space on %ls (%.0f GB)", k->model, GB(target_len()));
}

static int erase_list(wchar_t *o, int n)
{
    int c = 0;
    o[0] = 0;
    for (int i = 0; i < g_ndk; i++) {
        Disk *k = &g_dk[i];
        for (int j = 0; j < k->np; j++) {
            Part *t = &k->p[j];
            if (t->kind != 0 || t->inst) continue;
            int gone = t->del || (i == g_tdisk && (g_ttype == 2 || (g_ttype == 0 && g_tidx == j)));
            if (!gone) continue;
            wchar_t x[96];
            swprintf(x, 96, L"%ls%lc: %ls (%.0f GB)", c ? L", " : L"", t->letter ? t->letter : '?', *t->label ? t->label : L"Drive", GB(t->len));
            if ((int)(wcslen(o) + wcslen(x)) < n - 1) wcscat(o, x);
            c++;
        }
        if (i == g_tdisk && g_ttype == 2) {
            wchar_t x[96];
            swprintf(x, 96, L"%lsall of %ls", c ? L", " : L"", k->model);
            if ((int)(wcslen(o) + wcslen(x)) < n - 1) wcscat(o, x);
            c++;
        }
    }
    return c;
}

static int write_plan(const wchar_t *path)
{
    FILE *f = _wfopen(path, L"w");
    if (!f) return 0;
    for (int i = 0; i < g_ndk; i++) {
        Disk *k = &g_dk[i];
        int tgt = i == g_tdisk, dels = 0;
        for (int j = 0; j < k->np; j++) dels += k->p[j].del;
        if (!tgt && !dels) continue;
        fprintf(f, "select disk %d\n", k->num);
        if (tgt && g_ttype == 2) {
            fprintf(f, "clean\nconvert gpt\ncreate partition efi size=300\nformat quick fs=fat32 label=System\nassign letter=S noerr\ncreate partition msr size=16\ncreate partition primary\nformat quick fs=ntfs label=Windows\nassign letter=T noerr\n");
            continue;
        }
        ULONGLONG off = 0, len = 0;
        if (tgt && g_ttype == 0) { off = k->p[g_tidx].off; len = k->p[g_tidx].len; k->p[g_tidx].del = 1; }
        if (tgt && g_ttype == 1) { Gap gp[40]; gaps(k, gp); off = gp[g_tidx].off; len = gp[g_tidx].len; }
        if (tgt && k->efi) fprintf(f, "select partition %d\nassign letter=S noerr\n", k->efi);
        int ord[32], no = 0;
        for (int j = 0; j < k->np; j++) if (k->p[j].del) ord[no++] = k->p[j].num;
        for (int a = 0; a < no; a++) for (int b = a + 1; b < no; b++) if (ord[b] > ord[a]) { int x = ord[a]; ord[a] = ord[b]; ord[b] = x; }
        for (int a = 0; a < no; a++) fprintf(f, "select partition %d\ndelete partition override\n", ord[a]);
        if (tgt && g_ttype == 0) k->p[g_tidx].del = 0;
        if (!tgt) continue;
        ULONGLONG st = (off + 1048575) / 1048576 * 1048576, mb = (off + len - st) / 1048576;
        if (!k->efi) {
            fprintf(f, "create partition efi size=300 offset=%llu\nformat quick fs=fat32 label=System\nassign letter=S noerr\n", st / 1024);
            st += 300ULL * 1048576;
            fprintf(f, "create partition msr size=16 offset=%llu\n", st / 1024);
            st += 16ULL * 1048576;
            mb -= 316;
        }
        fprintf(f, "create partition primary offset=%llu size=%llu\nformat quick fs=ntfs label=Windows\nassign letter=T noerr\n", st / 1024, mb - 1);
    }
    fclose(f);
    return 1;
}
