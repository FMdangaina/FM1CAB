# Forza Motorsport 1 — Reverse Engineering Pack

Questo pacchetto raccoglie gli appunti e gli strumenti creati finora per il reverse engineering dei contenitori CAB e delle texture XPR di **Forza Motorsport 1 per Xbox originale**.

## Contenuto

```text
FM1_ReverseEngineering_Pack/
├── README.md
├── docs/
│   ├── CAB_format_discoveries.md
│   └── XPR0_to_JPEG_discoveries.md
├── tools/
│   ├── forza_fm1_cab.py
│   └── fm1_xpr_to_jpeg.py
├── samples/
│   ├── cab/
│   │   ├── car_toyota2000gt.cab
│   │   └── car_nissanaltima.cab
│   ├── xpr/
│   │   ├── livery0.xpr
│   │   ├── livery0hi.xpr
│   │   └── livery0nc.xpr
│   └── jpeg/
│       ├── livery0hi.jpg
│       ├── livery0hi_rgb.jpg
│       └── livery0hi_alpha.jpg
└── results/
    └── cab_extractions/
        ├── car_toyota2000gt_full.zip
        └── car_nissanaltima_full.zip
```

## Scoperte CAB confermate sui due campioni

- Firma iniziale: `AA AA C0 C0`.
- Numero record: DWORD little-endian all'offset `0x08`.
- Tabella record a `0x132`.
- Record da 24 byte, sei DWORD little-endian.
- Tabella dei percorsi immediatamente dopo i record, con stringhe ASCII terminate da NUL.
- Il quarto DWORD di ogni record (`record + 0x0C`) è un offset relativo alla tabella dei percorsi: permette di associare tutti i record ai percorsi originali.
- I payload si decomprimono con zlib; la dimensione decompressed attesa è verificata contro il terzo DWORD.
- Toyota 2000GT: 93 record, 93 associazioni risolte.
- Nissan Altima: 207 record, 207 associazioni risolte.
- Il quinto e sesto DWORD sono preservati ma la semantica non è ancora determinata.
- Queste conclusioni sono verificate su due CAB di vetture; altri tipi di CAB vanno ancora testati.

Dettagli: `docs/CAB_format_discoveries.md`.

## Stato XPR → JPEG

- Il convertitore `tools/fm1_xpr_to_jpeg.py` legge il contenitore `XPR0`.
- Decodifica DXT1, DXT3 e DXT5, oltre ad alcuni formati non compressi.
- `livery0hi.xpr` è stato convertito in JPEG 1024×1024.
- Il test RGB/alpha è stato generato dal file XPR originale. JPEG è lossy: per test precisi è preferibile aggiungere output PNG.
- `livery0.xpr` e `livery0nc.xpr` non sono ancora validati completamente.
- Il prototipo non è ancora un convertitore universale per tutte le varianti XPR Xbox.

Dettagli: `docs/XPR0_to_JPEG_discoveries.md`.

## Requisiti

- Python 3.
- Per il convertitore XPR: Pillow.

```bash
python -m pip install Pillow
```

## Esempi d'uso

### Estrarre un CAB

```bash
python tools/forza_fm1_cab.py samples/cab/car_toyota2000gt.cab -o output_toyota --zip
```

### Convertire XPR in JPEG

```bash
python tools/fm1_xpr_to_jpeg.py samples/xpr/livery0hi.xpr -o livery0hi_rebuilt.jpg
```

Elencare le mipmap previste:

```bash
python tools/fm1_xpr_to_jpeg.py samples/xpr/livery0hi.xpr --list-mips
```

## Importante

Questo è un pacchetto di ricerca, non una release stabile. Le conclusioni sono distinte tra quelle verificate sui campioni e quelle ancora ipotetiche. Conservare sempre una copia dei file originali durante i test.
