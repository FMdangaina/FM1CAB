# Forza Motorsport 1 (Xbox): formato CAB / CA2

**Stato:** lettura ed estrazione verificate su tutti gli archivi del gioco; scrittura non ancora fatta\
**Ultimo aggiornamento:** 18 ottobre 2026\
**Tool di riferimento:** `fm1cab.cs` (C#; `fm1cab list | extract | verify | hashhunt`)\
**Dati di test:** `F:\Xenia\Games\FM1_Extracted\media\cabs\` (239 `.cab` + 4 `.ca2`)

Build: `C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /nologo /platform:x86 /optimize fm1cab.cs`
(da PowerShell; Git Bash rovina gli switch `/`).

`forza_fm1_cab.py` è la prima versione della ricerca (2 campioni). È superata da `fm1cab.cs`:
fallisce su 13 archivi (voci non compresse, `.ca2`) e ignora il percorso base.

## 1. Verifica (18/10/2026)

`fm1cab verify` su tutta la cartella `cabs`:

```
243 archivi, 0 con problemi; 48372 voci: 45492 zlib, 2880 non compresse
```

Ogni regola sotto è controllata su ogni archivio e ogni voce viene decompressa e confrontata con la
dimensione attesa (più Adler-32 per le voci zlib).

## 2. Header (0x000 - 0x131)

| Offset | Tipo | Valore | Stato |
|---|---|---|---|
| 0x00 | u32 | `AA AA C0 C0` (firma) | 243/243 |
| 0x04 | u32 | 15 (versione) | 243/243 |
| 0x08 | u32 | numero di voci | 243/243 |
| 0x0C | u32 | 1; 0 solo in `mini.cab` (unica voce, non compressa) | ipotesi: "contiene voci zlib" |
| 0x10 | u32 | dimensione del file CAB | 243/243 |
| 0x14 | u32 | dimensione "tutto scompresso" = `0x132 + n*24 + path table + somma(dimensioni)` | 243/243 |
| 0x18 | u32 | dimensione in byte della path table | 243/243 |
| 0x1C | u32 | uguale a 0x18 (forse dimensione compressa / non compressa della path table) | 243/243 |
| 0x20 | 16 byte | zero | 243/243 |
| 0x30 | u16 | 1 | 243/243 |
| 0x32 | char[256] | **percorso base**, ASCII terminato da NUL, resto a zero | 243/243 |

Il percorso base spiega perché la tabella record comincia a 0x132 (`0x32 + 0x100`). È vuoto nei CAB
auto; vale per esempio `engines\eng_cosmo\` nei 7 CAB `engine_*`, `ui\ntsc\` in `ui_NTSC.ca2`,
`nowhere\` in `mini.cab`. Il percorso completo di un file è **percorso base + percorso della voce**.

## 3. Tabella record (0x132, n x 24 byte)

| Offset | Campo | Stato |
|---|---|---|
| +0x00 | offset del payload nel CAB | 243/243 |
| +0x04 | dimensione salvata | 243/243 |
| +0x08 | dimensione reale | 243/243 |
| +0x0C | offset della stringa, relativo all'inizio della path table | 243/243, uno-a-uno |
| +0x10 | sempre 0 | 48372/48372 |
| +0x14 | **hash del percorso** | vedi sotto |

- Se dimensione salvata == dimensione reale la voce è **memorizzata non compressa**, altrimenti è uno
  stream zlib (`78 DA`) con Adler-32 finale. 2880 voci non compresse (molte in `Cars.ca2`, poi file
  `.txt` di alcune auto, `lodaudio.ini` di `car_s7`).
- Esistono file vuoti: dimensione 0, stesso offset del payload successivo (`ui\ntsc\raceoverlaywindow.ini`).
- **I record sono ordinati per hash crescente e l'hash è unico nell'archivio** (243/243): serve per la
  ricerca binaria del gioco.
- L'hash dipende dal percorso completo: lo stesso percorso relativo (`audio\lod1\l04\upgrade.ini`) ha hash
  diversi nei CAB motore con base diversa; contenuti identici con percorsi diversi hanno hash diversi.
- **Algoritmo non trovato.** Provati: CRC-32 (4 varianti), Adler-32, FNV-1/1a, djb2, sdbm, x31/x33/x65599
  e simili, one-at-a-time, ELF, rotazioni; su percorso relativo/completo/nome file, minuscolo/maiuscolo/
  originale, `\` o `/`, prefissi `d:\media\` ecc., ASCII o UTF-16, con o senza NUL, byte invertiti
  (`fm1cab hashhunt`). Serve solo per **aggiungere** file nuovi; per sostituire un file esistente si
  riusa l'hash che c'è. Prossimo passo: cercare la funzione in `default.xbe` (lookup sulla tabella record).

## 4. Path table

Subito dopo i record; `n` stringhe ASCII terminate da NUL, lunghezza totale = header 0x18 (243/243).
L'ordine è quello di inserimento dei file, cartella per cartella: **non è alfabetico** (nei CAB auto
sembra ordinato, in `Mega.ca2` e `MegaXPR.ca2` no).

## 5. Payload

Dalla fine della path table alla fine del file, uno dopo l'altro **senza padding né buchi, nell'ordine
della path table** (243/243). Nessun allineamento.

## 6. `.ca2`

Un `.ca2` è un **CAB intero compresso a blocchi**: il CAB è diviso in pezzi da 4 MiB (l'ultimo più
corto), ogni pezzo è uno stream zlib che inizia su un confine di 2048 byte, con zeri fra gli stream e
in fondo. Dentro c'è lo stesso formato CAB (le voci sono di nuovo zlib o non compresse).

| File | Blocchi | CAB interno | Voci | Contenuto |
|---|---|---|---|---|
| `Cars.ca2` | 2 | 6.555.194 | 16104 | `.xml` 15660, `.ini` 444 |
| `Mega.ca2` | 3 | 12.143.867 | 2305 | `.bin` 1132, `.xml` 671, `.carbin` 269, `.ini` 212, ... |
| `MegaXPR.ca2` | 12 | 46.173.155 | 1090 | `.xpr` |
| `ui_NTSC.ca2` | 1 | 723.097 | 323 | `.ini` (base `ui\ntsc\`) |

## 7. Contenuto (tutti gli archivi)

`.ini` 26353, `.xml` 16331, `.xpr` 1823, `.livery` 1402, `.bin` 1132, `.carbin` 503, `.xwb` 480,
`.bat` 268, `.txt` 21, `.tga` 16, `.dat` 15, senza estensione 10, poi `.xls`, `.maxdata`, `.ase`, `.html`,
`.statusxml`, `.bak`, `.old`. Molti payload sono identici fra archivi (file audio motore condivisi).

## 8. Aperto

1. Algoritmo dell'hash del record (+0x14) - necessario solo per aggiungere file.
2. Significato certo di 0x0C (un solo controesempio) e di 0x1C (sempre uguale a 0x18).
3. Scrittura: ricostruire un CAB/CA2 e provarlo in Xenia. Lo zlib .NET non rifà byte-per-byte gli
   stream originali, quindi il confronto va fatto sui payload, non sul file.
