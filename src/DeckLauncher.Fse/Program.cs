using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeckLauncher.Fse
{
    /// <summary>
    /// Ponte do Modo Xbox. O Windows abre este programa (empacotado com a capacidade "gamingHome") como tela inicial; ele abre o
    /// DeckLauncher e fica vivo enquanto o DeckLauncher estiver aberto (se esta ponte terminar, o Windows acha que a tela inicial caiu).
    /// Assim o DeckLauncher e os jogos abertos por ele herdam o direito de ficar na frente dentro do Modo Xbox.
    /// </summary>
    internal static class Program
    {
        private const string LauncherProcess = "DeckLauncher";

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);

        [STAThread]
        private static void Main()
        {
            // Uma ponte por sessão: se o botão Início do Game Bar abrir outra, ela só chama o DeckLauncher para a frente e sai.
            bool first;
            using (var mutex = new Mutex(true, @"Local\DeckLauncherFseBridge", out first))
            {
                var running = Process.GetProcessesByName(LauncherProcess).FirstOrDefault();
                if (!first)
                {
                    BringToFront(running);
                    return;
                }

                if (running == null)
                {
                    var exe = FindLauncher();
                    if (exe == null)
                    {
                        MessageBox.Show("Não encontrei o DeckLauncher. Reinstale o programa com o instalador.", "DeckLauncher (Modo Xbox)",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    ShowHandshakeWindow();
                    AllowSetForegroundWindow(-1); // ASFW_ANY: o DeckLauncher pode assumir a frente

                    try
                    {
                        running = Process.Start(new ProcessStartInfo
                        {
                            FileName = exe,
                            Arguments = "--xbox",
                            WorkingDirectory = Path.GetDirectoryName(exe),
                            UseShellExecute = true,
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Não foi possível abrir o DeckLauncher: " + ex.Message, "DeckLauncher (Modo Xbox)",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }
                else
                {
                    BringToFront(running);
                }

                // Fica vivo até o DeckLauncher fechar.
                try { running?.WaitForExit(); } catch (InvalidOperationException) { }
                GC.KeepAlive(mutex);
            }
        }

        /// <summary>Uma janela quase invisível por um instante: na inicialização do Windows ela é o que mostra que a tela inicial "chegou".</summary>
        private static void ShowHandshakeWindow()
        {
            using (var form = new Form())
            {
                form.BackColor = Color.FromArgb(15, 17, 21);
                form.FormBorderStyle = FormBorderStyle.None;
                form.WindowState = FormWindowState.Maximized;
                form.ShowInTaskbar = false;
                form.Opacity = 0.01;
                var timer = new System.Windows.Forms.Timer { Interval = 1500 };
                timer.Tick += (s, e) => { timer.Stop(); form.Close(); };
                form.Shown += (s, e) => timer.Start();
                Application.EnableVisualStyles();
                Application.Run(form);
            }
        }

        private static void BringToFront(Process process)
        {
            if (process == null) return;
            try
            {
                process.Refresh();
                var window = process.MainWindowHandle;
                if (window == IntPtr.Zero) return;
                AllowSetForegroundWindow(process.Id);
                if (IsIconic(window)) ShowWindow(window, 9);
                SetForegroundWindow(window);
            }
            catch (InvalidOperationException) { }
        }

        /// <summary>Onde o instalador pôs o DeckLauncher: o registro (HKCU\Software\DeckLauncher\InstallDir) ou as pastas de sempre.</summary>
        private static string FindLauncher()
        {
            string dir = null;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\DeckLauncher"))
                    dir = key == null ? null : key.GetValue("InstallDir") as string;
            }
            catch (Exception) { }

            var candidates = new[]
            {
                dir,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DeckLauncher"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DeckLauncher"),
            };
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                var exe = Path.Combine(candidate, "DeckLauncher.exe");
                if (File.Exists(exe)) return exe;
            }
            return null;
        }
    }
}
