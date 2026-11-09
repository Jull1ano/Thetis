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
            HighDpi.Init(args);
            Console.RunMain(args);
        }
    }
}
