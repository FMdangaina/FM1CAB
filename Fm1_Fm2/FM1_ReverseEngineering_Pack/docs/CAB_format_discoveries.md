# Forza Motorsport 1 (Xbox) --- Reverse engineering del formato CAB

**Stato:** ricerca in corso\
**Ultimo aggiornamento:** 18 ottobre 2026\
**Ambito:** archivi `.cab` di Forza Motorsport 1 per Xbox originale\
**Implementazione di riferimento:** `forza_fm1_cab.py`

## 1. Obiettivo

Documentare la struttura dei file `.cab` usati da Forza Motorsport 1
(FM1) su Xbox e costruire un tool riutilizzabile che:

-   legga e validi l'header;
-   ricostruisca l'indice originale dei percorsi;
-   associ ogni record al nome corretto;
-   decomprima i payload;
-   ricrei cartelle, nomi ed estensioni originali (`.carbin`, `.xpr` e
    gli altri file);
-   generi manifest utili per analisi successive;
-   rifiuti archivi incoerenti invece di produrre estrazioni
    apparentemente valide ma sbagliate.

L'obiettivo non è riconvertire i `.carbin` o gli `.xpr`: il lavoro qui
riguarda il contenitore CAB e la ricostruzione dell'indice. Questi
formati sono già oggetto di strumenti separati.

## 2. Campioni analizzati

Sono stati esaminati due archivi di vetture:

  Campione                                     Dimensione CAB   Record dichiarati
  ------------------------------------------ ---------------- -------------------
  Toyota 2000GT --- `car_toyota2000gt.cab`     2.629.457 byte                  93
  Nissan Altima --- `car_nissanaltima.cab`     3.458.248 byte                 207

I due campioni condividono la stessa struttura osservata. Questo è un
buon indizio che il formato sia comune almeno a questi CAB di vetture di
FM1; non dimostra ancora che tutti i CAB del gioco, inclusi quelli di
altre categorie, usino necessariamente la stessa variante.

## 3. Struttura binaria ricostruita

### 3.1 Firma

I primi quattro byte sono:

``` text
AA AA C0 C0
```

Il parser li verifica prima di procedere. Un file con firma diversa
viene rifiutato come formato non riconosciuto.

### 3.2 Numero di record

Il numero di record è letto come intero unsigned a 32 bit little-endian
all'offset `0x08`.

Nel codice attuale è indicato come `COUNT_OFFSET = 8`.

Valori osservati:

-   Toyota 2000GT: `93` (`0x5D`);
-   Nissan Altima: `207` (`0xCF`).

Il parser applica anche un controllo di plausibilità sul numero e
verifica che la tabella dei record rientri nel file.

### 3.3 Inizio e dimensione della tabella record

La tabella dei record comincia all'offset `0x132`.

Ogni record è lungo 24 byte (`0x18`) ed è interpretato come sei DWORD
unsigned little-endian:

  ------------------------------------------------------------------------
  Campo                    Offset relativo al record Interpretazione
                                                     attuale
  --------------------- ---------------------------- ---------------------
  `field1`                                   `+0x00` Offset del payload
                                                     compresso nel CAB

  `field2`                                   `+0x04` Dimensione compressa

  `field3`                                   `+0x08` Dimensione attesa
                                                     dopo decompressione

  `field4`                                   `+0x0C` Offset relativo alla
                                                     tabella dei percorsi

  `field5`                                   `+0x10` Valore preservato;
                                                     semantica non ancora
                                                     determinata

  `field6`                                   `+0x14` Valore preservato nel
                                                     manifest; semantica
                                                     non ancora
                                                     determinata
  ------------------------------------------------------------------------

La tabella occupa quindi:

``` text
inizio_tabella = 0x132
dimensione_tabella = numero_record * 24
fine_tabella = 0x132 + numero_record * 24
```

La fine della tabella è anche l'inizio della tabella dei percorsi nei
due campioni analizzati.

**Nota:** l'offset `0x132` è verificato sui due campioni disponibili.
Prima di generalizzare il formato a ogni CAB di FM1, va controllato su
ulteriori archivi.

### 3.4 Tabella dei percorsi

Subito dopo la tabella dei record è presente una sequenza di stringhe
ASCII terminate da byte NUL (`0x00`).

Ogni stringa rappresenta un percorso relativo, per esempio un percorso
sotto `cars\...` o `engines\...`.

Per ogni percorso, il parser registra:

-   indice del percorso nell'ordine della tabella;
-   offset in byte relativo all'inizio della tabella dei percorsi;
-   stringa del percorso.

La tabella è percorsa leggendo le stringhe fino al terminatore NUL, per
il numero di elementi dichiarato nell'header.

### 3.5 Collegamento tra record e percorso --- scoperta principale

Il quarto DWORD di ogni record, all'offset relativo `+0x0C`, è un
**offset in byte relativo all'inizio della tabella dei percorsi**.

Non è necessario associare i nomi tramite ordine dei record, dimensione
dei file, estensione o hash: si usa direttamente questo riferimento.

Procedura:

1.  calcolare la posizione iniziale della tabella dei percorsi;
2.  leggere le stringhe NUL-terminate e memorizzarle con il loro offset
    relativo;
3.  leggere il DWORD a `record + 0x0C`;
4.  trovare il percorso che inizia esattamente a quell'offset relativo;
5.  assegnare quel percorso al payload del record.

La corrispondenza è stata verificata come uno-a-uno su entrambi i
campioni:

  Campione          Record   Riferimenti risolti   Percorsi non associati
  --------------- -------- --------------------- ------------------------
  Toyota 2000GT         93                 93/93                        0
  Nissan Altima        207               207/207                        0

Il parser verifica inoltre che ogni riferimento sia valido e che ogni
percorso venga referenziato esattamente una volta. Se la relazione non è
uno-a-uno, l'estrazione viene interrotta.

## 4. Payload e compressione

Il primo DWORD del record è trattato come offset del payload compresso
nel file CAB; il secondo come dimensione compressa e il terzo come
dimensione attesa dopo decompressione.

I payload vengono decompressi con zlib tramite la libreria standard
Python:

``` python
payload = zlib.decompress(compressed)
```

Dopo la decompressione, la dimensione effettiva viene confrontata con il
valore memorizzato nel terzo DWORD. Se non coincide, il parser segnala
un errore e non considera valida l'estrazione.

Nei due campioni analizzati, tutti i payload sono stati decompressi e la
dimensione risultante ha corrisposto a quella attesa:

-   Toyota 2000GT: 93/93;
-   Nissan Altima: 207/207.

Questa evidenza conferma il metodo di decompressione per i campioni
testati, non necessariamente per ogni variante di CAB del gioco.

## 5. Ricostruzione dei file

Poiché il percorso viene recuperato dall'indice interno, l'estrattore
può ricreare la struttura delle cartelle e mantenere i nomi e le
estensioni originali.

Tra i tipi di file attesi o osservati nell'insieme di risorse ci sono:

-   `.carbin`;
-   `.xpr`;
-   `.livery`;
-   `.ini`;
-   `.bat`;
-   altri file specifici del gioco.

I file `.carbin` e `.xpr` non vengono interpretati internamente dal CAB
extractor: vengono estratti come payload binari originali. La loro
analisi/conversione è un passaggio successivo.

Il tool normalizza i separatori di percorso e valida i nomi prima di
scrivere su disco. Rifiuta percorsi assoluti, segmenti `..`, prefissi di
unità e caratteri di controllo, per evitare che un archivio scriva fuori
dalla cartella di destinazione.

## 6. Manifest e verifiche

Il tool genera nella cartella di estrazione:

-   `manifest.json` --- riepilogo del CAB e dettagli di ogni record;
-   `manifest.csv` --- tabella consultabile in fogli di calcolo;
-   `paths_in_table_order.txt` --- ordine dei percorsi e relativi
    offset;
-   `README.txt` --- riepilogo dell'estrazione;
-   tutti i payload, ricreati sotto i percorsi originali.

Per ogni record il manifest include:

-   indice del record;
-   offset del record nel CAB;
-   offset del payload;
-   dimensione compressa;
-   dimensione decompressa;
-   offset del percorso nella tabella;
-   quinto e sesto DWORD grezzi;
-   percorso originale;
-   percorso di output;
-   SHA-256 del payload estratto.

Lo SHA-256 consente di confrontare estrazioni, individuare duplicati e
verificare che i payload non cambino tra esecuzioni.

## 7. Tool attuale

Script: `forza_fm1_cab.py`

Dipendenze: Python 3 e sola libreria standard; non richiede pacchetti
Python esterni.

Esempi:

``` bash
python forza_fm1_cab.py car_toyota2000gt.cab
python forza_fm1_cab.py car_nissanaltima.cab -o out --zip
python forza_fm1_cab.py auto.cab -o estrazione_auto --zip
```

Argomenti:

-   `cab`: file CAB da analizzare;
-   `-o` / `--out`: cartella di output; se omessa, viene usato
    `<nome>_extracted`;
-   `--zip`: crea anche uno ZIP della struttura estratta.

Il tool stampa il numero di file estratti e i byte totali decompressi.
In caso di firma errata, dimensioni fuori dai limiti, percorso non
valido, decompressione fallita, dimensioni discordanti o associazione
record-percorso incoerente, restituisce un errore.

## 8. Stato delle scoperte

### Confermato sui due CAB testati

-   Firma iniziale `AA AA C0 C0`.
-   Numero record a `0x08`, letto come DWORD little-endian.
-   Tabella record a `0x132`.
-   Record da 24 byte, composti da sei DWORD little-endian.
-   Tabella dei percorsi immediatamente dopo la tabella record.
-   Percorsi ASCII terminati da NUL.
-   Quarto DWORD del record (`+0x0C`) come offset relativo alla tabella
    dei percorsi.
-   Primo DWORD come offset payload; secondo come dimensione compressa;
    terzo come dimensione decompressa attesa.
-   Decompressione zlib corretta per tutti i payload dei due campioni.
-   Corrispondenza uno-a-uno tra record e percorsi: 93/93 e 207/207.

### Ancora da verificare

-   Significato semantico del quinto DWORD (`+0x10`).
-   Significato semantico del sesto DWORD (`+0x14`).
-   Se `0x132` e la stessa disposizione valgano per tutte le categorie
    di CAB di FM1.
-   Se esistano varianti del contenitore con altre firme, compressioni o
    layout.
-   Se i dati dopo l'ultima stringa del path table contengano padding o
    altre strutture in alcuni archivi.
-   Eventuali vincoli di allineamento dei payload e se i blocchi possano
    sovrapporsi o condividere regioni.
-   Se ci siano casi con percorsi duplicati o più record riferiti allo
    stesso percorso in archivi non ancora testati.

## 9. Prossimi test consigliati

1.  Provare il tool su CAB di altre vetture, incluse vetture con molte
    livree o varianti.
2.  Provare CAB non relativi alle auto, se presenti, per verificare
    quanto sia generico il formato.
3.  Confrontare i campi `field5` e `field6` fra file simili e diversi,
    cercando correlazioni con dimensioni, tipi di risorsa o flag.
4.  Verificare che gli offset dei payload non invadano le tabelle di
    metadati e controllare eventuali sovrapposizioni.
5.  Confrontare hash dei payload tra archivi diversi per identificare
    risorse condivise.
6.  Costruire eventualmente una modalità `--list`/`--verify` che mostri
    l'indice e validi l'archivio senza estrarre i file.
7.  Solo dopo aver testato più varianti, considerare stabile il formato
    e aggiungere supporto esplicito a eventuali layout alternativi.

## 10. Riferimenti e artefatti del progetto

Campioni di test:

-   `car_toyota2000gt.cab` --- 2.629.457 byte, 93 record.
-   `car_nissanaltima.cab` --- 3.458.248 byte, 207 record.

Implementazione di riferimento:

-   `forza_fm1_cab.py`

Output completi prodotti durante la sessione:

-   `car_toyota2000gt_full.zip`
-   `car_nissanaltima_full.zip`

Questi nomi si riferiscono agli artefatti creati durante la sessione; il
presente documento descrive il formato e il comportamento atteso del
tool, non sostituisce i file binari originali.

## 11. Conclusione

Il formato CAB di FM1 è stato ricostruito abbastanza da consentire, sui
due archivi di vetture analizzati, un'estrazione completa con nomi e
percorsi originali. La scoperta decisiva è il riferimento relativo alla
tabella dei percorsi nel quarto DWORD di ogni record. Il passo
successivo è allargare i test ad altri CAB e identificare i campi ancora
sconosciuti, mantenendo il parser rigoroso e verificabile.
