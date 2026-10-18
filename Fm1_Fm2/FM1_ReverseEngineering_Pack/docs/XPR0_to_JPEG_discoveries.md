# FM1 XPR0 → JPEG: scoperte e stato del convertitore

**Stato:** prototipo; testato su `livery0hi.xpr`  
**Tool:** `../tools/fm1_xpr_to_jpeg.py`  
**Dipendenza:** Python 3 + Pillow (`python -m pip install Pillow`)

## Campioni XPR disponibili

| File | Dimensione | Note |
|---|---:|---|
| `livery0.xpr` | 526.336 byte | XPR0; da investigare il layout della risorsa |
| `livery0hi.xpr` | 1.400.832 byte | Decodifica DXT5 testata; JPEG 1024×1024 generato |
| `livery0nc.xpr` | 2.099.200 byte | XPR0; variante con layout/dimensione differenti |

I file originali sono in `../samples/xpr/`. Le immagini di test sono in `../samples/jpeg/`.

## Struttura osservata

- Firma XPR: ASCII `XPR0` (`58 50 52 30`).
- Il campo dimensione totale è letto all'offset `0x04`; il campo dimensione header è letto a `0x08`.
- Il descrittore della risorsa comincia a `0x0C`.
- Nel campione `livery0hi.xpr`, il payload inizia a `0x800` (2048 byte).
- La parola formato/descrittore texture a `0x18` è `0x0AAB0F29`.
- Il campo formato estratto dal descrittore è `0x0F`, cioè DXT5.
- Il convertitore genera `livery0hi.jpg` a 1024×1024.
- La parola di formato include più campi bit-packed: non va interpretata come un semplice numero di mipmap.

## Comandi

Dalla cartella principale del pacchetto:

```bash
python -m pip install Pillow
python tools/fm1_xpr_to_jpeg.py samples/xpr/livery0hi.xpr
python tools/fm1_xpr_to_jpeg.py samples/xpr/livery0hi.xpr -o samples/jpeg/livery0hi_rebuilt.jpg
python tools/fm1_xpr_to_jpeg.py samples/xpr/livery0hi.xpr --list-mips
```

Per separare RGB e alpha si deve partire dal file XPR originale, non dal JPEG già generato: JPEG non conserva alpha.

## Separazione RGB / alpha

È stato generato un test per `livery0hi.xpr`:
- `livery0hi_rgb.jpg`: colori RGB;
- `livery0hi_alpha.jpg`: canale alpha in scala di grigi, bianco = opaco, nero = trasparente.

Sono JPEG con perdita, adatti a un controllo visivo rapido. Per analisi pixel-perfect usare PNG o BMP senza perdita.

## Limiti e attività aperte

- Il prototipo supporta DXT1, DXT3, DXT5 e alcuni formati RGB non compressi.
- La decodifica DXT5 del campione `livery0hi.xpr` produce un JPEG verificabile.
- I campioni `livery0.xpr` e `livery0nc.xpr` hanno un layout che non è ancora stato validato completamente; non assumere che siano semplici texture 2D standard.
- I dati residui dopo il mip selezionato non sono ancora interpretati in modo generale.
- Da verificare la corretta semantica dei campi XPR per dimensione, dimension type, profondità e livelli mip.
- Da verificare l'eventuale swizzle/tile Xbox e possibili risorse 3D o array.
- Il decoder non va considerato ancora un convertitore universale per tutti gli XPR Xbox/FM1.

## Riferimento esterno

Il progetto indicato per lo studio è [Team-Resurgent/XboxToolkit](https://github.com/Team-Resurgent/XboxToolkit), in particolare `XprUtility`. Il prototipo qui incluso è autonomo e non richiede XboxToolkit a runtime.
