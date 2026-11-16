# Raport Komunikacji USB z Wyświetlaczem Segmentowym XPG (ADATA)

## 1. Identyfikacja Urządzenia (USB Device Metadata)
* **Producent / Brand**: A-DATA Technology Co., Ltd. (XPG)
* **Vendor ID (idVendor)**: `0x125F`
* **Product ID (idProduct)**: `0x7601`
* **Klasa Urządzenia**: `USB HID (0x03)` (Human Interface Device)
* **Punkt Końcowy Kontrolny (Control Endpoint)**: Endpoint 0 OUT / IN
* **Punkty Końcowe Przerwań (Interrupt Endpoints)**:
  * EP 1 IN: `0x81` (Interrupt IN, Max Packet Size: 64 bajty, Interval: 1 ms)
  * EP 3 OUT: `0x03` (Interrupt OUT, Max Packet Size: 64 bajty, Interval: 1 ms)

---

## 2. Architektura Ramek i Wielkość Ramek (Wireshark Frame Breakdown)

### A. Ramka Żądania Wysyłania Danych (SET_REPORT Request Frame)
* **Całkowity Rozmiar Ramki Wireshark**: **100 bajtów**
* **Struktura Podziału Bajtów**:
  * **28 bajtów**: USBPcap Pseudoheader (Nagłówek przechwytywania Wireshark)
  * **8 bajtów**: USB Setup Data Header (`bmRequestType`, `bRequest`, `wValue`, `wIndex`, `wLength`)
  * **64 bajty**: Właściwy ładunek raportu HID (HID Report Payload)

### B. Ramka Potwierdzenia (SET_REPORT Response Frame)
* **Całkowity Rozmiar Ramki Wireshark**: **28 bajtów** (USBPcap Pseudoheader z kodem statusu `USBD_STATUS_SUCCESS`)

### C. Parametry Żądania USB Control Transfer
* **bmRequestType**: `0x21` (Host-to-Device, Class-Specific, Recipient: Interface)
* **bRequest**: `0x09` (`SET_REPORT`)
* **wValue**: `0x0207` (Report Type: `Output (2)`, Report ID: `7`)
* **wIndex**: `0x0000` (Interface 0)
* **wLength**: `64` (Długość ładunku = 64 bajty)
* **Częstotliwość Odświeżania**: `1 Hz` (Przesyłanie ramek co 1000 ms / 1 sekundę)

---

## 3. Mapa Struktury Ładunku Raportu HID (64 Bajty Payload)

Każda ramka wyjściowa przesłana do wyświetlacza segmentowego XPG ma długość 64 bajtów:

| Indeks Bajtu | Wartość Hex (Przykładowa) | Opis / Przeznaczenie |
| :--- | :--- | :--- |
| **0** | `0x07` | **Report ID** (Stała identyfikująca raport = 7) |
| **1** | `0xA1` | **Command Magic Byte** (Kod polecenia aktualizacji wyświetlacza) |
| **2** | `0x01` | **Version / Mode Flag** (Wersja protokołu / tryb segmentu) |
| **3 - 5** | `0x00 0x00 0x00` | **Reserved / Padding** (Wypełnienie zerami) |
| **6** | `0x0A` | **Telemetry Marker 1** (Znacznik wartości telemetrii) |
| **7** | `0x28` .. `0x2E` | **Sensor Value 1** (np. Temperatura CPU w °C: `0x28` = 40°C, `0x2E` = 46°C) |
| **8** | `0x0A` | **Telemetry Marker 2** |
| **9** | `0x00` .. `0x0C` | **Sensor Value 2** (np. Odczyt fan speed / status: `0x00`, `0x06`, `0x0C`) |
| **10** | `0x14` | **System Parameter** (`0x14` = 20 dec) |
| **11 - 13** | `0x07 0x07 0x07` | **Status / Control Flags** |
| **14 - 63** | `0x00 * 50` | **Zero Padding** (Wypełnienie zerami do pełnych 64 bajtów) |

---

## 4. Wypróbowane Przykłady Ramek (Hex Dumps)

### Przykładowa Ramka #1 (CPU Temp = 40°C):
```
07 a1 01 00 00 00 0a 28 0a 00 14 07 07 07 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
```

### Przykładowa Ramka #2 (CPU Temp = 46°C, Status = 12):
```
07 a1 01 00 00 00 0a 2e 0a 0c 14 07 07 07 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
```

---

## 5. Wygenerowane Pliki dla Gemini-Flash-Light

Przechwycone wyniki i struktury zostały zapisane w następujących plikach w katalogu projektu:

1. `xpg_display_capture.pcapng`
   * Binarny plik przechwytu Wireshark / USBPcap zawierający surowe pakietowe dane USB.
2. `xpg_display_packets.json`
   * Pełny zrzut strukturalny JSON wygenerowany bezpośrednio z narzędzia `tshark`.
3. `xpg_display_payloads.json`
   * Wyekstrahowane dane ładunków ramek wraz ze znacznikami czasu, rozmiarem ramek i tablicami bajtów.
4. `xpg_display_protocol_summary.json`
   * Kompaktowe podsumowanie protokołu z mapą bajtów zoptymalizowane do szybkiego przetwarzania przez **Gemini-Flash-Light**.
5. `xpg_display_communication.md`
   * Niniejszy dokument opisowy w formacie Markdown.
