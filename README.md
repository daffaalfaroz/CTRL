# CTRL — Custom Gaming Control Deck

CTRL adalah aplikasi yang memungkinkan perangkat Android/tablet
digunakan sebagai control deck tambahan untuk PC.

Project ini terdiri dari:

- `mobile/` — CTRL Mobile: aplikasi Android/tablet (Flutter/Dart)
- `desktop/` — CTRL Desktop: aplikasi pendamping PC (.NET console)

Status: Development

## Menjalankan CTRL Mobile

Prasyarat:

- Flutter SDK (stable) tersedia di `PATH`
- Android SDK / Android Studio (untuk build APK)

```bash
cd mobile
flutter pub get
flutter run          # jalankan di emulator/device
flutter build apk --debug
```

## Menjalankan CTRL Desktop

Prasyarat:

- .NET SDK 8.0+

```bash
cd desktop
dotnet run
```

## Menjalankan pengujian

```bash
cd mobile
flutter analyze
flutter test

cd ../desktop
dotnet build
```

## Struktur

```
CTRL/
├── mobile/     # Aplikasi Flutter (Android/tablet)
├── desktop/    # Aplikasi .NET (PC companion)
└── docs/       # Analisis teknis & spesifikasi protokol
```
