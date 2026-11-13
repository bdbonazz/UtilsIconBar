# GuidFormatter

Utility tray per Windows che converte GUID non formattati negli appunti.

## Esempio
- **Input:**  `E8482D7C80B24F32965E41F1B700A877`
- **Output:** `E8482D7C-80B2-4F32-965E-41F1B700A877`

Accetta anche GUID già con trattini (normalizza), con `{}` o `()`.

---

## Build (una tantum)

Prerequisito: [.NET 8 SDK](https://dotnet.microsoft.com/download)

```bash
cd GuidFormatter
dotnet publish -c Release
```

L'`.exe` autonomo (nessuna dipendenza) si trova in:
```
bin\Release\net8.0-windows\win-x64\publish\GuidFormatter.exe
```

---

## Setup

### Avvio con Windows
1. Premi `Win+R` → digita `shell:startup` → Invio
2. Copia (o crea collegamento a) `GuidFormatter.exe` nella cartella che si apre

### Pinnare nella barra delle applicazioni
Tasto destro su `GuidFormatter.exe` → **Aggiungi a "Start"** oppure
trascina il collegamento sulla taskbar.

> **Nota:** essendo un'app tray (nessuna finestra), Windows potrebbe
> non permettere il pin diretto. In quel caso crea un collegamento `.lnk`
> e pinnalo.

---

## Utilizzo

| Azione | Effetto |
|--------|---------|
| `Ctrl+Shift+G` | Converte il GUID negli appunti (ovunque tu sia) |
| Doppio clic sull'icona tray | Stessa conversione |
| Clic destro → Converti GUID | Stessa conversione |
| Clic destro → Esci | Chiude l'app |

Dopo la conversione compare una **notifica toast** con il GUID formattato,
pronto da incollare.
