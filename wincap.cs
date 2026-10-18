// wincap: starts a program, waits, saves a PNG of its main window (PrintWindow, no input sent), then closes it.
//   wincap <out.png> <seconds> <exe> [args...]
// Build: csc /nologo /r:System.Drawing.dll wincap.cs
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

static class WinCap
{
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    struct RECT { public int L, T, R, B; }

    static int Main(string[] a)
    {
        var psi = new ProcessStartInfo(a[2], string.Join(" ", a.Skip(3).Select(x => "\"" + x + "\""))) { UseShellExecute = false };
        var p = Process.Start(psi);
        Thread.Sleep(int.Parse(a[1]) * 1000);
        p.Refresh();
        if (p.HasExited) { Console.WriteLine("process exited with code " + p.ExitCode); return 2; }
        IntPtr h = p.MainWindowHandle;
        RECT r;
        GetWindowRect(h, out r);
        using (var bmp = new Bitmap(r.R - r.L, r.B - r.T))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                PrintWindow(h, hdc, 2);   // PW_RENDERFULLCONTENT: includes the GPU-drawn area
                g.ReleaseHdc(hdc);
            }
            bmp.Save(a[0], ImageFormat.Png);
        }
        Console.WriteLine("title: " + p.MainWindowTitle);
        p.CloseMainWindow();
        if (!p.WaitForExit(3000)) p.Kill();
        return 0;
    }
}
