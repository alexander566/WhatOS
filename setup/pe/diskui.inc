static void hot(float x, float y, float w, float h, int act, int arg)
{
    if (g_nbtn >= 64) return;
    SetRect(&g_btn[g_nbtn], (int)x, (int)y, (int)(x + w), (int)(y + h));
    g_act[g_nbtn] = act;
    g_arg[g_nbtn] = arg;
    g_nbtn++;
}

static void tile(GpGraphics *g, float x, float y, float s, ARGB col, const wchar_t *t)
{
    rrect(g, x, y, s, s, 8 * (float)g_s, col, 0, 0);
    float th = text_h(g, t, F_SMALL, s);
    text(g, t, F_SMALL, C_INK, x, y + (s - th) / 2, s, 1);
}

static void row(GpGraphics *g, float x, float y, float w, float h, int sel, int dim, ARGB tc, const wchar_t *tl, const wchar_t *name, const wchar_t *right, int act, int arg)
{
    float S = (float)g_s;
    int hv = g_hover >= 0 && g_hover < g_nbtn ? 0 : 0;
    (void)hv;
    rrect(g, x, y, w, h, 10 * S, sel ? 0xFF1A2840 : C_PANEL, sel ? C_ACCENT : C_LINE, 1.5f * S);
    float ts = h - 14 * S;
    tile(g, x + 8 * S, y + 7 * S, ts, tc, tl);
    float nh = text_h(g, name, F_BODY, w);
    text(g, name, F_BODY, dim ? C_MUTED : C_INK, x + ts + 22 * S, y + (h - nh) / 2, w - ts - 140 * S, 0);
    float rh = text_h(g, right, F_MONO, 130 * S);
    text(g, right, F_MONO, C_MUTED, x + w - 140 * S, y + (h - rh) / 2, 128 * S, 2);
    if (act) hot(x, y, w, h, act, arg);
}

static float sidebtn(GpGraphics *g, float x, float y, float w, float h, const wchar_t *t, ARGB col, int act, int arg)
{
    float S = (float)g_s;
    rrect(g, x, y, w, h, 10 * S, 0, C_LINE, 1 * S);
    float th = text_h(g, t, F_SMALL, w);
    text(g, t, F_SMALL, col, x, y + (h - th) / 2, w, 1);
    hot(x, y, w, h, act, arg);
    return w;
}

static void paint_disk(GpGraphics *g, int W, int H)
{
    float S = (float)g_s, cw = 660 * S, cx = (W - cw) / 2, y = 90 * S;
    wchar_t b[256];
    int rows = 0;
    for (int i = 0; i < g_ndk; i++) {
        if (g_usb && g_dk[i].inst) continue;
        Gap gp[40];
        rows += 2 + gaps(&g_dk[i], gp);
        for (int j = 0; j < g_dk[i].np; j++) if ((g_dk[i].p[j].kind == 0 || g_dk[i].p[j].inst) && !g_dk[i].p[j].del) rows++;
    }
    float rh = (H - 330 * S) / (rows ? rows : 1);
    if (rh > 46 * S) rh = 46 * S;
    if (rh < 28 * S) rh = 28 * S;
    float gap = rh * 0.18f, rr = rh - gap;
    y += text(g, L"Install Windows on", F_H1, C_INK, cx, y, cw, 0) + 6 * S;
    y += text(g, L"Pick where Windows goes. Delete drives you don't need. Nothing changes until you press Install.", F_BODY, C_MUTED, cx, y, cw, 0) + 16 * S;
    float dw = 110 * S, pw = cw - dw - 8 * S;
    for (int i = 0; i < g_ndk; i++) {
        Disk *k = &g_dk[i];
        if (g_usb && k->inst) continue;
        swprintf(b, 256, L"%ls \x00b7 %.0f GB%ls", *k->model ? k->model : L"Disk", GB(k->size), k->gpt ? L"" : L" \x00b7 MBR: whole disk only");
        text(g, b, F_SMALL, C_MUTED, cx, y + rr * 0.25f, cw, 0);
        y += rh;
        for (int j = 0; j < k->np; j++) {
            Part *t = &k->p[j];
            if (t->del || (t->kind != 0 && !t->inst)) continue;
            wchar_t tl[4] = {t->letter ? t->letter : L'?', t->letter ? L':' : 0, 0}, nm[96], rt[32];
            swprintf(rt, 32, L"%.0f GB", GB(t->len));
            if (t->inst) {
                row(g, cx, y, cw, rr, 0, 1, 0xFF7A4CE0, tl, L"WhatOS installer \x00b7 in use", rt, 0, 0);
            } else {
                swprintf(nm, 96, L"%ls%ls%ls", *t->label ? t->label : L"Drive", *t->fs ? L" \x00b7 " : L"", t->fs);
                int ok = k->gpt && t->len >= MINWIN;
                int sel = g_tdisk == i && g_ttype == 0 && g_tidx == j;
                row(g, cx, y, pw, rr, sel, !ok, 0xFF2A3A52, tl, nm, rt, ok ? 100 : 0, i * 100 + j);
                sidebtn(g, cx + pw + 8 * S, y, dw, rr, L"Delete", C_DANGER, 101, i * 100 + j);
            }
            y += rh;
        }
        Gap gp[40];
        int ng = gaps(k, gp);
        for (int j = 0; j < ng; j++) {
            swprintf(b, 32, L"%.0f GB", GB(gp[j].len));
            int ok = k->gpt && gp[j].len >= MINWIN;
            int sel = g_tdisk == i && g_ttype == 1 && g_tidx == j;
            row(g, cx, y, gp[j].hasdel ? pw : cw, rr, sel, !ok, 0xFF1D2633, L"+", L"Free space", b, ok ? 102 : 0, i * 100 + j);
            if (gp[j].hasdel) sidebtn(g, cx + pw + 8 * S, y, dw, rr, L"Undo", C_MUTED, 103, i);
            y += rh;
        }
        int ok = !k->inst && k->size >= MINWIN;
        row(g, cx, y, cw, rr, g_tdisk == i && g_ttype == 2, !ok, 0xFF5A2A33, L"ALL", ok ? L"Whole disk" : L"Whole disk \x00b7 holds the installer", L"erase all", ok ? 104 : 0, i);
        y += rh + 6 * S;
    }
    wchar_t er[1024];
    if (erase_list(er, 1024)) {
        swprintf(b, 256, L"Will be erased: %ls", er);
        float bh = text_h(g, b, F_SMALL, cw - 32 * S) + 22 * S;
        rrect(g, cx, y, cw, bh, 10 * S, 0xFF2A1518, C_DANGER, 1 * S);
        text(g, b, F_SMALL, 0xFFFFD3D3, cx + 16 * S, y + 11 * S, cw - 32 * S, 0);
        y += bh + 12 * S;
    }
    float by = H - 90 * S;
    if (y + 10 * S > by) by = y + 10 * S;
    int can = g_tdisk >= 0 && target_len() >= MINWIN;
    float w1 = text_w(g, L"Next", F_BTN) + 36 * S;
    rrect(g, cx + cw - w1, by, w1, 40 * S, 8 * S, can ? C_ACCENT : 0xFF24324A, 0, 0);
    float th = text_h(g, L"Next", F_BTN, w1);
    text(g, L"Next", F_BTN, can ? C_ACCINK : C_MUTED, cx + cw - w1, by + (40 * S - th) / 2, w1, 1);
    if (can) hot(cx + cw - w1, by, w1, 40 * S, 105, 0);
    sidebtn(g, cx, by, 120 * S, 40 * S, L"Rescan drives", C_MUTED, 108, 0);
    if (!can) text(g, g_tdisk < 0 ? L"Pick a drive to continue" : L"That space is too small (needs 30 GB)", F_SMALL, 0xFFFFB547, cx + 132 * S, by + 12 * S, cw - w1 - 150 * S, 0);
}

static void paint_confirm(GpGraphics *g, int W, int H)
{
    float S = (float)g_s, cw = 560 * S, cx = (W - cw) / 2, y = H / 2.0f - 170 * S;
    wchar_t b[1200], t[160], er[1024];
    y += text(g, L"Ready to install", F_H1, C_INK, cx, y, cw, 0) + 18 * S;
    target_name(t, 160);
    swprintf(b, 1200, L"Windows goes on\n%ls", t);
    float bh = text_h(g, b, F_BODY, cw - 32 * S) + 24 * S;
    rrect(g, cx, y, cw, bh, 10 * S, C_PANEL, C_LINE, 1 * S);
    text(g, b, F_BODY, C_INK, cx + 16 * S, y + 12 * S, cw - 32 * S, 0);
    y += bh + 12 * S;
    if (erase_list(er, 1024)) {
        swprintf(b, 1200, L"Erased for good: %ls", er);
        bh = text_h(g, b, F_SMALL, cw - 32 * S) + 24 * S;
        rrect(g, cx, y, cw, bh, 10 * S, 0xFF2A1518, C_DANGER, 1 * S);
        text(g, b, F_SMALL, 0xFFFFD3D3, cx + 16 * S, y + 12 * S, cw - 32 * S, 0);
        y += bh + 12 * S;
    }
    y += text(g, L"Other drives are not touched.", F_SMALL, C_MUTED, cx, y, cw, 0) + 20 * S;
    float bw = button(g, 0, L"Install", cx, y, 1);
    g_act[0] = 107;
    button(g, 1, L"Back", cx + bw + 10 * S, y, 0);
    g_act[1] = 106;
}

static void disk_press(int act, int arg)
{
    int d = arg / 100, j = arg % 100;
    switch (act) {
    case 100: g_tdisk = d; g_ttype = 0; g_tidx = j; break;
    case 101:
        g_dk[d].p[j].del = 1;
        if (g_tdisk == d && g_ttype == 0 && g_tidx == j) g_tdisk = -1;
        if (g_tdisk == d && g_ttype == 1) g_tdisk = -1;
        break;
    case 102: g_tdisk = d; g_ttype = 1; g_tidx = j; break;
    case 103:
        for (int k = 0; k < g_dk[arg].np; k++) g_dk[arg].p[k].del = 0;
        if (g_tdisk == arg && g_ttype == 1) g_tdisk = -1;
        break;
    case 104: g_tdisk = arg; g_ttype = 2; g_tidx = 0; break;
    case 105: g_view = V_CONFIRM; g_focus = 0; break;
    case 106: g_view = V_DISK; break;
    case 107: g_planned = 1; start_work(); break;
    case 108: scan_disks(); g_tdisk = -1; break;
    }
    InvalidateRect(g_wnd, NULL, FALSE);
}
