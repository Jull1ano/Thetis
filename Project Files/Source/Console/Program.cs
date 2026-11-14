/*  Program.cs - Jull1ano fork of Thetis

Entry point. It lives outside the Console class on purpose: Console's static fields load resources and
start GDI+, and GDI+ keeps the screen DPI it sees at its start. High DPI (HighDpi.cs) has to be switched
on before that, or native controls get 96 DPI fonts.

This program is free software; you can redistribute it and/or modify it under the terms of the GNU
General Public License as published by the Free Software Foundation; either version 2 of the License,
or (at your option) any later version.
*/

using System;

namespace Thetis
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            startMulticoreJit();
            HighDpi.Init(args);
            Console.RunMain(args);
        }

        // a short key for this exe's folder (the JIT profile, the last skin)
        public static string ForkExeKey()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\').ToLowerInvariant();
            uint h = 2166136261;
            foreach (char c in exeDir) { h ^= c; h *= 16777619; }
            return h.ToString("x8");
        }

        // .NET's multicore JIT: each start records the methods it compiles, and the next start compiles them on the
        // other cores ahead of use instead of one by one on the way (Thetis is a few hundred thousand lines; the Setup
        // window alone has thousands of controls). One profile per exe folder in %LOCALAPPDATA%\OpenHPSDR\ThetisForkJit;
        // a rebuilt exe makes a new one on its first start. THETIS_NO_MCJ=1 switches it off (to compare start times).
        private static void startMulticoreJit()
        {
            try
            {
                if (Environment.GetEnvironmentVariable("THETIS_NO_MCJ") == "1") return;
                string root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenHPSDR", "ThetisForkJit");
                System.IO.Directory.CreateDirectory(root);
                System.Runtime.ProfileOptimization.SetProfileRoot(root);
                System.Runtime.ProfileOptimization.StartProfile("startup-" + ForkExeKey() + ".jitprofile");
            }
            catch { }
        }
    }
}
