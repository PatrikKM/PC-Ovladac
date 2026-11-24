using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PCOvladac
{
    static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            bool createdNew;
            using (var mutex = new Mutex(true, "Local\\PC-Ovladac-SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "Aplikace PC-Ovladac již běží v oznamovací oblasti (vedle hodin).",
                        "PC-Ovladac",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                try { SetProcessDPIAware(); } catch { }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                bool startMinimized = false;
                foreach (string arg in args)
                {
                    if (string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase))
                    {
                        startMinimized = true;
                    }
                }

                using (var context = new TrayAppContext(startMinimized))
                {
                    Application.Run(context);
                }
            }
        }
    }
}
