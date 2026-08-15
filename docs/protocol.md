# Spesifikasi Protokol Komunikasi CTRL — `ctrl-protocol`

Status: v1 — seluruh keputusan desain final (siap sebagai kontrak implementasi)
Versi protokol dokumen: 1.0
Dokumen terkait: `docs/analisis-teknis.md`

---

## 1. Tujuan protokol

`ctrl-protocol` adalah kontrak jaringan antara **CTRL Mobile** (Android) dan
**CTRL Desktop** (Windows) yang mendefinisikan bagaimana perangkat mobile
mengirim masukan kontrol deck secara real-time ke PC, serta bagaimana kedua
sisi menyinkronkan profil, layout, dan status.

Prinsip yang mengikat seluruh spesifikasi:

1. **Mobile hanya mengirim `controlId` semantik + nilai input.**
   Mobile tidak pernah mengirim key code Windows, button index XInput, atau
   konsep API input apa pun.
2. **Desktop yang melakukan input mapping.** Binding `controlId → output`
   (keyboard/mouse/gamepad) diputuskan di sisi desktop.
3. **Protokol harus versioned** dan bisa berkembang (additive) tanpa merusak
   client lama.
4. **Event button down dan button up tidak boleh hilang.** Transport harus
   reliable; kehilangan satu event press/release dianggap kegagalan.
5. **Desktop wajib bisa membersihkan (flush) seluruh state input** saat
   koneksi terputus agar tidak ada stuck key/stuck gamepad.
6. Protokol hanya menyediakan **abstraksi button/axis/trigger/stick** untuk
   gamepad — tidak terikat pada driver/API tertentu (mis. ViGEm).

Protokol ini netral platform; spec ini adalah satu-satunya sumber kebenaran
(codec Dart dan C# diturunkan darinya).

## 2. Transport layer

- **Transport: raw TCP** di jaringan lokal, satu koneksi per perangkat.
- Socket server desktop wajib mengaktifkan **TCP_NODELAY** (disable Nagle)
  di kedua arah untuk menghindari penundaan pengiriman frame kecil.
- Koneksi berarah *client=CTRL Mobile, server=CTRL Desktop*. Mobile yang
  memulai koneksi; desktop hanya mendengarkan.
- Port default: **TCP 42123** (dapat dikonfigurasi; lihat §24).
- Discovery tidak termasuk protokol ini, namun protokol ini memakai port
  yang sama yang diiklankan discovery.
- mDNS discovery (`_ctrl._tcp.local`) adalah **milestone implementasi
  lanjutan** (di luar M0/M1). **M0 wajib mendukung koneksi manual IP:port**
  (pengguna memasukkan alamat PC); mDNS hanya menambah kenyamanan discovery
  dan bukan prasyarat fungsional v1.
- **UDP dan WebSocket TIDAK digunakan di v1.** (Reserved untuk masa depan.)
- Koneksi TCP adalah stream: tidak ada garansi batas baca. Receiver harus
  buffer sampai satu frame utuh berdasarkan `PayloadLength` (lihat §5).

## 3. Connection lifecycle

State machine di sisi client (mobile):

```
CLOSED ──TCP connect──► CONNECTED ──kirim HELLO──► WAIT_WELCOME
    ▲                        │                         │
    │                        │ (gagal)                 │ (WELCOME terima)
    │                        ▼                         ▼
    │                     RECONNECTING ◄──── WAIT_AUTH ──kirim AUTH
    │                        │  (backoff)               │
    │                        │                          ▼
    └──────DISCONNECT ◄──── READY ◄──AUTH_OK─── WAIT_AUTH_OK
                                   │
                                   └── kirim INPUT_SNAPSHOT pertama
```

Urutan pesan pada satu sesi baru (wajib, tanpa pengecualian):

1. Client membuka TCP dan mengirim **HELLO**.
2. Server membalas **WELCOME** (berisi versi efektif, `sessionId`,
   `challenge`, `authRequired`).
3. Client mengirim **AUTH** (buktikan kepemilikan token persisten via
   `challengeResponse`, ATAU kirim kode pairing sekali pakai +
   `challengeResponse`).
4. Server membalas **AUTH_OK** (dan `newToken` bila menggunakan kode pairing)
   atau **AUTH_DENIED**.
5. Setelah AUTH_OK, sesi masuk status **READY**. Client wajib segera
   mengirim **INPUT_SNAPSHOT** berisi seluruh state kontrol saat ini.
6. Setelah itu, pertukaran pesan normal (input, heartbeat, status, profil).

Aturan tambahan:

- Client TIDAK boleh mengirim pesan application-plane sebelum menerima
  AUTH_OK. Pelanggaran → server kirim ERROR `not-authenticated` + tutup.
- Server hanya mengizinkan **satu sesi aktif per `deviceId`**. AUTH_OK dari
  `deviceId` yang sudah punya sesi aktif akan menutup (dan flush) sesi lama.
- Setiap koneksi mendapat `sessionId` baru (16 byte acak).

## 4. Protocol version

- Versi protokol berupa pasangan `(major, minor)`.
- **Major berubah = breaking** (perubahan layout frame, makna field, atau
  penambahan message type wajib-dipahami). **Minor berubah = additive**
  (type baru yang boleh diabaikan, field opsional baru).
- Client mengiklankan versi maksimum yang didukungnya di **HELLO**.
- Server menentukan **versi efektif** dan menyampaikannya di **WELCOME**.
- Aturan negosiasi:
  - `clientMajor == serverMajor` → OK; `minor = min(clientMinor, serverMinor)`.
  - `clientMajor > serverMajor` → **tolak** (ERROR `protocol-version-mismatch`
    berisi versi server); client terlalu baru.
  - `clientMajor < serverMajor` → diterima jika
    `clientMajor >= server.minSupportedMajor`, dengan server berjalan pada
    major client (mode kompatibilitas). Jika tidak → tolak.
- Setiap pesan membawa `VersionMajor`/`VersionMinor` di header (frame-version);
  kedua field itu mencerminkan versi efektif yang dinegosiasikan.

## 5. Message framing

- Satu pesan = **header tetap 18 byte + payload (0–65535 byte)**.
- Framing berbasis panjang: `PayloadLength` di header menentukan jumlah byte
  payload; TIDAK ada delimiter antar pesan.
- Pembacaan: buffer sampai 18 byte header → validasi → baca `PayloadLength`
  byte payload → proses → lanjut.
- Setiap frame TIDAK dijamin tiba dalam satu `read()`; receiver wajib
  menangani partial reads.
- Semua integer multi-byte **big-endian (network byte order)**; string
  **UTF-8** dengan prefix panjang 1 byte (kecuali dinyatakan lain); floating
  point **IEEE-754 single (float32)**.

## 6. Message types

| Type | Nama | Arah | Wajib-dipahami | Isi payload (ringkas) |
|---|---|---|---|---|
| `0x01` | HELLO | C→S | ya | deviceId, clientVersion, versi, capabilities |
| `0x02` | WELCOME | S→C | ya | serverName, versi efektif, sessionId, challenge |
| `0x03` | AUTH | C→S | ya | credentialType, credential (kode pairing saja), deviceId, challengeResponse |
| `0x04` | AUTH_OK | S→C | ya | result, sessionId, capabilities server, newToken (opsional) |
| `0x05` | AUTH_DENIED | S→C | ya | reason, pesan |
| `0x06` | INPUT_EVENT | C→S | tidak | satu event record (button/axis/stick/trigger/hat) |
| `0x07` | INPUT_SNAPSHOT | C→S | tidak | daftar event record (state penuh) |
| `0x08` | INPUT_RESET | S→C | tidak | reason (perintah reset state input) |
| `0x09` | HEARTBEAT | C→S | tidak | clientSendTime |
| `0x0A` | PONG | S→C | tidak | clientSendTime (echo) + serverTime |
| `0x0B` | ACK | dua arah | tidak | ackedSequence + ackTime |
| `0x0C` | ERROR | dua arah | ya | code, severity, pesan |
| `0x0D` | DISCONNECT | dua arah | ya | reason |
| `0x0E` | PROFILE_LIST_REQ | C→S | ya | (kosong) |
| `0x0F` | PROFILE_LIST | S→C | ya | daftar profil (JSON) |
| `0x10` | PROFILE_SELECT | C→S | ya | profileId |
| `0x11` | PROFILE_SELECTED | S→C | ya | result |
| `0x12` | CONFIG_PUSH | dua arah | ya | profileId, revision, kind + JSON (layout/bindings/settings) |
| `0x13` | STATUS | S→C | tidak | status ringkas (JSON) |
| `0x14` | GAMEPAD_STATUS | S→C | tidak | status gamepad virtual (JSON, abstrak) |
| `0x15–0xEF` | — | — | — | **reserved** untuk penambahan minor |
| `0xF0–0xFF` | — | — | — | reserved untuk diagnostic/testing |

Catatan:
- "Wajib-dipahami" = penerima yang tidak mengenali type ini HARUS kirim
  ERROR `unsupported-message` + tutup koneksi (karena menerima pesan seperti
  ini menandakan major tidak kompatibel). Type yang tidak wajib-dipahami
  diabaikan (hanya dicatat di log).
- INPUT_EVENT hanya membawa **satu** event di v1. Batching menjadi `0x15`
  reserved untuk minor masa depan.

## 7. Message header

18 byte, big-endian:

| Offset | Ukuran | Field | Deskripsi |
|---|---|---|---|
| 0 | 2 | `Magic` | `0x43 0x54` ("CT") |
| 2 | 1 | `VersionMajor` | versi efektif major |
| 3 | 1 | `VersionMinor` | versi efektif minor |
| 4 | 1 | `Flags` | lihat bit di bawah |
| 5 | 1 | `MessageType` | lihat §6 |
| 6 | 2 | `PayloadLength` | uint16 BE, 0–65535 |
| 8 | 2 | `Sequence` | uint16 BE, urutan per arah |
| 10 | 8 | `Timestamp` | uint64 BE, ms sejak epoch |

`Flags` (bit, dari LSB):

| Bit | Nama | Deskripsi |
|---|---|---|
| 0 (0x01) | `ACK_REQUESTED` | penerima wajib membalas ACK |
| 1 (0x02) | `SECURE` | **reserved; harus 0 di v1** (jika 1 → ERROR). Cadangan untuk session security layer TLS 1.3 (§22) |
| 2 (0x04) | `COMPRESSED` | **reserved; harus 0 di v1** (jika 1 → ERROR) |
| 3 (0x08) | `MUST_UNDERSTAND` | lihat aturan §6 |
| 4–7 | — | reserved; harus 0 |

`Sequence`: counter per arah, dimulai dari 0 tepat setelah AUTH_OK, naik 1
per pesan terkirim, modulo 2^16 (membungkus). Digunakan untuk korelasi ACK
dan deteksi anomali (duplikat/bolak-balik dicatat, tidak fatal).

`Timestamp`: dipakai pengukuran latensi dan keperluan diagnostik; nilai tidak
disinkronkan jam antar perangkat (tidak dipakai untuk urutan).

## 8. Event input — button (digital)

Event record (dipakai INPUT_EVENT dan INPUT_SNAPSHOT), varian button:

```
1 byte   controlIdLength   (1..64)
N byte   controlId         (UTF-8; id unik kontrol dalam layout aktif)
1 byte   kind              = 0x00 (button)
1 byte   flags             bit0 (0x01) stateChanged; bit1 (0x02) initial; sisa 0
1 byte   state             0x00 = up, 0x01 = down
2 byte   pressCount        uint16 BE; naik 1 setiap press (down) baru
```

Aturan:

- **Down dan up TIDAK boleh hilang.** Transport TCP menjamin pengiriman;
  `pressCount` menjadi alat deteksi kehilangan (lihat §15).
- `pressCount` ditetapkan saat event `down` dan di-echo pada `up` yang
  bersangkutan. Nilai monoton naik per kontrol.
- `stateChanged=1` menandai transisi state; `=0` untuk pengulangan/refresh
  (jarang untuk button; umum untuk axis — lihat §9).
- `initial=1` hanya berlaku untuk entri di dalam INPUT_SNAPSHOT.

Ukuran record button: 6 + `controlIdLength` byte (14 byte untuk id 8 karakter).

## 9. Event input — axis, stick, trigger, hat

Varian sama header event record; `kind` membedakan encoding.

### axis (`kind = 0x01`) dan trigger (`kind = 0x03`)

```
1 byte  controlIdLength
N byte  controlId
1 byte  kind          (0x01 axis | 0x03 trigger)
1 byte  flags
4 byte  value         float32 BE, ternormalisasi 0.0–1.0, harus finite
```

Semantik: `axis` = sumbu generik; `trigger` = axis dengan makna gamepad
trigger (0 = lepas, 1 = penuh). Encoding sama. Client HARUS terus mengirim
nilai saat berubah; `stateChanged=1` saat nilai berubah.

### stick (`kind = 0x02`)

```
1 byte  controlIdLength
N byte  controlId
1 byte  kind          = 0x02 (stick)
1 byte  flags
4 byte  x             float32 BE, -1.0..1.0
4 byte  y             float32 BE, -1.0..1.0
```

Center = (0,0). Nilai harus finite; di luar rentang diklamp ke rentang.

### hat / d-pad (`kind = 0x04`)

```
1 byte  controlIdLength
N byte  controlId
1 byte  kind          = 0x04 (hat)
1 byte  flags
1 byte  value         0=center 1=N 2=NE 3=E 4=SE 5=S 6=SW 7=W 8=NW
```

Ukuran record: axis/trigger = 7 + N; stick = 11 + N; hat = 4 + N byte.

Protokol ini TIDAK menyebut driver gamepad apa pun (ViGEm, dsb.) — kind di
atas adalah abstraksi output; desktop yang memutuskan pemetaannya.

## 10. Heartbeat

- Client mengirim **HEARTBEAT** setiap **1000 ms** saat READY.
- Payload: `clientSendTime` (8 byte uint64 BE, ms).
- Server membalas **PONG** dengan `clientSendTime` (echo) + `serverTime`.
- Latency RTT client = `now - clientSendTime` (dipakai latency HUD).
- Server menganggap koneksi mati jika **tidak menerima HEARTBEAT selama
  3000 ms** (≈ 3 miss) → mark disconnect + flush (§14, §16).
- Heartbeat TIDAK memakai `ACK_REQUESTED` (PONG adalah jawaban naturalnya).

## 11. Acknowledgement

- `ACK` dikirim HANYA bila pesan masuk men-set flag `ACK_REQUESTED`.
- Payload ACK: `ackedSequence` (2 byte, seq pesan yang di-ack) +
  `ackTime` (8 byte, ms).
- Pesan yang wajib memakai `ACK_REQUESTED`: **CONFIG_PUSH** (dan jenis
  kontrol lain yang tidak punya jawaban natural).
- Pesan yang punya jawaban natural TIDAK memakai ACK: HELLO→WELCOME,
  AUTH→AUTH_OK/AUTH_DENIED, PROFILE_SELECT→PROFILE_SELECTED.
- Pengirim CONFIG_PUSH yang tidak menerima ACK dalam 3000 ms:
  ulangi 1×; jika masih gagal → anggap koneksi terganggu (masuk alur
  reconnect). Input event TIDAK pernah di-ack (jalur hot path).

## 12. Pairing dan authentication

Satu pesan AUTH menangani dua situasi (pairing pertama dan koneksi ulang),
dengan format payload yang sama:

```
1 byte  credentialType      0x01 token | 0x02 pairingCode
1 byte  credentialLength    (0x00 untuk token; panjang kode untuk pairing)
N byte  credential          HANYA diisi untuk pairingCode (6 digit ATAU payload QR, UTF-8)
1 byte  deviceIdLength
M byte  deviceId
32 byte challengeResponse
```

**V1 challenge-response (tanpa KDF):**

```
challengeResponse = HMAC-SHA256(sharedSecret, challenge)
```

- `challenge` = 32 byte acak dari WELCOME (baru per sesi → anti replay).
- `sharedSecret` = **byte mentah kredensial**: token persisten (untuk
  `credentialType=0x01`) ATAU kode pairing (untuk `0x02`). **TIDAK memakai
  HKDF/KDF apa pun di v1.**

### A. Koneksi ulang — token persisten (`credentialType=0x01`)

- **Token TIDAK pernah dikirim lewat wire.** `credentialLength=0`,
  `credential` kosong. Client mengirim `deviceId` + `challengeResponse`;
  server mencari token tersimpan untuk `deviceId`, menghitung
  `HMAC-SHA256(token, challenge)`, lalu membandingkannya. Eavesdropper pasif
  tidak bisa memakai ulang token.
- Sukses → AUTH_OK **tanpa** `newToken` (`newTokenLength=0`).

### B. Koneksi pertama — kode pairing sekali pakai (`credentialType=0x02`)

- Kode dikirim sebagai `credential` (6 digit ATAU payload QR). Eksposur
  dibatasi: **sekali pakai + TTL 300 detik**.
- Payload QR memakai **CTRL pairing URI ber-versi** (grammar wajib
  didokumentasikan sebelum implementasi, §24.16). **`sharedSecret` TIDAK
  diletakkan langsung di QR plaintext** kecuali model keamanan eksplisit
  memerlukannya; QR cukup membawa kode pairing sekali pakai + alamat server.
- `challengeResponse = HMAC-SHA256(pairingCode, challenge)` — server
  memverifikasi memakai kode yang diterima.
- Sukses → AUTH_OK menyertakan `newToken` (token persisten baru) untuk
  disimpan client; kode langsung tidak berlaku lagi. Gagal → AUTH_DENIED
  (reason `bad-credential`, `expired-code`, `device-limit`).

### Umum

- HMAC di atas HANYA membuktikan kepemilikan kredensial pada sesi ini;
  ia **tidak** memberi integrity/confidentiality pada aliran input setelah
  AUTH_OK (lihat §22 — batasan sementara tahap pengembangan).
- Batas gagal auth: **5 percobaan gagal → kunci 30 detik** (per IP/deviceId).

Pesan AUTH_OK:

```
1 byte   result                  0x00 = ok
16 byte  sessionId               echo dari WELCOME
4 byte   serverCapabilities      uint32 BE
1 byte   newTokenLength          0 = tidak ada
N byte   newToken                hanya saat pairing sukses
```

Pesan AUTH_DENIED:

```
1 byte   reason               0x01 bad-credential | 0x02 expired-code | 0x03 device-limit
1 byte   messageLength        0–255 (0 = tanpa pesan)
N byte   message              UTF-8, bebas debug (TIDAK boleh berisi token/kode)
```

Aturan AUTH_DENIED:

- `reason` tidak dikenal → pesan ditolak (ERROR `invalid-message` + tutup).
- `messageLength` boleh 0 (pesan kosong).
- Payload truncated, UTF-8 tidak valid, atau byte tambahan setelah `message`
  → pesan ditolak. Tidak ada field lain selain `reason`, `messageLength`,
  dan `message`.

Client wajib menyimpan `newToken` dengan aman (mis. Android Keystore).
Server menyimpan token ter-hash (bukan plaintext).

## 13. Reconnect behavior

- Deteksi putus: socket error/close/RST ATAU timeout heartbeat 3000 ms.
- Client pindah ke state **RECONNECTING**: backoff eksponensial
  `500ms, 1s, 2s, 4s, …` dengan jitter acak ±20%, **cap 30 s**; coba terus
  sampai sukses atau pengguna membatalkan.
- Saat koneksi baru berhasil (HELLO → AUTH memakai token persisten, token
  tidak dikirim lewat wire):
  - Server menutup & **flush** sesi lama `deviceId` yang sama bila masih ada.
  - Client menerima AUTH_OK → **mengirim INPUT_SNAPSHOT penuh** state saat
    ini (lihat §15) → sesi siap.
- Server TIDAK menganggap sesi selesai saat reconnect; ia menunggu
  timeout/flush §14.

## 14. Disconnect behavior

Pemicu disconnect (client atau server):

1. **Graceful:** salah satu sisi mengirim **DISCONNECT** (payload reason:
   `0x00` normal, `0x01` app-closing, `0x02` server-restart, `0x03`
   idle-timeout, `0x04` security, `0x05` protocol-violation) lalu menutup TCP.
2. **Ungraceful:** TCP close/RST/timeout heartbeat (server) — tidak ada pesan
   DISCONNECT.

Saat koneksi terputus (apa pun pemicunya), server **WAJIB segera flush**
seluruh state input (§16). Client berhenti mengirim, menutup socket, dan
masuk state RECONNECTING (kecuali pengguna memilih berhenti).

## 15. Input state synchronization

- **INPUT_SNAPSHOT** = daftar event record yang menggambarkan **seluruh
  state kontrol yang sedang aktif** (semua button down, semua nilai axis/
  stick/trigger saat ini). Entry snapshot men-set `flags.initial=1`.
- Waktu pengiriman:
  1. Wajib: segera setelah AUTH_OK (resync penuh).
  2. Wajib: setelah reconnect (bagian dari §13).
  3. Opsional: berkala 1000 ms sekali untuk koreksi drift (default mati).
- Format payload:

```
2 byte   entryCount     uint16 BE, 1–1024
...      event records  (encoding sama persis dengan INPUT_EVENT)
```

- Server mengganti state aktif dari snapshot (bukan menambahkan), lalu
  mendiff-nya terhadap output nyata agar hanya mengeluarkan press/release
  yang diperlukan.
- Deteksi kehilangan event: jika `pressCount` yang diterima melompat > 1
  untuk kontrol yang sama, server menandai state kontrol itu tak pasti dan
  meminta resync (mengirim `INPUT_RESET`; client balas INPUT_SNAPSHOT).

## 16. Input flush setelah disconnect/reconnect

**Flush = desktop mengembalikan semua output virtual ke netral** (semua
tombol/klik dilepas, stick di tengah, trigger ke 0), tanpa bergantung pada
driver tertentu (mis. "reset semua perangkat virtual ke netral").

Pemicu wajib flush:

1. Koneksi terputus (graceful maupun tidak) — server segera melepas semua
   input yang sedang aktif. Tidak ada pesan yang mungkin dikirim (koneksi
   sudah mati); ini murni aksi internal server.
2. Sesi lama ditutup oleh reconnect `deviceId` yang sama (§13).
3. Pergantian profil aktif (state input lama tidak berlaku).
4. **INPUT_RESET** (S→C) — server meminta client memperlakukan seluruh
   state sebagai released sampai client mengirim snapshot/event baru.
   Payload reason: `0x00` state-reset, `0x01` profile-switch, `0x02`
   maintenance. Biasanya diikuti request INPUT_SNAPSHOT.

Urutan pasca reconnect (ringkas): AUTH_OK → (server flush internal dari
state lama) → client kirim INPUT_SNAPSHOT → server terapkan → input berjalan.

## 17. Error messages

Format payload ERROR:

```
1 byte  code
1 byte  severity    0=info 1=warn 2=fatal
2 byte  messageLength  uint16 BE
N byte  message      UTF-8, bebas debug (TIDAK boleh berisi token/kode)
```

| code | Nama | Severity umum | Makna |
|---|---|---|---|
| `0x01` | protocol-version-mismatch | fatal | versi tidak kompatibel; `message` berisi versi server |
| `0x02` | auth-failed | fatal | kredensial ditolak |
| `0x03` | not-authenticated | fatal | pesan application-plane sebelum AUTH_OK |
| `0x04` | device-limit | warn | satu sesi aktif per deviceId; sesi lama diambil alih |
| `0x05` | payload-too-large | fatal | melebihi batas §20 |
| `0x06` | invalid-message | fatal | struktur payload tidak valid |
| `0x07` | unsupported-message | fatal | type wajib-dipahami yang tidak dikenal |
| `0x08` | forbidden | fatal | reserved flag / kebijakan keamanan |
| `0x09` | server-shutdown | info | server berhenti |
| `0xFF` | internal | warn/fatal | error tak terduga |

ERROR biasanya diikuti penutupan koneksi (kecuali severity `info`).

## 18. Protocol compatibility / versioning

- Semua perubahan pada spesifikasi ini:
  - **Minor** (additive): type baru `0x15+` yang boleh diabaikan, field
    opsional, pesan STATUS/GAMEPAD_STATUS diperluas. Client lama tetap
    berfungsi.
  - **Major** (breaking): perubahan header, makna field, atau menambah
    type wajib-dipahami baru — WAJIB menaikkan major dan memperbarui
    negosiasi §4.
- Aturan penerima:
  - Type tidak dikenal + `MUST_UNDERSTAND` → ERROR `unsupported-message`
    + tutup.
  - Type tidak dikenal tanpa `MUST_UNDERSTAND` → abaikan (log).
  - Nilai enum tidak dikenal di payload (kind, reason, errorCode, state,
    hat) → tolak pesan: kontrol-plane → ERROR `invalid-message` + tutup;
    input-plane → buang event (log).
  - Reserved flag (`SECURE`, `COMPRESSED`) yang bernilai 1 di v1 →
    ERROR `forbidden` + tutup.
- Capabilities (bitmask, HELLO client / AUTH_OK server):
  `0x00000001` profile-sync (CONFIG_PUSH/PROFILE_*), `0x00000002` snapshot,
  `0x00000004` gamepad-abstrak, `0x00000008` rumble (reserved).
  Fitur hanya boleh dipakai jika **kedua sisi** mengumumkan bit-nya.
  `snapshot` (0x02) wajib di v1.
- Struktur/semantik baru harus didokumentasikan sebagai *addition* di versi
  minor tanpa mengubah byte layout yang sudah ada.

## 19. Contoh message (hex, big-endian)

Konvensi contoh: header 18 byte diikuti payload. Nilai `Timestamp` dan kunci
hanya ilustrasi. String diberi anotasi `"..."`.

### 19.1 HELLO (C→S)
Payload: `deviceId="ctrl-42a8"` (9), `clientVersion="0.1.0"` (5),
major=1, minor=0, capabilities=0x00000007.

```
43 54                        magic "CT"
01 00                        ver 1.0
08                           flags MUST_UNDERSTAND
01                           type HELLO
00 16                        payloadLength = 22
00 00                        seq 0
00 00 01 8D 9E 8E 2A 00      timestamp (contoh)
09                           deviceIdLength 9
63 74 72 6C 2D 34 32 61 38   "ctrl-42a8"
05                           clientVersionLength 5
30 2E 31 2E 30               "0.1.0"
01 00                        protocol major 1 minor 0
00 00 00 07                  capabilities
```

### 19.2 WELCOME (S→C)
Payload: `serverName="CTRL-PC"` (7), versi efektif 1.0, minSupportedMajor=1,
sessionId 16 byte contoh, authRequired=1, challenge 32 byte contoh.

```
43 54 01 00 08 02 00 3C 00 00 00 00 01 8D 9E 8E 2A 00
07 43 54 52 4C 2D 50 43          "CTRL-PC"
01 00                            versi efektif 1.0
01                               minSupportedMajor 1
00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F   sessionId
01                               authRequired
10 11 12 13 14 15 16 17 18 19 1A 1B 1C 1D 1E 1F
20 21 22 23 24 25 26 27 28 29 2A 2B 2C 2D 2E 2F   challenge (32 byte)
```

### 19.3 AUTH — pairing code (C→S)
Payload: credentialType=0x02, kode `"123456"`, deviceId `"ctrl-42a8"`,
challengeResponse 32 byte contoh.

```
43 54 01 00 08 03 00 32 00 01 00 00 01 8D 9E 8E 2A 00
02                               credentialType pairingCode
06                               credentialLength 6
31 32 33 34 35 36                "123456"
09                               deviceIdLength 9
63 74 72 6C 2D 34 32 61 38       "ctrl-42a8"
50 51 52 53 54 55 56 57 58 59 5A 5B 5C 5D 5E 5F
60 61 62 63 64 65 66 67 68 69 6A 6B 6C 6D 6E 6F   challengeResponse
```

### 19.4 AUTH_OK (S→C)
Payload: result=0, sessionId echo, serverCapabilities=0x00000007,
newTokenLength=16, token `"a1b2c3d4e5f60718"`.

```
43 54 01 00 08 04 00 26 00 02 00 00 01 8D 9E 8E 2A 00
00                               result ok
00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F   sessionId
00 00 00 07                      serverCapabilities
10                               newTokenLength 16
61 31 62 32 63 33 64 34 65 35 66 36 30 37 31 38   token
```

### 19.5 INPUT_EVENT — button down (C→S)
Payload: `controlId="btn-fire"` (8), kind=button, flags=stateChanged,
state=down, pressCount=1. Panjang payload = 14.

```
43 54 01 00 00 06 00 0E 00 03 00 00 01 8D 9E 8E 2A 00
08                               controlIdLength 8
62 74 6E 2D 66 69 72 65          "btn-fire"
00                               kind button
01                               flags stateChanged
01                               state down
00 01                            pressCount 1
```

Button up identik dengan `state=00` dan `pressCount` yang sama.

### 19.6 INPUT_EVENT — axis (C→S)
Payload: `controlId="thr"` (3), kind=axis, flags=stateChanged,
value=0.5 (0x3F000000). Panjang = 10.

```
43 54 01 00 00 06 00 0A 00 04 00 00 01 8D 9E 8E 2A 00
03 74 68 72                      "thr"
01                               kind axis
01                               flags stateChanged
3F 00 00 00                      float32 0.5
```

### 19.7 INPUT_EVENT — stick (C→S)
Payload: `controlId="rs"` (2), kind=stick, flags=stateChanged,
x=-0.5 (0xBF000000), y=0.25 (0x3E800000). Panjang = 13.

```
43 54 01 00 00 06 00 0D 00 05 00 00 01 8D 9E 8E 2A 00
02 72 73                         "rs"
02                               kind stick
01                               flags stateChanged
BF 00 00 00                      x = -0.5
3E 80 00 00                      y =  0.25
```

### 19.8 INPUT_SNAPSHOT (C→S)
Payload: 2 entry — button `btn-fire` down pressCount=5; stick `rs` center
(0,0). Panjang = 2 + 14 + 13 = 29.

```
43 54 01 00 00 07 00 1D 00 06 00 00 01 8D 9E 8E 2A 00
00 02                            entryCount 2
08 62 74 6E 2D 66 69 72 65 00 01 01 00 05    button down, pc=5
02 72 73 02 01 00 00 00 00 00 00 00 00        stick (0,0)
```

### 19.9 HEARTBEAT (C→S) dan PONG (S→C)
HEARTBEAT: payload `clientSendTime` (8 byte). PONG: `clientSendTime` + `serverTime`.

```
HEARTBEAT  43 54 01 00 00 09 00 08 00 07 00 00 01 8D 9E 8E 2C 00
          00 00 01 8D 9E 8E 2C 00      clientSendTime
PONG       43 54 01 00 00 0A 00 10 00 00 00 00 01 8D 9E 8E 2C 05
          00 00 01 8D 9E 8E 2C 00      clientSendTime (echo)
          00 00 01 8D 9E 8E 2C 05      serverTime
```

### 19.10 DISCONNECT (S→C atau C→S)
Payload: reason=0 (normal).

```
43 54 01 00 08 0D 00 01 00 08 00 00 01 8D 9E 8E 2A 00
00                                reason normal
```

### 19.11 ERROR (S→C)
Payload: code=0x02 (auth-failed), severity=1, message `"auth failed"`.

```
43 54 01 00 08 0C 00 0F 00 02 00 00 01 8D 9E 8E 2A 00
02                               code auth-failed
01                               severity warn
00 0B                            messageLength 11
61 75 74 68 20 66 61 69 6C 65 64 "auth failed"
```

### 19.12 CONFIG_PUSH (dua arah: C→S / S→C)

Sinkronisasi layout/bindings/settings antar perangkat. Format payload:

```
1 byte   profileIdLength      (36 — UUID ASCII; lihat §24.15)
N byte   profileId            (UTF-8, UUID)
8 byte   revision             uint64 BE — revisi milik owner (lihat aturan)
1 byte   kind                 0x01 layout | 0x02 bindings | 0x03 settings
1 byte   encoding             0x01 JSON
2 byte   jsonLength           uint16 BE
N byte   json                 (UTF-8)
```

**Kepemilikan (ownership):**
- **Mobile OWNS layout (kind=0x01)** — layout yang dapat diedit pengguna.
  Mobile menaikkan `revision` tiap ada edit dan mengirim ke desktop.
- **Desktop OWNS bindings & runtime state (kind=0x02, 0x03)** — hasil
  resolusi mapping/output. Desktop yang menaikkan `revision`-nya.
- Desktop boleh mengirim salinan layout kanonik (kind=0x01) ke mobile untuk
  dirender, tetapi TIDAK menaikkan revision layout (mobile tetap owner).

**Aturan anti-stale (revision):**
- `revision` monotonik per `(profileId, kind)`, dikelola oleh owner-nya.
- Penerima menyimpan `lastAppliedRevision[profileId][kind]`.
- Payload dengan `revision <= lastAppliedRevision` untuk `(profileId, kind)`
  yang sama = **stale → TIDAK diterapkan** (ACK tetap dikirim agar pengirim
  tidak mengulang). Payload dengan `revision > lastAppliedRevision` diterapkan
  dan `lastAppliedRevision` diperbarui.
- Kedua sisi WAJIB mengisi `profileId` + `revision`; jika tidak → pesan
  ditolak (ERROR `invalid-message`).

Contoh (S→C, layout, `ACK_REQUESTED`): profileId UUID
`"a3f8e2b1-9c4d-4e6f-8a1b-2c3d4e5f6071"` (36), revision 3, kind=0x01,
encoding=0x01, JSON `{"schemaVersion":1}` (19 byte):
payload = 1+36+8+1+1+2+19 = 68 byte → `PayloadLength = 0x0044`.

```json
{"schemaVersion":1,"id":"lay-01","name":"Racing","controls":[
 {"id":"btn-fire","type":"button","x":0.1,"y":0.1,"width":0.2,"height":0.2,"style":{"label":"FIRE"}}]}
```

Binding memakai **nama logis**, bukan kode API Windows, contoh:
`{"schemaVersion":1,"bindings":{"btn-fire":{"type":"keyboard","key":"Space"}}}`.
Resolusi `key → VirtualKeyCode` murni tanggung jawab desktop.

### 19.13 AUTH_DENIED (S→C)
Payload: reason=0x01 (bad-credential), message `"auth failed"` (11 byte).
Panjang payload = 1 + 1 + 11 = 13 (0x0D).

```
43 54 01 00 08 05 00 0D 00 02 00 00 01 8D 9E 8E 2A 00
01                               reason bad-credential
0B                               messageLength 11
61 75 74 68 20 66 61 69 6C 65 64 "auth failed"
```

## 20. Batas ukuran message

| Batas | Nilai |
|---|---|
| Panjang header | tetap 18 byte |
| `PayloadLength` maksimum | 65 535 byte (uint16 BE) |
| Panjang `controlId` | 1–64 byte |
| Panjang `deviceId` | 1–64 byte |
| Panjang `profileId` | 36 byte (UUID ASCII; lihat §24.15) |
| Panjang `clientVersion`/`serverName` | 1–64 byte |
| Panjang kredensial (AUTH) | token tersimpan 32–512 byte (batas storage/internal, TIDAK dikirim); kode pairing/payload QR yang dikirim ≤ 255 byte (dibatasi `credentialLength` 1 byte) |
| Panjang `newToken` (AUTH_OK) | ≤ 255 byte yang dikirim via wire (dibatasi `newTokenLength` 1 byte); token tersimpan 32–512 byte (batas storage/internal) |
| Panjang `challenge`/`challengeResponse` | tetap 32 byte |
| Panjang JSON (PROFILE_LIST/CONFIG_PUSH/STATUS) | ≤ 65 535 (dibatasi max payload); chunking reserved untuk minor |
| Entri INPUT_SNAPSHOT | 1–1024 |
| Panjang pesan ERROR | ≤ 1024 byte |

Batas storage/internal dan batas wire TIDAK sama. Field `credentialLength`
(AUTH) dan `newTokenLength` (AUTH_OK) masing-masing 1 byte, sehingga payload
yang dikirim lewat wire maksimal 255 byte. Token persisten disimpan 32–512 byte
secara internal namun TIDAK pernah dikirim lewat wire (AUTH token memakai
`credentialLength=0`).

Penerima TIDAK boleh mengalokasikan buffer berdasarkan nilai panjang sebelum
memvalidasi batas. Payload > 65 535 tidak dapat direpresentasikan header;
stream yang mengklaim panjang itu dianggap corrupt → ERROR `payload-too-large`.

## 21. Aturan validasi message

Urutan pemeriksaan penerima:

1. `Magic` tidak cocok → buang byte hingga magic cocok berikutnya; jika tidak
   ada dalam 1 frame → tutup koneksi (korupsi stream).
2. `VersionMajor` di luar dukungan → ERROR `protocol-version-mismatch` +
   tutup (hanya untuk HELLO/WELCOME; pesan lain langsung tutup).
3. Reserved flags (`SECURE`, `COMPRESSED`, bit4–7) ≠ 0 → ERROR
   `forbidden` + tutup.
4. `PayloadLength` > 65 535 → ERROR `payload-too-large` + tutup.
5. Baca payload penuh dari stream; stream habis sebelum `PayloadLength` →
   anggap corrupt → tutup.
6. `MessageType` tidak dikenal → lihat aturan MUST_UNDERSTAND (§6).
7. Validasi struktur per type: field string di dalam batas, enum di dalam
   rentang, float finite & dalam rentang, `entryCount` ≤ 1024. CONFIG_PUSH
   tanpa `profileId`/`revision` dianggap invalid.
   Gagal → kontrol-plane: ERROR `invalid-message` + tutup; input-plane:
   buang event (log).
8. Urutan: pesan application-plane sebelum AUTH_OK → ERROR
   `not-authenticated` + tutup.
9. `Sequence` tidak monoton → catat (tidak fatal).
10. `controlId` tidak dikenal pada layout aktif → log + abaikan (input).

## 22. Aturan keamanan

- **Threat model v1 (batasan sementara tahap pengembangan, BUKAN desain
  produksi final):** jaringan LAN semi-terpercaya. Setelah AUTH_OK, pesan
  dikirim sebagai **authenticated plaintext** — penyadap pasif dapat membaca
  nilai input. Namun token persisten TIDAK pernah lewat wire (dibuktikan via
  challenge-response HMAC, §12) dan kode pairing sekali pakai + TTL 300 s
  membatasi jendela eksposur.
- **Session security layer (final): TLS 1.3 over TCP.** Menambah integrity
  dan confidentiality pada aliran pasca-AUTH_OK dengan membungkus stream TCP
  memakai **TLS 1.3 standar** (record layer TLS; pesan protokol CTRL sebagai
  application data). **TIDAK memakai AES-GCM/crypto custom; dilarang
  menciptakan protokol kriptografi sendiri.** Implementasi boleh **ditunda ke
  milestone keamanan** dan TIDAK memblokir M0. Bit `SECURE` di header +
  negosiasi upgrade disediakan untuk ini; di v1 bit `SECURE` wajib 0.
- **Auth wajib** sebelum pesan application-plane (§12). Kode pairing sekali
  pakai, TTL 300 detik. Batas gagal auth 5 → kunci 30 detik.
- **Jangan pernah** menempatkan token/kode di pesan ERROR, log, atau pesan
  STATUS. Token server disimpan ter-hash.
- `challenge` baru (32 byte acak) per sesi → mencegah replay.
- Input selalu berupa `controlId` logis — tidak ada kode API Windows di wire
  (defense in depth & decoupling).
- Batas ukuran/rate enforced (§20, §21) untuk mencegah abuse buffer & flood.
- Rate-limit handshake/HELLO (mis. per IP) untuk mengurangi DoS ringan.
- Client menyimpan token di penyimpanan aman (mis. Android Keystore);
  server memakai DPAPI/secret store.
- Firewall: port protokol dibuka server desktop saat instalasi (admin).

## 23. Alur pesan — ringkasan

Koneksi + profil:

```
Mobile                                 Desktop
  │ HELLO ──────────────────────────────► │
  │ ◄───────────────────────── WELCOME    │ (versi, challenge, sessionId)
  │ AUTH (deviceId + challengeResponse) ─► │
  │ ◄─────────────────────── AUTH_OK      │ (capabilities, newToken?)
  │ INPUT_SNAPSHOT ─────────────────────► │  ← resync state
  │ HEARTBEAT (1s) ─────────────────────► │
  │ ◄─────────────────────────── PONG     │
  │ PROFILE_LIST_REQ ───────────────────► │
  │ ◄──────────────────────── PROFILE_LIST│ (JSON)
  │ PROFILE_SELECT ─────────────────────► │
  │ ◄─────────────────── PROFILE_SELECTED │
  │ ◄──────────────────────── CONFIG_PUSH │ (layout, profileId+revision, ACK)
  │ ACK ────────────────────────────────► │
  │ INPUT_EVENT (btn/axis/stick) ───────► │  ← jalur input hot-path
  │ ◄─────────────────────────── STATUS   │ (latency/baterai, JSON)
  │ DISCONNECT ─────────────────────────► │
```

## 24. Keputusan desain — status

### Diterapkan (v1, final)

1. **Endianness & header**: semua integer big-endian; header tetap 18 byte.
2. **Identitas kontrol = string `controlId` (1–64) per event.** Optimasi
   "control slot" 2-byte ditunda ke minor.
3. **Satu event per frame** di v1; batching (`INPUT_BATCH`) reserved.
4. **Port default TCP 42123**; **mDNS `_ctrl._tcp.local` = milestone
   lanjutan — M0 wajib mendukung koneksi manual IP:port.**
5. **`Sequence` di-reset setelah AUTH_OK**; `pressCount` hanya alat deteksi.
6. **Challenge-response = `HMAC-SHA256(sharedSecret, challenge)`**, dengan
   `sharedSecret` = byte mentah token/kode pairing. **Tanpa HKDF di v1.**
7. **Authenticated plaintext pasca-AUTH_OK hanya batasan sementara (M0)**;
   **session security layer (integrity/encryption) direservasi** di §22,
   memakai kriptografi mapan; bit `SECURE` reserved.
8. **Satu sesi aktif per `deviceId`; AUTH_OK baru menutup sesi lama.**
9. **Negosiasi versi** (major sama → min minor; client lebih baru → tolak;
   client lebih tua → kompatibilitas ke `minSupportedMajor`).
10. **Snapshot wajib pasca AUTH_OK**; snapshot berkala opsional (default off).
11. **CONFIG_PUSH dua arah**: mobile OWNS layout (user-editable);
    desktop OWNS bindings/runtime state. Setiap CONFIG_PUSH membawa
    `profileId` + `revision`; receiver mengabaikan payload stale
    (`revision <= lastApplied`).
12. **Binding memakai nama logis** (`"Space"`, `"KeyW"`, `axis:2`) — resolusi
    ke API Windows di desktop saja.
13. **Session security layer = TLS 1.3 over TCP** (§22) — kriptografi mapan,
    tanpa AES-GCM/crypto custom; implementasi ditunda ke milestone keamanan,
    tidak memblokir M0/M1.
14. **Semantik `revision` CONFIG_PUSH final:** monotonik per `(profileId,
    kind)`; `revision > lastApplied` → terapkan; `<= lastApplied` → abaikan
    payload tetapi tetap ACK; konfigurasi stale tidak pernah menimpa state
    yang lebih baru.
15. **`profileId` = UUID.** Nama profil yang mudah dibaca adalah metadata
    display terpisah dan TIDAK dipakai sebagai identifikasi internal.
16. **QR pairing = CTRL pairing URI ber-versi.** `sharedSecret` tidak
    diletakkan langsung di payload QR plaintext kecuali model keamanan
    eksplisit memerlukannya; grammar QR wajib didokumentasikan sebelum
    implementasi pairing.

### Masih menunggu persetujuan

Tidak ada keputusan desain yang belum final untuk v1.

Aksi pra-implementasi (bukan persetujuan; ditagih di milestone terkait):
- Dokumentasikan **grammar CTRL pairing URI** sebelum implementasi pairing
  (M1) — keputusan #16.
- Definisikan **skema trust/sertifikat TLS 1.3** sebelum milestone keamanan.

## 25. Pemeriksaan konsistensi internal

- Semua tipe integer/float: big-endian (dinyatakan §5, dipakai konsisten di
  semua contoh §19).
- Ukuran record §8/§9 konsisten dengan contoh §19.5–19.7 (button 14 byte,
  axis 10, stick 13).
- `MUST_UNDERSTAND` konsisten antara tabel §6, header §7, dan validasi §21.
- Versi efektif WELCOME (§4) dicerminkan di field header WELCOME/HELLO.
- CAP_SNAPSHOT (0x02) diwajibkan §18 dan dipakai §15.
- Tidak ada pesan yang menyebut ViGEm/driver; gamepad hanya lewat kind
  button/axis/stick/trigger/hat (§9, §18 CAP_GAMEPAD).
- Tidak ada mekanisme ACK pada input hot path; ACK hanya untuk CONFIG_PUSH
  dkk. (§11) — konsisten dengan prinsip latensi.
- Bit `SECURE` konsisten: reserved di §7, wajib 0 di v1 (§18, §21), dan
  dipakai oleh session security layer §22.
- Auth flow konsisten: §12 (token tidak dikirim; `challengeResponse =
  HMAC-SHA256(sharedSecret, challenge)`) ↔ §19.3 (pairing, credential dikirim).
- CONFIG_PUSH konsisten: payload berisi `profileId`+`revision` (§19.12),
  aturan anti-stale (§19.12), validasi (§21), dan kepemilikan (§24).
- Session security final: TLS 1.3 over TCP (§22) konsisten dengan keputusan
  §24.13 dan bit `SECURE` reserved (§7); tidak ada crypto custom.
- Contoh CONFIG_PUSH (§19.12) memakai `profileId` UUID 36 byte — konsisten
  dengan §20 dan §24.15.
