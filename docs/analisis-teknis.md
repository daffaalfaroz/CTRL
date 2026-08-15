# Analisis Teknis CTRL — Custom Gaming Control Deck

Status: Draft v1 (tahap analisis, belum ada implementasi)
Dokumen pendamping: `docs/protocol.md` (spek protokol biner), `docs/keputusan-teknis.md` (ADR).

---

## 1. Product overview

CTRL adalah aplikasi dua sisi yang mengubah HP/tablet Android menjadi
**control deck tambahan** untuk PC Windows saat bermain game. Perangkat
mobile menampilkan control layout virtual (tombol, analog stick, trigger)
yang disentuh pengguna; masukan dikirim secara real-time melalui jaringan
lokal ke aplikasi PC, yang menerjemahkannya menjadi keyboard, mouse, atau
virtual gamepad.

Poin penting identitas produk:
- Produk ini **bukan remote desktop** — tampilan mobile adalah deck kontrol,
  bukan cermin layar PC.
- Mobile adalah **sumber masukan**; PC adalah **pengeksekusi masukan**.
- PC tidak bergantung pada cloud; seluruh fungsi inti berjalan di jaringan lokal.

## 2. Target pengguna

- Gamer PC yang ingin kontrol ekstra saat bermain (panel tambahan untuk
  game simulasi, flight sim, racing, MMO, builder, dsb.).
- Pemilik perangkat Android bekas/kedua (HP lama, tablet) yang bisa dijadikan
  second screen kontrol.
- Pengguna yang nyaman dengan setup teknis ringan (pasang aplikasi PC,
  instal driver virtual gamepad).
- Game yang menjadi target utama: **single-player / co-op** dan game tanpa
  anti-cheat agresif (lihat Risiko teknis).

## 3. Problem statement

Banyak game PC memakai lebih banyak masukan daripada yang nyaman dijangkau
dari keyboard/mouse atau satu gamepad (misal: banyak shortcut, panel MFD di
flight sim, tombol utilitas di MMO/racing). Solusi umum adalah membeli hardware
(stream deck, keypad, button box) — mahal dan statis.

Dengan CTRL, perangkat Android yang sudah dimiliki pengguna menjadi control
deck **virtual, custom, dan per-game**. Masalah yang diselesaikan:

- Menambah surface masukan tanpa membeli hardware tambahan.
- Layout yang bisa diatur per game (posisi, ukuran, fungsi tombol).
- Satu tombol mobile bisa memetakan ke keyboard, mouse, atau gamepad.

## 4. Core use cases

1. Pengguna menghubungkan HP/tablet ke PC melalui jaringan lokal (discovery
   otomatis atau IP manual) dan melakukan pairing.
2. Pengguna memilih profil game → control deck tampil sesuai layout profil.
3. Pengguna menekan/menggerakkan kontrol → PC menerima input dan
   meneruskannya ke game (keyboard/mouse/gamepad).
4. Pengguna membuka Layout Editor untuk membuat/mengubah layout: tambah
   tombol, atur posisi/ukuran/rotasi, pilih gaya visual.
5. Pengguna menetapkan fungsi tiap kontrol (bind) ke keyboard/mouse/gamepad.
6. Pengguna menyimpan layout + bindings sebagai profil per game.
7. Koneksi terputus (WiFi, app ditutup) → auto-reconnect; PC melepas semua
   tombol yang masih "ditekan" agar tidak macet (stuck keys).

## 5. Functional requirements

Fitur wajib:
- **Connection** — discovery PC (mDNS), koneksi manual via IP, pairing
  (PIN/QR), heartbeat, auto-reconnect, pemutusan secara eksplisit.
- **Control Deck** — render layout; dukung tombol digital, analog stick,
  trigger, dan (nantinya) hat/d-pad; layanan immersive fullscreen;
  umpan balik (visual press, opsional haptic).
- **Custom Layout Editor** — tambah/hapus kontrol, pindah, resize, rotasi,
  ubah gaya/opacity, simpan layout.
- **Input Mapping** — bind satu kontrol ke keyboard / mouse / gamepad;
  resolusi binding dilakukan di sisi PC.
- **Game Profiles** — kumpulan layout + bindings per game; pilih profil aktif.
- **Keyboard control** — simulasi tombol keyboard (tekan/lepas, kombinasi).
- **Mouse control** — klik (kiri/kanan/tengah), move relatif & absolut, scroll.
- **Virtual gamepad** — emulasi Xbox 360 (XInput) lewat ViGEmBus; tombol,
  analog stick (dengan deadzone/response curve), trigger.
- **Komunikasi lokal** — TCP LAN, protokol biner, versi protokol.

Non-fungsional terkait produk (dijabarkan di bagian 6).

Fitur yang **tidak boleh** ada: jam digital, fitur jam, atau hal lain yang
tidak mendukung fungsi control deck.

## 6. Non-functional requirements

- **Latency**: end-to-end input-to-game ideal < 16 ms (satu frame 60 Hz);
  target keras di LAN: network path < 5 ms. Jitter serendah mungkin.
- **Reliability**: tidak ada input yang hilang untuk event digital (kejadian
  press/release harus sampai); tidak boleh ada stuck key; state konsisten
  setelah reconnect.
- **Power**: mobile harus tetap awake saat dipakai (wakelock); minimalkan CPU
  saat idle.
- **Security**: pairing harus mencegah device asing; trafik default berjalan
  di LAN yang dianggap semi-terpercaya (token-auth handshake, stream
  plaintext dengan opsi enkripsi ke depan).
- **Extensibility**: pemetaan input dipisahkan lewat interface `IOutputSink`
  sehingga keyboard/mouse/gamepad (dan provider lain) bisa ditukar.
- **Portability kompilasi**: protokol biner dispesifikasikan netral-bahasa;
  codec diduplikasi Dart dan C# dari satu spec (docs/protocol.md).
- **Backward compatibility protokol**: header versi; penolakan/negosiasi versi.
- **Privasi**: tanpa telemetri wajib; data profil tersimpan lokal.

## 7. MVP scope

MVP = jalur inti yang bisa dites pengguna dan menghasilkan value:

1. Koneksi: discovery mDNS + IP manual, pairing PIN, TCP, heartbeat,
   auto-reconnect, pelepasan input saat putus.
2. Deck dasar: render layout dari data JSON; tombol digital, analog stick,
   trigger yang berfungsi.
3. Layout editor dasar: tambah/mindah/resize kontrol, simpan layout.
4. Bindings keyboard + mouse lewat `SendInput`.
5. Satu profil aktif per sesi (belum auto-switch per game).
6. Latency HUD (debug) + status koneksi + indikator baterai.

Virtual gamepad **tidak** di MVP-1; masuk milestone tersendiri (M5) karena
menambah beban instalasi driver.

## 8. Fitur yang ditunda ke versi berikutnya

- Virtual gamepad via ViGEm (membutuhkan instalasi driver + admin).
- Makro / urutan multi-tombol / multi-key.
- Mapping analog stick → gerak relatif mouse atau tombol.
- Kanal UDP berkecepatan tinggi untuk data stick (optimasi latensi lanjutan).
- Auto-deteksi game yang sedang berjalan → switch profil otomatis.
- Multi device (lebih dari satu mobile terhubung bersamaan).
- Android gamepad fisik sebagai sumber input di mobile.
- iOS, dukungan OS lain (macOS/Linux desktop).
- Enkripsi penuh stream, scripting (Lua/JSON), plugin SDK, telemetri opt-in.

## 9. Arsitektur sistem CTRL

Dua executable, satu protokol.

```
┌─────────────────────────────┐        ┌──────────────────────────────┐
│  CTRL Mobile (Android)      │  TCP   │  CTRL Desktop (Windows)      │
│  Flutter / Dart             │ ─────► │  C# / .NET                   │
│                             │  LAN   │                              │
│  UI deck + layout editor    │        │  Listener TCP + hub koneksi  │
│  TouchInputSource           │        │  BindingResolver (profil)    │
│  ProtocolEncoder            │        │  IOutputSink (kbd/mouse/game)│
│  Storage (layout+profil)    │        │  Storage (profil+binding)    │
└─────────────────────────────┘        └──────────────────────────────┘
```

Prinsip:
- **Mobile = sumber masukan, Desktop = pengeksekusi masukan.**
- Mobile tidak pernah tahu key code Windows; hanya mengirim `controlId`
  (id kontrol dalam layout) + nilai input.
- Desktop memegang pengetahuan tentang perangkat output (keyboard, mouse,
  gamepad) dan menyelesaikan binding.
- Layout dan profil disinkronkan antar perangkat sebagai payload (JSON);
  protokol input tetap biner & kecil.

## 10. Pembagian tanggung jawab

CTRL Mobile (Flutter):
- Render control deck sesuai layout aktif.
- Tangkap sentuhan/multi-touch → buat `InputEvent`.
- Encode `InputEvent` ke frame biner, kirim via TCP.
- Layout Editor (UI + persisten layout).
- Discovery (mDNS client), pairing UI, status koneksi, reconnect.
- Menyimpan layout & profil lokal (JSON).

CTRL Desktop (.NET):
- Listener TCP + koneksi per device; mDNS advertiser.
- Decode `InputEvent`, cari binding aktif (profil) → resolve `OutputAction`.
- Kirim ke `IOutputSink`: `KeyboardSink` / `MouseSink` / `GamepadSink`.
- Menyimpan profil & binding lokal (`%APPDATA%\CTRL`).
- UI pengaturan: firewall, driver ViGEm, daftar device, pemilihan profil.
- Menjalankan sebagai aplikasi tray agar bisa berjalan saat game bermain.

## 11. Arsitektur komunikasi Android ↔ Windows

- Transport: **TCP** (lihat perbandingan bagian 12), satu koneksi terarah
  mobile→PC untuk input; PC boleh mengirim ack/status/heartbeat balik pada
  koneksi yang sama.
- Discovery: mDNS service `_ctrl._tcp.local` (advertiser di PC, client di
  mobile); fallback manual IP:port.
- Pairing: handshake pada koneksi pertama (PIN/QR) → token disimpan kedua
  sisi; koneksi berikutnya wajib autentikasi token.
- Heartbeat: ping setiap 1 s; timeout 3–5 s → mark disconnected.
- Reconnect: mobile mencoba ulang dengan backoff; saat terkoneksi kembali,
  mobile mengirim **snapshot state lengkap** (semua kontrol yang sedang
  aktif) supaya stick/button yang ditahan tidak hilang.
- Safety: saat koneksi terputus, desktop melepas semua input aktif.

## 12. Perbandingan protokol komunikasi

### WebSocket vs TCP vs UDP

| Aspek | TCP | UDP | WebSocket |
|---|---|---|---|
| Reliabilitas | Urut & terjamin (retransmit) | Tidak dijamin (loss/duplikat) | Sama dengan TCP |
| Latensi | Sangat rendah di LAN (0.5–2 ms); jaga NoDelay | Terendah | TCP + overhead framing kecil |
| Overhead | Header 20 B + ACK | Header 8 B | Upgrade HTTP + frame (2–14 B) |
| Kompleksitas | Dasar; perlu protokol sendiri | Tinggi (urutan, loss, ack aplikasi) | Rendah; framing + ekosistem (SignalR) |
| Reconnect | Perlu logika sendiri | Perlu logika sendiri | Manual (atau SignalR otomatis) |
| Cocok | Input state-change (press/release) | Streaming analog berkecepatan tinggi yang toleran loss | RPC/config, frontend web |

### Analisis untuk CTRL

- Event input CTRL adalah **state-change** (press/release, nilai stick).
  Kehilangan satu event press = tombol tidak berfungsi; tidak bisa ditoleransi.
  → TCP (reliable, ordered) paling cocok.
- **UDP** unggul hanya untuk data sampel berulang berkecepatan tinggi yang
  toleran terhadap paket hilang (mis. posisi stick 250 Hz), karena tanpa
  retransmit tidak ada head-of-line blocking. Di LAN, loss sangat kecil dan
  TCP NoDelay sudah memberikan latensi serupa. Keunggulan UDP tidak cukup
  berarti untuk MVP.
- **WebSocket** tidak memberi keunggulan fungsional dibanding TCP murni di
  sini: protokol input biner kita kecil, dan kita tidak butuh HTTP/foreground
  web. Menambah lapisan framing tanpa benefit.

### Keputusan

- **Transport utama: TCP (raw socket, `NoDelay`) dengan protokol biner
  custom ber-versi.**
- Serialisasi: biner custom untuk hot path (input event) + JSON untuk
  config/layout/profil. (Alternatif yang sah: MessagePack/protobuf — lihat
  Risiko dependency.)
- Kanal **UDP** disimpan sebagai opsional masa depan (milestone lanjutan)
  untuk data analog; arsitektur memisahkan *channel abstraction* sejak awal
  agar penambahan ini tidak mengubah desain inti.

## 13. Input event architecture

Abstraksi agar satu kontrol CTRL bisa dipetakan ke banyak jenis output dan
UI tidak terikat pada API input Windows.

```
CTRL Button (mobile, UI widget)
   │  pointer events
   ▼
TouchInputSource  ── menghasilkan ──►  InputEvent (semantik, netral)
   │                                     controlId: string (GUID)
   │                                     inputType: digital|analog|stick
   │                                     value: bool / float / (x,y)
   │                                     seq: uint, t: timestamp
   ▼
ProtocolEncoder ──► frame biner ──► TCP
                                         ▼
                                 CTRL Desktop
                                         │
                         ProtocolDecoder ► InputEvent
                                         ▼
                         BindingResolver (GameProfile aktif)
                                         │  controlId → OutputBinding
                                         ▼
                              IOutputSink (interface)
                            ┌──────────┬──────────┬───────────┐
                            ▼          ▼          ▼           ▼
                       KeyboardSink MouseSink GamepadSink  (extensi: makro,
                        (SendInput) (SendInput)  (ViGEm)      profile-switch)
```

Aturan kunci:
- **Mobile hanya mengirim `controlId` + nilai.** Tidak ada key code.
- Binding disimpan & diselesaikan di **desktop**, karena output devices ada di
  sana.
- `IOutputSink` adalah satu-satunya tempat yang menyentuh API Windows; UI dan
  jaringan tidak pernah menyentuh `user32`/XInput.
- `InputEvent` didefinisikan sekali di `docs/protocol.md`; codec Dart & C#
  dibangkitkan/duplikasi dari spec.

## 14. Keyboard mapping architecture

- Output: `KeyboardSink` → `SendInput` (user32).
- Bind: `controlId → {type: keyboard, key: VirtualKeyCode, modifiers}`.
- Kombinasi modifier (Ctrl/Alt/Shift/Win) didukung sebagai daftar tombol yang
  ditekan bersamaan.
- Satu kontrol digital menghasilkan press saat sentuh, release saat lepas.
- Safety: sink menyimpan set tombol aktif; jika sink diberi sinyal "flush"
  (disconnect/timeout), semua tombol dilepas (mencegah stuck key).
- Abstraksi: `IKeyboardInput` interface sehingga `Interception` (driver
  kernel) dapat ditukar untuk game bermasalah nanti (lihat bagian Risiko).

## 15. Mouse mapping architecture

- Output: `MouseSink` → `SendInput` (user32; move relatif/absolut, click,
  scroll).
- Bind yang didukung: klik kiri/kanan/tengah, gerak mouse relatif
  (dari stick analog), gerak absolut, scroll wheel.
- Stick→mouse: posisi stick dipetakan ke kecepatan delta relatif dengan
  kurva/eksponen; dijalankan pada loop kecil desktop (bukan per-event) agar
  halus.
- Interface `IMouseInput` agar provider bisa diganti (SendInput / Interception).

## 16. Virtual gamepad architecture pada Windows

### Teknologi yang diperlukan

Windows hanya mengenali gamepad sebagai **device input nyata** (HID/XInput).
Untuk membuat gamepad virtual dibutuhkan **driver kernel-mode (virtual bus)**
yang mendaftarkan perangkat virtual; userspace tidak bisa membuat device
XInput tanpa driver.

### Pilihan yang tersedia

| Opsi | Jenis | Lisensi | Status/maintenance | Kekurangan |
|---|---|---|---|---|
| **ViGEmBus + ViGEmClient** | driver virtual bus (emulasi Xbox 360/XInput + DualShock 4) | driver BSD-3-Clause; `Nefarius.ViGEm.Client` (NuGet) MIT | Standar de facto (DS4Windows, reWASD, dll.); driver signed; NuGet terakhir 2/2023; **proyek native ViGEmClient sudah di-retire/EOL oleh Nefarius** | Instalasi butuh admin; SmartScreen; arah maintenance tidak dijamin |
| **LizardByte Virtual-Gamepad-Emulation-Client** | fork ViGEmClient yang aktif | MIT | Aktif, dipakai Sunshine (remote play) | Masih butuh driver ViGEmBus yang sama |
| **ScpVBus** | virtual bus tua (Xbox 360) | MIT | Hampir tak terawat, unsigned | Kurang stabil; tidak disarankan |
| **Interception (oblitum)** | driver untuk **keyboard/mouse**, bukan gamepad | MIT | Masih dipakai | Bukan untuk gamepad; di-block sebagian anti-cheat |
| **GP2040-CE / hardware fisik (RP2040, Arduino, Teensy)** | USB gamepad hardware | MIT (firmware) | Sangat matang | Butuh hardware + kabel; bukan solusi murni software |

### Rekomendasi gamepad

- **Utama: ViGEmBus (driver) + `Nefarius.ViGEm.Client` (NuGet, MIT).**
  Ini jalur paling teruji; game mengenali Xbox 360 XInput secara langsung,
  termasuk rumble yang bisa diteruskan sebagai haptic ke mobile.
- **Mitigasi maintenance:** pin versi driver & NuGet; bundle installer driver
  signed; pantau fork LizardByte (cocok bila ViGEmBus tak lagi diupdate);
  dan bungkus lewat interface `IGamepadSink` agar provider bisa diganti tanpa
  mengubah lapisan lain.
- Driver harus diinstal dengan hak admin satu kali; aplikasi mendeteksi dan
  memandu instalasi.

## 17. Custom control layout architecture

Data model (dibagikan, serialisasi JSON):

```
Layout {
  id, name, screen: {width,height}      // referensi kanvas
  controls: [
    { id: GUID, type: button|stick|trigger|dpad,
      x, y, width, height, rotation,      // posisi/ukuran relatif (0–1)
      style: {color, label, icon, opacity},
      haptic: bool,
      props: {deadzone?, range?, axis?} }
  ]
}
```

- Posisi disimpan **relatif (0–1)** terhadap ukuran kanvas sehingga layout
  proporsional di HP vs tablet dan rotasi.
- Layout adalah milik mobile (deck & editor); desktop hanya menerima
  `controlId` yang sudah terikat.
- Editor: hit-testing + gesture (drag/resize/rotate) bekerja pada koordinat
  kanvas; perubahan langsung disimpan (JSON) dan diterapkan live.
- Ke depan, layout bisa di-render juga di desktop untuk preview.

## 18. Game profile architecture

```
GameProfile {
  id, name,
  layout: {layoutId | inline layout},
  bindings: { controlId → OutputBinding },
  options: { pollRate?, deadzone defaults?, haptic? }
}

OutputBinding = keyboard: {key, modifiers}
              | mouse: {action, ...}
              | gamepad: {button|axis|stick, xAxis?, yAxis?}
              | macro: [...]            // versi lanjutan
              | profileSwitch: profileId // versi lanjutan
```

- Profil utama disimpan di **desktop** (`%APPDATA%\CTRL\profiles\*.json`)
  karena binding bergantung pada perangkat output PC.
- Layout (bagian visual) bisa disimpan di mobile dan dikirim ke desktop saat
  sinkron; desktop menyimpan salinan agar profil portabel.
- Saat koneksi, mobile meminta daftar profil dari desktop; pengguna memilih
  profil aktif → desktop mengirim layout untuk dirender mobile.
- `BindingResolver` = lookup `controlId → OutputBinding` berdasarkan profil
  aktif; resolusi O(1) memakai dictionary.

## 19. Local data storage

- Mobile: file JSON di app documents dir (v1 cukup; pakai SQLite/drift hanya
  bila perlu query). Menyimpan: daftar layout, profil salinan, pengaturan,
  daftar PC yang ter-pair.
- Desktop: JSON di `%APPDATA%\CTRL\` (settings, profiles, devices).
  Tidak perlu DB untuk v1.
- JSON dipilih karena data kecil, mudah dibaca/ditulis, mudah divalidasi,
  dan bebas dependency eksternal.
- Migrasi/versi: setiap file JSON menyimpan `schemaVersion`.

## 20. Connection pairing dan keamanan

- **Discovery:** mDNS `_ctrl._tcp.local` (desktop advertise port; mobile
  query). Fallback manual IP:port bila mDNS diblokir (AP isolation).
- **Pairing pertama:** mobile memindai QR (berisi `ip:port + pairingCode`) yang
  ditampilkan desktop, atau memasukkan PIN pendek; kode satu kali (short-lived).
- Setelah pairing: kedua sisi menyimpan `deviceId` + `authToken`; setiap
  handshake berikutnya mengirim token (HMAC untuk menolak replay).
- **Stream:** default plaintext (LAN semi-terpercaya, latensi rendah); opsi
  enkripsi penuh ditunda. Risiko didokumentasikan; traffic hanya berisi
  kontrol id + nilai, bukan data sensitif.
- **Pembatasan:** satu koneksi per deviceId; reject device tak dikenal.
- **Firewall:** installer desktop mendaftarkan aturan inbound untuk port
  protokol (butuh admin sekali).

## 21. Error handling dan reconnect

State machine koneksi (mobile):

```
Connected ⇄ Disconnected ⇄ Reconnecting
   ▲              │              │
   └──healthy─────┴──timeout─────┘   (backoff: 0.5s, 1s, 2s, …, cap 30s)
```

- Heartbeat 1 s; miss 3× (3–5 s) → mark disconnected.
- Saat disconnect, **desktop melepas semua input aktif** (flush sinks) —
  mencegah keyboard/gamepad macet.
- Saat reconnect berhasil: handshake ulang (token) → mobile kirim snapshot
  seluruh state kontrol (stick posisi, tombol yang masih ditekan) agar
  sinkron dan tidak ada kehilangan input.
- Error yang ditampilkan: koneksi gagal, PC tidak ditemukan, protokol
  tidak cocok (versi mismatch → dialog update), driver ViGEm belum terpasang.
- Logging: log loopback desktop sederhana untuk debugging; tak perlu telemetri.

## 22. Pertimbangan latency

Budget target input→game < 16 ms (1 frame @60 Hz). Perkiraan:

| Sumber | Estimasi |
|---|---|
| Sampling sentuh Android | ~4–8 ms |
| Encode + kirim TCP (LAN) | < 1 ms |
| Network (jaringan lokal, LAN) | 0.5–2 ms |
| Decode + resolve binding | < 0.5 ms |
| `SendInput` (kbd/mouse) | ~1 ms |
| ViGEm/XInput poll (125 Hz) | ≤ 8 ms |
| Game poll input (125–1000 Hz) | 1–8 ms |

- Jalur network sangat kecil; dominan adalah poll rate output (ViGEm) dan
  sampling sentuh.
- Jaga `NoDelay`; jangan batching di jalur input; pertahankan event loop
  desktop responsif (input diproses segera, bukan menunggu frame render).
- Optimasi lanjutan bila perlu: tingkatkan poll rate ViGEm, kanal UDP untuk
  stick, kurangi overhead encode. Latency HUD (desktop→mobile roundtrip)
  untuk mengukur nyata.

## 23. Risiko teknis

| Risiko | Dampak | Mitigasi |
|---|---|---|
| **Anti-cheat** (Vanguard, EAC, BattlEye) mendeteksi/memblokir SendInput, driver input, atau ViGEm | Game online tertentu menolak/tidak merespons input; potensi flag | Targetkan single-player/co-op & game tanpa anti-cheat; dokumentasi eksplisit; `IOutputSink` memungkinkan penukaran provider |
| Driver ViGEmBus: instalasi admin, SmartScreen, unsigned-on-old-build | Friction pemasangan; support load tinggi | Bundle installer signed; panduan instalasi; deteksi status driver |
| AP isolation / firewall memblokir mDNS & TCP | Discovery dan koneksi gagal | Fallback IP manual; panduan firewall; paket mDNS opsional |
| Wakelock / screen-off / background kill di Android | Koneksi mati saat layar off atau app dikecilkan | Wakelock + layanan foreground; reconnect otomatis |
| Gesture sistem Android (notifikasi, nav bar) mencuri sentuhan | Input terputus saat bermain | Immersive fullscreen; konfirmasi sentuhan 10-point |
| Game memegang input eksklusif (RAW/foreground) | Input tak masuk game | Uji per game; kebanyakan berfungsi via SendInput |
| Stuck keys (putus di tengah press) | Game macet | Flush sink saat disconnect; snapshot pada reconnect |
| Rotasi tablet/tampilan saat memegang deck | Layout berubah tak terduga | Layout relatif (0–1); lock orientasi per profil |

## 24. Risiko dependency/library

| Dependency | Lisensi | Status | Risiko & catatan |
|---|---|---|---|
| ViGEmBus (driver) | BSD-3-Clause | Dipakai luas; **proyek resmi menuju EOL** | Sedang. Pin versi; bundle installer; siapkan fallback fork LizardByte |
| Nefarius.ViGEm.Client (NuGet) | MIT | Update terakhir 2023; stabil | Rendah-sedang. Abstraksi `IGamepadSink` |
| SendInput (user32) | API OS | Teruji | Rendah; beberapa game/anti-cheat memblokir |
| Interception (opsional) | MIT | Driver kernel, belum signed resmi | Sedang. Hanya bila perlu; anti-cheat block |
| `multicast_dns` (Flutter) | BSD-3-Clause | Verifikasi publisher flutter.dev, aktif (4.9M dl) | Rendah |
| `Makaretu.Dns.Multicast` (.NET) | MIT | **Repo asli (richardschneider) usang**; fork `Makaretu.Dns.Multicast.New` aktif (jdomnitz, 2024) | Gunakan fork `.New`; atau buat advertiser mDNS sendiri |
| Serialisasi biner custom | — | Ditulis sendiri | Rendah; spec di `docs/protocol.md`; hindari codec pihak ketiga di hot path |
| (Alternatif) MessagePack / protobuf | MIT/Apache | Matang | Bila dipilih, ganti custom codec; tambah toolchain |

Aturan umum: hot path input **tanpa dependency pihak ketiga**; dependency
berlisensi permisif; semua dependency di-pin; driver kernel dijaga seminimal
mungkin.

## 25. Struktur repository yang direkomendasikan

```
CTRL/
├── README.md
├── AGENTS.md
├── .gitignore
├── docs/
│   ├── analisis-teknis.md     ← dokumen ini
│   ├── protocol.md            ← spek protokol biner (single source of truth)
│   └── keputusan-teknis.md    ← ADR (keputusan yang disetujui)
├── CTRL Mobile/               ← Flutter (Dart)
│   ├── pubspec.yaml
│   ├── lib/
│   │   ├── main.dart
│   │   ├── app/               ← bootstrap, routes, theme
│   │   ├── core/
│   │   │   ├── protocol/      ← codec biner (sesuai docs/protocol.md)
│   │   │   ├── connection/    ← tcp client, discovery, pairing, reconnect
│   │   │   └── storage/       ← layout/profil/settings (JSON)
│   │   ├── features/
│   │   │   ├── deck/          ← render deck aktif
│   │   │   ├── editor/        ← layout editor
│   │   │   ├── connection/    ← discovery + pairing UI
│   │   │   └── settings/
│   │   └── widgets/           ← kontrol reusable (button, stick, trigger)
│   └── test/
└── CTRL Desktop/              ← .NET (solusi C#)
    ├── CTRL.Desktop.sln
    ├── src/
    │   ├── CTRL.Desktop/      ← host: tray, jendela, settings UI (WPF)
    │   ├── CTRL.Server/       ← listener TCP, hub koneksi, mDNS advertiser
    │   ├── CTRL.Core/         ← protocol, InputEvent, profil, BindingResolver
    │   └── CTRL.Input/        ← IOutputSink + keyboard/mouse/gamepad
    └── tests/
        ├── CTRL.Core.Tests/   ← codec roundtrip, resolver
        └── CTRL.Input.Tests/  ← sink (unit), integrasi opsional
```

Catatan penamaan: struktur di atas belum dibuat; dibuat pada milestone M0.

## 26. Development roadmap

- **M0 — Bootstrap & CI.** Proyek Flutter + solusi .NET + struktur docs +
  CI (build dua sisi). Kriteria: kedua aplikasi kosong berjalan.
- **M1 — Connection Core.** TCP server/client, protokol biner v0,
  discovery mDNS + IP manual, pairing PIN/QR, heartbeat, reconnect, firewall
  rule, latency ping. Kriteria: koneksi/disconnect/reconnect stabil;
  roundtrip LAN < 5 ms.
- **M2 — Keyboard & Mouse MVP.** Deck contoh statis; tombol→keyboard,
  tombol→mouse (click/move); flush stuck key. Kriteria: bisa main game
  keyboard/mouse sederhana.
- **M3 — Layout Editor.** Tambah/hapus/mindah/resize/rotate, simpan layout,
  preview live.
- **M4 — Game Profiles.** Binding per game, pemilihan profil, sinkronisasi
  layout+binding.
- **M5 — Virtual Gamepad.** Integrasi ViGEm, tombol gamepad, analog stick
  (deadzone/kurva), trigger, instalasi driver, umpan balik haptic (rumble).
- **M6 — Polish & Rilis.** Reconnect hardening + snapshot sync, error UX,
  latency HUD, distribusi (APK + installer Windows), penandatanganan.
- **V2+ — Lanjutan.** Kanal UDP, makro/multi-key, stick→mouse/keys,
  auto-detect game + switch profil, multi-device, gamepad mobile sebagai
  sumber, iOS, enkripsi stream, scripting.

---

## A. Rekomendasi arsitektur final

- Dua aplikasi: **Mobile (Flutter)** sebagai UI/sumber input,
  **Desktop (.NET/WPF)** sebagai server + pengeksekusi input.
- Komunikasi: **TCP (raw, `NoDelay`) + protokol biner ber-versi**; mDNS untuk
  discovery; fallback IP manual; pairing PIN/QR → token; heartbeat +
  reconnect + snapshot state; desktop melepas input saat disconnect.
- Input: `InputEvent` semantik (`controlId`+nilai) dikirim mobile; desktop
  menyelesaikan binding via profil dan mengirim ke **`IOutputSink`**
  (keyboard/mouse = `SendInput`, gamepad = ViGEm). UI tidak pernah menyentuh
  API input Windows.
- Layout & profil disimpan sebagai JSON, disinkronkan antar perangkat.
- Virtual gamepad via **ViGEmBus + Nefarius.ViGEm.Client**, dibungkus
  `IGamepadSink` untuk ketahanan jangka panjang.

## B. Teknologi yang direkomendasikan

- Mobile: **Flutter** (Dart), minSdk 24+, target Android 7.0+; package:
  `multicast_dns`.
- Desktop: **.NET 8+**; UI **WPF** (Windows-only, dukungan tray matang);
  package: `Nefarius.ViGEm.Client`; mDNS via `Makaretu.Dns.Multicast.New`
  (fork aktif) atau implementasi sendiri.
- Driver: **ViGEmBus** (bundle installer, pin versi; pantau fork LizardByte).
- Serialisasi: biner custom (hot path) + JSON (config) — bebas dependency;
  alternatif sah: MessagePack/protobuf.
- Build/CI: GitHub Actions (workflow Flutter + .NET).

## C. Struktur repository

Lihat bagian 25. Inti: `CTRL Mobile/` (Flutter), `CTRL Desktop/` (.NET),
`docs/` (analisis, protocol.md, keputusan-teknis.md). Dibuat di M0.

## D. Urutan milestone

M0 Bootstrap → M1 Connection Core → M2 Keyboard/Mouse MVP → M3 Layout
Editor → M4 Game Profiles → M5 Virtual Gamepad → M6 Polish & Rilis →
V2+ (UDP, makro, auto-detect game, multi-device, iOS, dsb.).

## E. Keputusan teknis yang harus disetujui sebelum coding

1. **Transport = raw TCP (`NoDelay`) + protokol biner ber-versi**, dengan
   kanal UDP sebagai opsional masa depan. (Alternatif yang bisa dipertimbangkan
   ulang: WebSocket/SignalR bila ingin RPC & reconnect siap pakai.)
2. **Serialisasi = biner custom (hot path) + JSON (config/profil).** Alternatif:
   MessagePack (pragmatis, tanpa codegen) atau Protobuf (schema + tooling).
3. **Discovery = mDNS `_ctrl._tcp.local` + fallback manual IP.**
4. **Pairing = PIN/QR sekali + token (HMAC) untuk koneksi berikutnya;
   stream plaintext di LAN (enkripsi penuh ditunda).**
5. **Keyboard/mouse = `SendInput` (default),** dibungkus interface agar bisa
   ditukar ke Interception bila game bermasalah.
6. **Virtual gamepad = ViGEmBus + Nefarius.ViGEm.Client**, pin versi,
   bundle installer driver, abstraksi `IGamepadSink`; gamepad **tidak** di
   MVP-1 (M5).
7. **Target game dinyatakan: single-player/co-op & game tanpa anti-cheat
   agresif** — risiko anti-cheat didokumentasikan ke pengguna.
8. **Safety input:** desktop melepas semua input saat disconnect; snapshot
   state dikirim saat reconnect.
9. **Stack desktop = .NET 8 + WPF; mobile = Flutter, minSdk 24+.**
10. **Storage = JSON lokal** (mobile documents dir, desktop `%APPDATA%\CTRL`);
    tanpa DB di v1.
11. **Profil/binding disimpan di desktop** (karena bergantung perangkat
    output); layout disimpan di mobile dan disinkronkan.
12. **dokumen spesifikasi:** `docs/protocol.md` dibuat & disetujui sebelum
    implementasi M1; codec Dart & C# harus cocok dengannya.
