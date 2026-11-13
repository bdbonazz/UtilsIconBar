using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Toolkit.Uwp.Notifications;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

namespace GuidFormatter
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // Single instance check
            bool createdNew;
            using var mutex = new Mutex(true, "GuidFormatterApp", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("GuidFormatter è già in esecuzione nel tray.", "GuidFormatter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApplicationContext());
        }
    }

    class TrayApplicationContext : ApplicationContext
    {
        private NotifyIcon _trayIcon;
        private HotkeyManager _hotkeyManager;
        private const int HOTKEY_ID = 1;

        public TrayApplicationContext()
        {
            // Build tray icon using a simple programmatic icon
            _trayIcon = new NotifyIcon
            {
                Icon = CreateIcon(),
                Text = "GUID Formatter\nCtrl+Shift+G per convertire",
                Visible = true,
                ContextMenuStrip = BuildContextMenu()
            };

            _trayIcon.DoubleClick += (s, e) => ConvertClipboard();

            // Register global hotkey Ctrl+Shift+G
            _hotkeyManager = new HotkeyManager();
            _hotkeyManager.RegisterHotKey(HOTKEY_ID, KeyModifiers.Control | KeyModifiers.Shift, Keys.G);
            _hotkeyManager.HotKeyPressed += (s, e) =>
            {
                if (e.Id == HOTKEY_ID)
                    ConvertClipboard();
            };
        }

        private ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip();
            var convertItem = new ToolStripMenuItem("🔄 Converti GUID (Ctrl+Shift+G)");
            convertItem.Click += (s, e) => ConvertClipboard();
            convertItem.Font = new Font(convertItem.Font, FontStyle.Bold);

            var separator = new ToolStripSeparator();

            var exitItem = new ToolStripMenuItem("❌ Esci");
            exitItem.Click += (s, e) => ExitApp();

            menu.Items.Add(convertItem);
            menu.Items.Add(separator);
            menu.Items.Add(exitItem);
            return menu;
        }

        private void ConvertClipboard()
        {
            try
            {
                if (!Clipboard.ContainsText())
                {
                    ShowToast("Nessun testo negli appunti", "", isError: true);
                    return;
                }

                var text = Clipboard.GetText().Trim();
                var result = TryFormatGuid(text);

                if (result == null)
                {
                    ShowToast("GUID non valido", $"\"{Truncate(text, 40)}\" non è un GUID riconoscibile", isError: true);
                    return;
                }

                Clipboard.SetText(result);
                ShowToast("GUID copiato!", result);
            }
            catch (Exception ex)
            {
                ShowToast("Errore", ex.Message, isError: true);
            }
        }

        private string? TryFormatGuid(string input)
        {
            // Remove common wrappers: {}, ()
            input = input.Trim('{', '}', '(', ')').Trim();

            // Remove all hyphens to normalize
            var clean = input.Replace("-", "").Replace(" ", "");

            if (clean.Length != 32) return null;
            if (!Regex.IsMatch(clean, @"^[0-9a-fA-F]{32}$")) return null;

            // Format as 8-4-4-4-12
            var formatted = $"{clean[..8]}-{clean[8..12]}-{clean[12..16]}-{clean[16..20]}-{clean[20..]}".ToUpper();
            return formatted;
        }

        private void ShowToast(string title, string message, bool isError = false)
        {
            try
            {
                var builder = new ToastContentBuilder()
                    .AddText(title)
                    .SetToastScenario(ToastScenario.Default);

                if (!string.IsNullOrEmpty(message))
                    builder.AddText(message);

                builder.Show(toast =>
                {
                    toast.ExpirationTime = DateTime.Now.AddSeconds(3);
                });
            }
            catch
            {
                // Fallback: balloon tip
                _trayIcon.ShowBalloonTip(2000, title, message, isError ? ToolTipIcon.Error : ToolTipIcon.Info);
            }
        }

        private static string Truncate(string s, int max) =>
            s.Length <= max ? s : s[..max] + "…";

        private static Icon CreateIcon()
        {
            // Draw a simple "G{}" icon programmatically
            var bmp = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            g.FillRectangle(new SolidBrush(Color.FromArgb(0, 120, 212)), 0, 0, 16, 16);
            g.DrawString("G", new Font("Arial", 8, FontStyle.Bold), Brushes.White, 1, 2);
            return Icon.FromHandle(bmp.GetHicon());
        }

        private void ExitApp()
        {
            _hotkeyManager.UnregisterHotKey(HOTKEY_ID);
            _hotkeyManager.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hotkeyManager?.Dispose();
                _trayIcon?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // --- Global Hotkey via Win32 ---
    enum KeyModifiers
    {
        None = 0,
        Alt = 1,
        Control = 2,
        Shift = 4,
        Windows = 8
    }

    class HotKeyEventArgs : EventArgs
    {
        public int Id { get; }
        public HotKeyEventArgs(int id) => Id = id;
    }

    class HotkeyManager : Form
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;

        public event EventHandler<HotKeyEventArgs>? HotKeyPressed;

        public bool RegisterHotKey(int id, KeyModifiers modifiers, Keys key) =>
            RegisterHotKey(Handle, id, (int)modifiers, (int)key);

        public bool UnregisterHotKey(int id) =>
            UnregisterHotKey(Handle, id);

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
                HotKeyPressed?.Invoke(this, new HotKeyEventArgs(m.WParam.ToInt32()));
            base.WndProc(ref m);
        }

        protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);
    }
}
