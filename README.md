# PC-Ovladac

Jednoduché dálkové ovládání Windows PC z Android telefonu po domácí síti (LAN).

## Součásti projektu

- **Windows aplikace (`PC-Ovladac.exe`)** — běží na pozadí v oznamovací oblasti (system tray), poskytuje HTTP API (výchozí port `5000`), automatické vyhledání v síti přes mDNS a okno s nastavením příkazů.
- **Android aplikace (`PC-Ovladac.apk`)** — Jetpack Compose klient (`cz.pcovladac.app`) pro ovládání hlasitosti, přehrávání médií, zamknutí PC a dalších vlastních příkazů.
- **Pomocný skript (`scan-site.ps1`)** — rychlý sken zařízení v lokální podsíti.

## Stažení

Hotové sestavení (`PC-Ovladac.apk` a `PC-Ovladac.exe`) najdete v sekci [Releases](https://github.com/PatrikKM/PC-Ovladac/releases).

## Sestavení ze zdrojových kódů

### Windows server (`PC-Ovladac.exe`)
```powershell
.\build.ps1
```

### Android aplikace (`PC-Ovladac.apk`)
```powershell
cd android-app
.\gradlew.bat assembleRelease
```
Pro podepsání release APK vytvořte v `android-app/` soubor `keystore.properties` s cestou k vašemu keystore (`storeFile`, `storePassword`, `keyAlias`, `keyPassword`).
