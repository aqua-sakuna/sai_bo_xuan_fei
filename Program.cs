using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Cyber​​ConcubineSelection
{
    static class Program
    {
        private static Mutex? singleInstanceMutex;

        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            singleInstanceMutex = new Mutex(true, @"Local\CyberConcubineSelection_SingleInstance", out bool isFirstInstance);
            if (!isFirstInstance)
            {
                MessageBox.Show("程序已经在运行中了。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (Environment.OSVersion.Version.Major >= 6)
            {
                SetProcessDPIAware();
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var configMigration = new ConfigMigration();
            configMigration.MigrateOldConfig();

            try
            {
                Application.Run(new 赛博选妃());
            }
            finally
            {
                singleInstanceMutex?.ReleaseMutex();
                singleInstanceMutex?.Dispose();
            }
        }
    }
}
