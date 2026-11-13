using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace GuidFormatter;

/// <summary>
/// Decodifica (senza verifica della firma) un JSON Web Token.
/// </summary>
static class JwtDecoder
{
    // Claim registrati (RFC 7519) espressi come NumericDate (secondi Unix)
    private static readonly HashSet<string> DateClaims = new(StringComparer.Ordinal)
    {
        "exp", "iat", "nbf", "auth_time", "updated_at"
    };

    /// <summary>
    /// Prova a decodificare il token. Restituisce null se la stringa non è un JWT valido.
    /// </summary>
    public static List<KeyValuePair<string, string>>? TryDecodeClaims(string input)
    {
        string token = input.Trim();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = token[7..].Trim();

        // JWS compatto: header.payload.signature (signature vuota ammessa per alg=none)
        string[] parts = token.Split('.');
        if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0)
            return null;

        try
        {
            using JsonDocument header = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            if (header.RootElement.ValueKind != JsonValueKind.Object ||
                !header.RootElement.TryGetProperty("alg", out _))
                return null;

            if (parts[2].Length > 0)
                _ = Base64UrlDecode(parts[2]); // la firma deve essere almeno base64url valida

            using JsonDocument payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            if (payload.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            List<KeyValuePair<string, string>> claims = [];
            foreach (JsonProperty prop in payload.RootElement.EnumerateObject())
                claims.Add(new(prop.Name, FormatValue(prop.Name, prop.Value)));
            return claims;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static byte[] Base64UrlDecode(string s)
    {
        foreach (char c in s)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_'))
                throw new FormatException("Carattere non base64url");
        }

        string b64 = s.Replace('-', '+').Replace('_', '/');
        switch (b64.Length % 4)
        {
            case 2: b64 += "=="; break;
            case 3: b64 += "="; break;
            case 1: throw new FormatException("Lunghezza base64url non valida");
        }
        return Convert.FromBase64String(b64);
    }

    private static string FormatValue(string name, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return value.GetString() ?? "";

            case JsonValueKind.Number:
                if (DateClaims.Contains(name) && value.TryGetInt64(out long seconds))
                {
                    try
                    {
                        DateTime local = DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;
                        return $"{seconds}  ({local.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)})";
                    }
                    catch (ArgumentOutOfRangeException) { /* fuori range: mostra il numero grezzo */ }
                }
                return value.GetRawText();

            case JsonValueKind.Array:
                IEnumerable<string> items = value.EnumerateArray().Select(e =>
                    e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.GetRawText());
                return string.Join(", ", items);

            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
            case JsonValueKind.Object:
            default:
                return value.GetRawText();
        }
    }
}

/// <summary>
/// Finestra con la griglia Chiave / Valore dei claim del token.
/// </summary>
class JwtInspectorForm : Form
{
    public JwtInspectorForm(IReadOnlyList<KeyValuePair<string, string>> claims)
    {
        Text = $"Ispeziona JWT — {claims.Count} claim";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(760, 480);
        MinimumSize = new Size(400, 250);
        ShowInTaskbar = true;
        TopMost = true;

        DataGridView grid = new()
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None
        };

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Chiave",
            HeaderText = "Chiave",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
            DefaultCellStyle = { Font = new Font(grid.Font, FontStyle.Bold) }
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Valore",
            HeaderText = "Valore",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            DefaultCellStyle = { WrapMode = DataGridViewTriState.True }
        });

        foreach (KeyValuePair<string, string> claim in claims)
            grid.Rows.Add(claim.Key, claim.Value);

        Controls.Add(grid);

        KeyPreview = true;
        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
        };

        Shown += (s, e) =>
        {
            TopMost = false; // porta in primo piano solo all'apertura
            Activate();
        };
    }
}
