using System;
using System.Windows.Forms;

namespace RVCPM
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppPaths.EnsureDirectories();
            AppPaths.CleanupStaleTemp();
            Application.Run(new MainForm());
        }
    }
}
