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

Çıktı `publish\UpdateHub.exe` olur (framework'e bağlı, tek dosya).

İkon `tools/make-icon.cs` ile üretilir:

```powershell
dotnet run tools/make-icon.cs -- src/UpdateHub/Assets/UpdateHub.ico
```

## Proje yapısı

```
src/UpdateHub/
  Sources/           kaynak başına tarama ve güncelleme komutları
  Sources/Parsers/   CLI çıktılarını ayrıştıran saf fonksiyonlar
  Services/          süreç çalıştırma, sürüm karşılaştırma, çift tespiti
  Services/Elevation tek UAC ile yönetici işleri
  ViewModels/ Views/ WPF arayüzü (CommunityToolkit.Mvvm)
tests/UpdateHub.Tests/
  Fixtures/          gerçek CLI çıktılarından alınmış örnekler
```

## Yol haritası

- [x] Tarama, gruplu tablo, rozetler
- [x] Sıralı güncelleme, canlı çıktı, iptal
- [x] Tek UAC ile yönetici işleri
- [ ] Hariç tutma (`ignore`, `pin`, `skipVersion`) ve onay penceresi
- [ ] Güncelleme geçmişi ve "Geçmiş" sekmesi
- [ ] `--check` modu (konsola özet, çıkış kodu 0/1/2)

## Lisans

[MIT](LICENSE)
