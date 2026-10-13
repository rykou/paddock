using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Paddock
{
    // Keeps fences stacked directly above the desktop window. Normally the desktop is at the bottom, so that is just
    // under every app; after Win+D the desktop is raised above the apps, and the fences go up with it.
    static class DesktopLayer
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);

        const uint GW_HWNDNEXT = 2, GW_HWNDPREV = 3;
        public static readonly IntPtr HWND_BOTTOM = (IntPtr)1;

        // The top-level window that holds the desktop icon view.
        public static IntPtr Host()
        {
            IntPtr progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero && FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                return progman;
            IntPtr worker = IntPtr.Zero;
            while ((worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
                if (FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                    return worker;
            return IntPtr.Zero;
        }

        // The window a fence should be placed under so it ends up directly above the desktop.
        // Zero means the top of the stack; HWND_BOTTOM is the fallback when the desktop cannot be found.
        public static IntPtr SlotAbove()
        {
            IntPtr host = Host();
            return host == IntPtr.Zero ? HWND_BOTTOM : GetWindow(host, GW_HWNDPREV);
        }

        // True when nothing but other fences, hidden or minimised windows lies between this fence and the desktop below it.
        public static bool SitsOnDesktop(IntPtr fence, HashSet<IntPtr> fences)
        {
            IntPtr host = Host();
            if (host == IntPtr.Zero) return true;
            int guard = 0;
            for (IntPtr h = GetWindow(fence, GW_HWNDNEXT); h != IntPtr.Zero && guard++ < 10000; h = GetWindow(h, GW_HWNDNEXT))
            {
                if (h == host) return true;
                if (fences.Contains(h) || !IsWindowVisible(h) || IsIconic(h)) continue;
                return false; // an app window is under the fence
            }
            return false; // the desktop is above the fence and covers it
        }
    }
}
