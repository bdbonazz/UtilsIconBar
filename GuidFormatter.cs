using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

namespace GuidFormatter;

static class Program
{
    [STAThread]
    static void Main()
    {
        using Mutex mutex = new(true, "GuidFormatterApp", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("GuidFormatter è già in esecuzione nel tray.", "GuidFormatter",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApplicationContext());
    }
}

partial class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly HotkeyManager _hotkeyManager;
    private const int HOTKEY_ID = 1;

    public TrayApplicationContext()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "GUID Formatter — Ctrl+Shift+G",
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };
        _trayIcon.DoubleClick += (s, e) => ConvertClipboard();

        _hotkeyManager = new HotkeyManager();
        _hotkeyManager.RegisterHotKey(HOTKEY_ID, KeyModifiers.Control | KeyModifiers.Shift, Keys.G);
        _hotkeyManager.HotKeyPressed += (s, e) =>
        {
            if (e.Id == HOTKEY_ID) ConvertClipboard();
        };
    }

    private ContextMenuStrip BuildContextMenu()
    {
        ContextMenuStrip menu = new();

        ToolStripMenuItem convertItem = new("Converti GUID  (Ctrl+Shift+G)")
        {
            Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold)
        };
        convertItem.Click += (s, e) => ConvertClipboard();

        ToolStripMenuItem jwtItem = new("Ispeziona JWT");
        jwtItem.Click += (s, e) => InspectJwt();

        ToolStripMenuItem qrItem = new("Genera QR Code");
        qrItem.Click += (s, e) => ShowQrCode();

        ToolStripMenuItem exitItem = new("Esci");
        exitItem.Click += (s, e) => ExitApp();

        menu.Items.Add(convertItem);
        menu.Items.Add(jwtItem);
        menu.Items.Add(qrItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);
        return menu;
    }

    private void ConvertClipboard()
    {
        try
        {
            if (!Clipboard.ContainsText())
            {
                Notify("Nessun testo negli appunti", "", ToolTipIcon.Warning);
                return;
            }

            string text = Clipboard.GetText().Trim();
            string? result = TryFormatGuid(text);

            if (result is null)
            {
                Notify("GUID non valido", Truncate(text, 50), ToolTipIcon.Error);
                return;
            }

            Clipboard.SetText(result);
            Notify("GUID copiato!", result, ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Notify("Errore", ex.Message, ToolTipIcon.Error);
        }
    }

    private void InspectJwt()
    {
        try
        {
            if (!Clipboard.ContainsText())
            {
                Notify("Nessun testo negli appunti", "", ToolTipIcon.Warning);
                return;
            }

            string text = Clipboard.GetText().Trim();
            var claims = JwtDecoder.TryDecodeClaims(text);

            if (claims is null)
            {
                Notify("JWT non valido", Truncate(text, 50), ToolTipIcon.Error);
                return;
            }

            JwtInspectorForm form = new(claims);
            form.FormClosed += (s, e) => form.Dispose();
            form.Show();
        }
        catch (Exception ex)
        {
            Notify("Errore", ex.Message, ToolTipIcon.Error);
        }
    }

    private void ShowQrCode()
    {
        try
        {
            if (!Clipboard.ContainsText())
            {
                Notify("Nessun testo negli appunti", "", ToolTipIcon.Warning);
                return;
            }

            string text = Clipboard.GetText();
            if (string.IsNullOrEmpty(text))
            {
                Notify("Nessun testo negli appunti", "", ToolTipIcon.Warning);
                return;
            }

            QrCode? qr = QrCode.TryEncode(text);
            if (qr is null)
            {
                Notify("Testo troppo lungo", "Il QR Code supporta al massimo 2331 byte (UTF-8).", ToolTipIcon.Error);
                return;
            }

            QrCodeForm form = new(qr, text);
            form.FormClosed += (s, e) => form.Dispose();
            form.Show();
        }
        catch (Exception ex)
        {
            Notify("Errore", ex.Message, ToolTipIcon.Error);
        }
    }

    private static string? TryFormatGuid(string input)
    {
        input = input.Trim('{', '}', '(', ')').Trim();
        string clean = input.Replace("-", "").Replace(" ", "");

        return clean.Length != 32
            ? null
            : !MyRegex().IsMatch(clean)
            ? null
            : $"{clean[..8]}-{clean[8..12]}-{clean[12..16]}-{clean[16..20]}-{clean[20..]}".ToUpper();
    }

    private void Notify(string title, string message, ToolTipIcon icon) => _trayIcon.ShowBalloonTip(2500, title, message, icon);

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private static Icon CreateIcon()
    {
        Bitmap bmp = new(16, 16);
        using Graphics g = Graphics.FromImage(bmp);
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

    [GeneratedRegex(@"^[0-9a-fA-F]{32}$")]
    private static partial Regex MyRegex();
}

[Flags]
enum KeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

class HotKeyEventArgs(int id) : EventArgs
{
    public int Id { get; } = id;
}

class HotkeyManager : Form
{
#pragma warning disable SYSLIB1054 // Usare 'LibraryImportAttribute' invece di 'DllImportAttribute' per generare codice di marshalling di P/Invoke in fase di compilazione
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
#pragma warning restore SYSLIB1054 // Usare 'LibraryImportAttribute' invece di 'DllImportAttribute' per generare codice di marshalling di P/Invoke in fase di compilazione

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