# UpdateHub

Windows'ta farklı paket yöneticilerindeki eski paketleri tek ekranda listeleyen küçük bir WPF uygulaması. Neyin güncelleneceğini siz seçersiniz; otomatik güncelleme yapmaz.

## Desteklenen kaynaklar

| Kaynak | Tarama | Güncelleme |
|---|---|---|
| winget | `winget upgrade` (varsa `Microsoft.WinGet.Client` modülü) | `winget upgrade --id <id> --exact --silent` |
| Chocolatey | `choco outdated -r` | `choco upgrade <id...> -y` (yönetici, tek UAC) |
| npm (global) | `npm outdated -g --json` | `npm install -g <id>@latest` |
| uv tools | `uv tool list --outdated` | `uv tool upgrade <id>` (bozuk ortamda `--reinstall`) |
| pip (global) | `python -m pip list --outdated` | `python -m pip install -U <id>` |
| Python (py) | `py list` / `py list --online` | `py install -u <tag>` |
| dotnet tools | `dotnet tool list -g` + NuGet | `dotnet tool update -g <id>` |

Kurulu olmayan kaynaklar "kurulu değil" olarak gösterilir ve atlanır.

## Özellikler

- Kaynaklar paralel taranır. Kaynak sekmelerinde güncelleme sayısı rozetle görünür.
- Rozetler: **MAJOR** (ana sürüm değişiyor), **YÖNETİCİ** (UAC gerekir), **ÇİFT** (aynı yazılım başka kaynakta da kurulu, ör. choco `python` ve Python Install Manager), **BOZUK** (uv ortamı bozuk).
- Güncellemeler sırayla çalışır. Çıktı canlı akar ve iptal edilebilir. "Ham metin" sekmesinden çıktının tamamı kopyalanabilir.
- Yönetici gerektiren işler tek UAC onayıyla toplu çalışır (aşağıya bakın).
- Sağ tık > "Komutu kopyala" ile güncelleme komutunu panoya alabilirsiniz.
- Biten her güncelleme geçmişe yazılır ve alt paneldeki **Geçmiş** sekmesinde listelenir (aşağıya bakın).
- `--check` modu pencere açmadan tarar; PowerShell profilinden kullanılabilir.
- Windows açılışında başlayabilir; güncelleme yoksa pencere açılmaz (aşağıya bakın).

## Windows açılışında başlatma

Durum çubuğundaki **Windows açılışında başlat** kutusu, çalışan exe'yi `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` altına `"…\UpdateHub.exe" --startup` olarak kaydeder. Yönetici gerekmez. Giriş Ayarlar > Uygulamalar > Başlangıç'ta da görünür; oradan kapatılırsa kutu da kapalı görünür.

`--startup` ile açılınca uygulama pencere açmadan tarar. Hariç tutulmayan güncelleme varsa pencere açılır, yoksa uygulama sessizce kapanır.

Kayıt exe'nin tam yolunu tutar. Bu yüzden kutuyu sabit bir klasördeki yayından (`publish\UpdateHub.exe`) açın; `bin\Debug` sürümünden açarsanız kayıt o exe'yi gösterir.

## `--check` modu

Pencere açmaz. Tüm kaynakları tarar, kaynak bazında özet yazar ve çıkış koduyla kapanır:

| Kod | Anlamı |
|---|---|
| 0 | Güncelleme yok |
| 1 | Güncelleme var (bazı kaynaklar hata verse bile) |
| 2 | Güncelleme bulunamadı ve en az bir kaynak hata verdi |

Örnek çıktı:

```
winget        2 güncelleme (1 gizlendi)
  Poly.PolyStudio  5.1.0.1111 → 5.2.0.741
  RARLab.WinRAR    7.00.0 → 7.23.0
Chocolatey    kurulu değil
npm (global)  güncel
dotnet tools  1 güncelleme
  dotnet-ef  8.0.5 → 10.0.1  MAJOR

Toplam 3 güncelleme.
```

UpdateHub bir pencere uygulaması (WinExe) olduğu için cmd ve PowerShell onu beklemez. PowerShell'de çıktıyı bir boruya verin; boru PowerShell'i bekletir ve `$LASTEXITCODE` dolar. Profil (`$PROFILE`) örneği:

```powershell
& "C:\Araclar\UpdateHub.exe" --check | Out-Host
if ($LASTEXITCODE -eq 1) { Write-Host "Güncelleme var: UpdateHub'ı açın." -ForegroundColor Yellow }
```

- Çıktı konsolun kod sayfasıyla yazılır. Kod sayfasında olmayan karakterler düzleşir (ör. `→` yerine `->`). Tam Unicode için profilde önce `[Console]::OutputEncoding = [Text.Encoding]::UTF8` ayarlayın.
- cmd'den doğrudan çalıştırıldığında çıktı istemden sonra yazılır; bitince Enter'a basın. Çıkış kodu cmd'de alınamaz.
- Hariç tutmalar ve "gizlendi" kuralları pencereyle aynıdır.

## Geçmiş

Biten her güncelleme `%LOCALAPPDATA%\UpdateHub\history.jsonl` dosyasına bir satır olarak eklenir:

```json
{"ts":"2026-10-27T14:30:00+03:00","source":"Npm","id":"npm","from":"12.0.2","to":"12.2.1","exitCode":0,"durationMs":4200,"elevated":false}
```

- Toplu komutlarda (ör. choco) her paket ayrı satırdır; süre ve çıkış kodu ortaktır.
- İptal edilen, UAC'si reddedilen veya hiç başlatılamayan işler yazılmaz.
- Bozuk satırlar okurken atlanır. Geçmiş sekmesi son 500 kaydı gösterir. "Tümünü kopyala" kayıtları sekmeyle ayrılmış metin olarak kopyalar.

## Hariç tutma

Ayarlar `%APPDATA%\UpdateHub\config.json` dosyasındadır. Satıra sağ tıklayıp "Hariç tut", "Bu sürümü atla" veya "Sürüme sabitle…" ile eklenir; elle de düzenlenebilir. Sabitleme penceresi kurulu sürümden desen önerir (`8.0.5` → `8.*`, `8.0.*`) ve önerilen sürümün gizlenip gizlenmeyeceğini hemen gösterir:

```json
{
  "exclusions": [
    { "source": "DotnetTool", "id": "dotnet-ef", "mode": "pin", "pattern": "8.*" },
    { "source": "Pip", "id": "certifi", "mode": "skipVersion", "version": "2026.10.23" },
    { "source": "Choco", "id": "visualstudio2019buildtools", "mode": "ignore" }
  ]
}
```

| `mode` | Etkisi |
|---|---|
| `ignore` | Paket hiç gösterilmez. |
| `pin` | Yeni sürüm `pattern` desenine uymuyorsa gösterilmez. |
| `skipVersion` | Sadece `version` sürümü atlanır; daha yenisi çıkınca yine görünür. |

`source` değerleri: `Winget`, `Choco`, `Npm`, `Uv`, `Pip`, `PythonRuntime`, `DotnetTool`.

Araç çubuğundaki **Hariç tutulanları göster** kutusu, gizlenen paketleri soluk ve `HARİÇ` rozetiyle gösterir (rozetin ipucunda kural yazar). Bu paketler seçilemez. Satıra sağ tıklayıp **Kuralı kaldır** ile paketin kuralları silinir ve paket yeniden güncellenebilir olur.

## Yönetici yetkisi

Uygulama normal kullanıcı olarak çalışır. Chocolatey gibi yönetici gerektiren paketler seçildiğinde:

1. Paketler `%TEMP%\UpdateHub\job-<guid>.json` dosyasına yazılır. Dosyada sadece kaynak adı ve paket kimlikleri bulunur, komut satırı bulunmaz.
2. Aynı exe `--elevated-run <job.json>` ile yönetici olarak başlatılır. Tek bir UAC onayı istenir.
3. Yükseltilmiş süreç komutu kendisi oluşturur ve çıktıyı `job-<guid>.out.jsonl` dosyasına yazar. Ana pencere bu dosyayı izler.

UAC reddedilirse satırlar "İptal edildi (UAC)" olarak işaretlenir. Yükseltilmiş süreç yönetici gerektirmeyen kaynakları ve geçersiz paket kimliklerini reddeder.

## Gereksinimler

- Windows 10/11 x64
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- Derlemek için .NET 10 SDK

## Derleme ve çalıştırma

```powershell
dotnet build
dotnet test
dotnet run --project src/UpdateHub
```

Tek dosya halinde yayınlamak için:

```powershell
dotnet publish src/UpdateHub -c Release -o publish
```

Çıktı `publish\UpdateHub.exe` olur (framework'e bağlı, tek dosya, yaklaşık 1,3 MB).

İkon `tools/make-icon.cs` ile üretilir:

```powershell
dotnet run tools/make-icon.cs -- src/UpdateHub/Assets/UpdateHub.ico
```

## Proje yapısı

```
src/UpdateHub/
  Sources/           kaynak başına tarama ve güncelleme komutları
  Sources/Parsers/   CLI çıktılarını ayrıştıran saf fonksiyonlar
  Services/          süreç çalıştırma, sürüm karşılaştırma, çift tespiti, geçmiş, --check
  Services/Elevation tek UAC ile yönetici işleri
  ViewModels/ Views/ WPF arayüzü (CommunityToolkit.Mvvm)
tests/UpdateHub.Tests/
  Fixtures/          gerçek CLI çıktılarından alınmış örnekler
```

## Yol haritası

- [x] Tarama, gruplu tablo, rozetler
- [x] Sıralı güncelleme, canlı çıktı, iptal
- [x] Tek UAC ile yönetici işleri
- [x] Hariç tutma (`ignore`, `pin`, `skipVersion`) ve onay penceresi
- [x] Güncelleme geçmişi ve "Geçmiş" sekmesi
- [x] `--check` modu (konsola özet, çıkış kodu 0/1/2)
- [x] Tek dosyalık yayın
- [x] Windows açılışında başlatma (`--startup`)

## Lisans

[MIT](LICENSE)
