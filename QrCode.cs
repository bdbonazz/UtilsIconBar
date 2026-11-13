using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Forms;

namespace GuidFormatter;

/// <summary>
/// Encoder QR Code (ISO/IEC 18004) minimale, senza dipendenze esterne.
/// Modalità byte (UTF-8), versioni 1-40, livello di correzione M.
/// Algoritmo basato sull'implementazione di riferimento di Project Nayuki (MIT).
/// </summary>
sealed class QrCode
{
    public int Size { get; }
    private readonly bool[,] _modules;     // [y, x] true = scuro
    private readonly bool[,] _isFunction;
    private readonly int _version;

    // Livello M: indice tabella = 1, bit formato = 0
    private const int EclIndex = 1;
    private const int EclFormatBits = 0;

    private static readonly int[,] EccCodewordsPerBlock =
    {
        { -1,  7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 }, // L
        { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 }, // M
        { -1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 }, // Q
        { -1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 }, // H
    };

    private static readonly int[,] NumErrorCorrectionBlocks =
    {
        { -1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4,  4,  4,  4,  4,  6,  6,  6,  6,  7,  8,  8,  9,  9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25 }, // L
        { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5,  5,  8,  9,  9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 }, // M
        { -1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8,  8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68 }, // Q
        { -1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81 }, // H
    };

    public bool GetModule(int x, int y) => _modules[y, x];

    /// <summary>Codifica il testo. Restituisce null se troppo lungo per un QR (v40-M).</summary>
    public static QrCode? TryEncode(string text)
    {
        byte[] data = Encoding.UTF8.GetBytes(text);

        int version = 1;
        int dataUsedBits;
        while (true)
        {
            int ccBits = version <= 9 ? 8 : 16;
            int capacityBits = GetNumDataCodewords(version) * 8;
            dataUsedBits = 4 + ccBits + data.Length * 8;
            if (data.Length < (1 << ccBits) && dataUsedBits <= capacityBits)
                break;
            if (++version > 40)
                return null;
        }

        // Flusso di bit: modo byte (0100) + contatore + dati
        List<bool> bits = [];
        AppendBits(bits, 0x4, 4);
        AppendBits(bits, data.Length, version <= 9 ? 8 : 16);
        foreach (byte b in data)
            AppendBits(bits, b, 8);

        int capacity = GetNumDataCodewords(version) * 8;
        AppendBits(bits, 0, Math.Min(4, capacity - bits.Count)); // terminatore
        AppendBits(bits, 0, (8 - bits.Count % 8) % 8);            // allineamento al byte
        for (int pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11)
            AppendBits(bits, pad, 8);

        byte[] codewords = new byte[bits.Count / 8];
        for (int i = 0; i < bits.Count; i++)
            if (bits[i]) codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));

        return new QrCode(version, codewords);
    }

    private QrCode(int version, byte[] dataCodewords)
    {
        _version = version;
        Size = version * 4 + 17;
        _modules = new bool[Size, Size];
        _isFunction = new bool[Size, Size];

        DrawFunctionPatterns();
        DrawCodewords(AddEccAndInterleave(dataCodewords));

        // Scelta della maschera con penalità minima
        int bestMask = 0;
        int minPenalty = int.MaxValue;
        for (int mask = 0; mask < 8; mask++)
        {
            ApplyMask(mask);
            DrawFormatBits(mask);
            int penalty = GetPenaltyScore();
            if (penalty < minPenalty)
            {
                bestMask = mask;
                minPenalty = penalty;
            }
            ApplyMask(mask); // XOR: annulla
        }
        ApplyMask(bestMask);
        DrawFormatBits(bestMask);
    }

    // ---------- Pattern funzionali ----------

    private void DrawFunctionPatterns()
    {
        for (int i = 0; i < Size; i++)
        {
            SetFunctionModule(6, i, i % 2 == 0);
            SetFunctionModule(i, 6, i % 2 == 0);
        }

        DrawFinderPattern(3, 3);
        DrawFinderPattern(Size - 4, 3);
        DrawFinderPattern(3, Size - 4);

        int[] alignPos = GetAlignmentPatternPositions();
        int n = alignPos.Length;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                if (!((i == 0 && j == 0) || (i == 0 && j == n - 1) || (i == n - 1 && j == 0)))
                    DrawAlignmentPattern(alignPos[i], alignPos[j]);

        DrawFormatBits(0); // segnaposto, riscritto dopo la scelta della maschera
        DrawVersion();
    }

    private void DrawFormatBits(int mask)
    {
        int data = EclFormatBits << 3 | mask;
        int rem = data;
        for (int i = 0; i < 10; i++)
            rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        int bits = (data << 10 | rem) ^ 0x5412;

        for (int i = 0; i <= 5; i++) SetFunctionModule(8, i, GetBit(bits, i));
        SetFunctionModule(8, 7, GetBit(bits, 6));
        SetFunctionModule(8, 8, GetBit(bits, 7));
        SetFunctionModule(7, 8, GetBit(bits, 8));
        for (int i = 9; i < 15; i++) SetFunctionModule(14 - i, 8, GetBit(bits, i));

        for (int i = 0; i < 8; i++) SetFunctionModule(Size - 1 - i, 8, GetBit(bits, i));
        for (int i = 8; i < 15; i++) SetFunctionModule(8, Size - 15 + i, GetBit(bits, i));
        SetFunctionModule(8, Size - 8, true); // modulo sempre scuro
    }

    private void DrawVersion()
    {
        if (_version < 7) return;
        int rem = _version;
        for (int i = 0; i < 12; i++)
            rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
        int bits = _version << 12 | rem;

        for (int i = 0; i < 18; i++)
        {
            bool bit = GetBit(bits, i);
            int a = Size - 11 + i % 3;
            int b = i / 3;
            SetFunctionModule(a, b, bit);
            SetFunctionModule(b, a, bit);
        }
    }

    private void DrawFinderPattern(int x, int y)
    {
        for (int dy = -4; dy <= 4; dy++)
            for (int dx = -4; dx <= 4; dx++)
            {
                int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                int xx = x + dx, yy = y + dy;
                if (xx >= 0 && xx < Size && yy >= 0 && yy < Size)
                    SetFunctionModule(xx, yy, dist != 2 && dist != 4);
            }
    }

    private void DrawAlignmentPattern(int x, int y)
    {
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                SetFunctionModule(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
    }

    private void SetFunctionModule(int x, int y, bool isDark)
    {
        _modules[y, x] = isDark;
        _isFunction[y, x] = true;
    }

    private int[] GetAlignmentPatternPositions()
    {
        if (_version == 1) return [];
        int numAlign = _version / 7 + 2;
        int step = (_version * 8 + numAlign * 3 + 5) / (numAlign * 4 - 4) * 2;
        int[] result = new int[numAlign];
        result[0] = 6;
        for (int i = numAlign - 1, pos = Size - 7; i >= 1; i--, pos -= step)
            result[i] = pos;
        return result;
    }

    // ---------- Dati e correzione d'errore ----------

    private byte[] AddEccAndInterleave(byte[] data)
    {
        int numBlocks = NumErrorCorrectionBlocks[EclIndex, _version];
        int blockEccLen = EccCodewordsPerBlock[EclIndex, _version];
        int rawCodewords = GetNumRawDataModules(_version) / 8;
        int numShortBlocks = numBlocks - rawCodewords % numBlocks;
        int shortBlockLen = rawCodewords / numBlocks;

        byte[] divisor = ReedSolomonComputeDivisor(blockEccLen);
        List<byte[]> blocks = [];
        for (int i = 0, k = 0; i < numBlocks; i++)
        {
            int datLen = shortBlockLen - blockEccLen + (i < numShortBlocks ? 0 : 1);
            byte[] dat = data[k..(k + datLen)];
            k += datLen;
            byte[] ecc = ReedSolomonComputeRemainder(dat, divisor);

            byte[] block = new byte[shortBlockLen + 1];
            Array.Copy(dat, block, datLen);
            // i blocchi corti hanno un byte "vuoto" in posizione shortBlockLen - blockEccLen
            Array.Copy(ecc, 0, block, block.Length - blockEccLen, blockEccLen);
            blocks.Add(block);
        }

        List<byte> result = new(rawCodewords);
        for (int i = 0; i < blocks[0].Length; i++)
            for (int j = 0; j < blocks.Count; j++)
                if (i != shortBlockLen - blockEccLen || j >= numShortBlocks)
                    result.Add(blocks[j][i]);
        return [.. result];
    }

    private void DrawCodewords(byte[] data)
    {
        int i = 0;
        for (int right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int vert = 0; vert < Size; vert++)
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int y = upward ? Size - 1 - vert : vert;
                    if (!_isFunction[y, x] && i < data.Length * 8)
                    {
                        _modules[y, x] = GetBit(data[i >> 3], 7 - (i & 7));
                        i++;
                    }
                }
        }
    }

    private void ApplyMask(int mask)
    {
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                bool invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    7 => ((x + y) % 2 + x * y % 3) % 2 == 0,
                    _ => throw new ArgumentOutOfRangeException(nameof(mask))
                };
                if (invert && !_isFunction[y, x])
                    _modules[y, x] = !_modules[y, x];
            }
    }

    private int GetPenaltyScore()
    {
        int result = 0;

        // Regola 1: sequenze di 5+ moduli dello stesso colore (righe e colonne)
        for (int pass = 0; pass < 2; pass++)
            for (int a = 0; a < Size; a++)
            {
                int run = 1;
                for (int b = 1; b < Size; b++)
                {
                    bool cur = pass == 0 ? _modules[a, b] : _modules[b, a];
                    bool prev = pass == 0 ? _modules[a, b - 1] : _modules[b - 1, a];
                    if (cur == prev)
                    {
                        run++;
                        if (run == 5) result += 3;
                        else if (run > 5) result++;
                    }
                    else run = 1;
                }
            }

        // Regola 2: blocchi 2x2 dello stesso colore
        for (int y = 0; y < Size - 1; y++)
            for (int x = 0; x < Size - 1; x++)
            {
                bool c = _modules[y, x];
                if (c == _modules[y, x + 1] && c == _modules[y + 1, x] && c == _modules[y + 1, x + 1])
                    result += 3;
            }

        // Regola 3: pattern simili ai finder (1011101 con 4 moduli chiari a un lato)
        bool[] p1 = [true, false, true, true, true, false, true, false, false, false, false];
        bool[] p2 = [false, false, false, false, true, false, true, true, true, false, true];
        for (int pass = 0; pass < 2; pass++)
            for (int a = 0; a < Size; a++)
                for (int b = 0; b + 11 <= Size; b++)
                {
                    bool m1 = true, m2 = true;
                    for (int k = 0; k < 11 && (m1 || m2); k++)
                    {
                        bool v = pass == 0 ? _modules[a, b + k] : _modules[b + k, a];
                        if (v != p1[k]) m1 = false;
                        if (v != p2[k]) m2 = false;
                    }
                    if (m1) result += 40;
                    if (m2) result += 40;
                }

        // Regola 4: bilanciamento scuri/chiari
        int dark = 0;
        foreach (bool m in _modules)
            if (m) dark++;
        int total = Size * Size;
        int k4 = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
        result += k4 * 10;

        return result;
    }

    // ---------- Reed-Solomon su GF(2^8) ----------

    private static byte[] ReedSolomonComputeDivisor(int degree)
    {
        byte[] result = new byte[degree];
        result[degree - 1] = 1;
        int root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < result.Length; j++)
            {
                result[j] = ReedSolomonMultiply(result[j], root);
                if (j + 1 < result.Length)
                    result[j] ^= result[j + 1];
            }
            root = ReedSolomonMultiply(root, 0x02);
        }
        return result;
    }

    private static byte[] ReedSolomonComputeRemainder(byte[] data, byte[] divisor)
    {
        byte[] result = new byte[divisor.Length];
        foreach (byte b in data)
        {
            int factor = b ^ result[0];
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (int i = 0; i < result.Length; i++)
                result[i] ^= ReedSolomonMultiply(divisor[i], factor);
        }
        return result;
    }

    private static byte ReedSolomonMultiply(int x, int y)
    {
        int z = 0;
        for (int i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return (byte)z;
    }

    // ---------- Utilità ----------

    private static int GetNumRawDataModules(int ver)
    {
        int result = (16 * ver + 128) * ver + 64;
        if (ver >= 2)
        {
            int numAlign = ver / 7 + 2;
            result -= (25 * numAlign - 10) * numAlign - 55;
            if (ver >= 7) result -= 36;
        }
        return result;
    }

    private static int GetNumDataCodewords(int ver) =>
        GetNumRawDataModules(ver) / 8
        - EccCodewordsPerBlock[EclIndex, ver] * NumErrorCorrectionBlocks[EclIndex, ver];

    private static void AppendBits(List<bool> bits, int value, int length)
    {
        for (int i = length - 1; i >= 0; i--)
            bits.Add(((value >> i) & 1) != 0);
    }

    private static bool GetBit(int x, int i) => ((x >> i) & 1) != 0;

    /// <summary>Genera la bitmap con quiet zone di 4 moduli.</summary>
    public Bitmap ToBitmap(int scale, int border = 4)
    {
        int dim = (Size + border * 2) * scale;
        Bitmap bmp = new(dim, dim, PixelFormat.Format24bppRgb);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                if (_modules[y, x])
                    g.FillRectangle(Brushes.Black, (x + border) * scale, (y + border) * scale, scale, scale);
        return bmp;
    }
}

/// <summary>
/// Finestra che mostra il QR Code del testo negli appunti.
/// </summary>
class QrCodeForm : Form
{
    private readonly Bitmap _image;

    public QrCodeForm(QrCode qr, string text)
    {
        // scala tale da avere un'immagine di circa 400-600 px
        int scale = Math.Max(2, 500 / (qr.Size + 8));
        _image = qr.ToBitmap(scale);

        Text = $"QR Code — {text.Length} caratteri";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(Math.Max(_image.Width + 20, 360), _image.Height + 90);
        MinimumSize = new Size(300, 300);
        TopMost = true;

        PictureBox picture = new()
        {
            Dock = DockStyle.Fill,
            Image = _image,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.White
        };

        TextBox preview = new()
        {
            Dock = DockStyle.Bottom,
            ReadOnly = true,
            Text = text.ReplaceLineEndings(" "),
            BorderStyle = BorderStyle.FixedSingle
        };

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(4)
        };

        Button closeButton = new() { Text = "Chiudi", AutoSize = true };
        closeButton.Click += (s, e) => Close();

        Button saveButton = new() { Text = "Salva PNG…", AutoSize = true };
        saveButton.Click += (s, e) => SaveImage();

        Button copyButton = new() { Text = "Copia immagine", AutoSize = true };
        copyButton.Click += (s, e) => Clipboard.SetImage(_image);

        buttons.Controls.AddRange([closeButton, saveButton, copyButton]);

        Controls.Add(picture);
        Controls.Add(preview);
        Controls.Add(buttons);

        CancelButton = closeButton;

        Shown += (s, e) =>
        {
            TopMost = false;
            Activate();
        };
    }

    private void SaveImage()
    {
        using SaveFileDialog dialog = new()
        {
            Filter = "Immagine PNG|*.png",
            FileName = "qrcode.png"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _image.Save(dialog.FileName, ImageFormat.Png);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _image.Dispose();
        base.Dispose(disposing);
    }
}
