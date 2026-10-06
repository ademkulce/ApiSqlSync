# ApiSqlSync

[![Build and Test](https://github.com/ademkulce/ApiSqlSync/actions/workflows/ci.yml/badge.svg)](https://github.com/ademkulce/ApiSqlSync/actions/workflows/ci.yml)

[English](README.md) | Türkçe

PostgREST uyumlu bir API’den SQL Server’a artımlı veri senkronizasyonu.

Kalıcı checkpoint, çalışma geçmişi, arka plan servisi ve
ASP.NET Core web paneli içerir.

## Özellikler

- `(updated_at, id)` cursor’ı üzerinden artımlı sayfalama.
- Kayıtları JSON olarak SQL Server’a ekleme ve güncelleme.
- Her sayfanın verilerini ve checkpoint’ini aynı transaction’da kaydetme.
- Son kaydedilen checkpoint’ten devam etme.
- Başarılı, başarısız ve iptal edilen senkronizasyon turlarını kaydetme.
- CLI üzerinden tek bir senkronizasyon turu çalıştırma.
- Worker üzerinden belirli aralıklarla senkronizasyon çalıştırma.
- Web panelinde iş özetlerini ve çalışma geçmişini görüntüleme.
- Gömülü SQL scriptleriyle otomatik veritabanı kurulumu.
- Geliştirme ortamındaki demo API ile uygulamayı deneme.

## Projeler

| Proje | Sorumluluk |
| --- | --- |
| ApiSqlSync.Core | Arayüzler, cursor modelleri, senkronizasyon motoru ve çalışma takibi |
| ApiSqlSync.PostgRest | HTTP istekleri, cursor sorguları ve JSON okuma |
| ApiSqlSync.SqlServer | Veritabanı işlemleri, panel sorguları ve otomatik kurulum |
| ApiSqlSync.Cli | Veritabanı kurulumu ve tek seferlik senkronizasyon |
| ApiSqlSync.Worker | Periyodik senkronizasyon |
| ApiSqlSync.Web | Web paneli ve geliştirme ortamı demo API’si |

Kaynak projeler `src/`, test projeleri `tests/` klasöründedir.

## Gereksinimler

- .NET 10 SDK.
- JSON desteği bulunan SQL Server veya Windows üzerinde
  SQL Server Express LocalDB.
- .NET 10 destekleyen Visual Studio ya da uyumlu başka bir editör.

Örnek yapılandırma Windows LocalDB kullanır.
Başka bir SQL Server kullanacaksanız bağlantı dizelerini güncelleyin.

## Windows üzerinde hızlı başlangıç

Aşağıdaki komutları repo kök klasöründe PowerShell üzerinden çalıştırın.

### 1. Yerel yapılandırma dosyalarını oluşturun

```powershell
Copy-Item src/ApiSqlSync.Cli/appsettings.Local.example.json src/ApiSqlSync.Cli/appsettings.Local.json
Copy-Item src/ApiSqlSync.Worker/appsettings.Local.example.json src/ApiSqlSync.Worker/appsettings.Local.json
Copy-Item src/ApiSqlSync.Web/appsettings.Local.example.json src/ApiSqlSync.Web/appsettings.Local.json
```

Kopyalanan dosyalarda şu ayarları kontrol edin:

- Üç projede de aynı hedef SQL Server veritabanını kullanın.
- Demo için CLI ve Worker API adresini
  `https://localhost:7190/demo-api/` olarak ayarlayın.
- API kaynağını `records` olarak ayarlayın.
- Demo API için token gerekmez.

Yerel yapılandırma dosyaları Git’e dahil edilmez.

### 2. Paketleri yükleyin ve solution’ı derleyin

```powershell
dotnet restore ApiSqlSync.slnx
dotnet build ApiSqlSync.slnx
```

### 3. Veritabanını hazırlayın

```powershell
dotnet run --project src/ApiSqlSync.Cli -- --init-db
```

Bu komut, yapılandırılan veritabanı yoksa oluşturur ve henüz
uygulanmamış şema scriptlerini çalıştırır.

Tekrar çalıştırıldığında `dbo.SchemaMigrations` tablosunda kayıtlı
scriptler atlanır.

Veritabanı henüz yoksa bağlantıda kullanılan hesabın
veritabanı oluşturma yetkisi bulunmalıdır.

### 4. Web uygulamasını başlatın

Geliştirme HTTPS sertifikasına güven verin:

```powershell
dotnet dev-certs https --trust
```

Uygulamayı Development ortamında başlatın:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/ApiSqlSync.Web --no-launch-profile -- --urls "https://localhost:7190"
```

Yukarıdaki ortam değişkeni yalnızca mevcut PowerShell oturumu için geçerlidir.

Bu terminali açık bırakın.

- İş özeti: https://localhost:7190/SyncJobs
- Çalışma geçmişi: https://localhost:7190/SyncRuns
- Demo API: https://localhost:7190/demo-api/records

Demo API yalnızca Development ortamında kullanılabilir.

### 5. Bir senkronizasyon turu çalıştırın

Repo kök klasöründe ikinci bir terminal açın:

```powershell
dotnet run --project src/ApiSqlSync.Cli
```

Kaynakta daha yeni kayıt yoksa sonraki çalıştırma sıfır kayıt işlemelidir.

### 6. Periyodik senkronizasyonu başlatın

CLI turu tamamlandıktan sonra Worker’ı çalıştırın:

```powershell
dotnet run --project src/ApiSqlSync.Worker
```

Worker ilk turu hemen çalıştırır. Tamamlanan veya başarısız olan
her turdan sonra `Sync:IntervalSeconds` kadar bekler.

Durdurmak için Ctrl+C kullanın.

## Yapılandırma

Yapılandırma şu sırayla yüklenir:

1. Uygulamanın varsayılan yapılandırması.
2. İsteğe bağlı `appsettings.Local.json`.
3. `APISQLSYNC_` önekiyle başlayan ortam değişkenleri.

Sonradan yüklenen değerler önceki değerleri geçersiz kılar.

CLI ve Worker şu ayarları kullanır:

| Ayar | Açıklama |
| --- | --- |
| Sync:JobId | Kayıtlar, checkpoint ve çalışma geçmişi için iş kimliği |
| Sync:PageSize | Her sayfada istenecek en fazla kayıt sayısı |
| PostgRest:BaseUrl | API’nin temel adresi |
| PostgRest:Resource | Temel adrese eklenecek kaynak adı |
| PostgRest:TimestampField | Sayısal zaman alanı |
| PostgRest:IdField | Sayısal kayıt kimliği alanı |
| PostgRest:Token | İsteğe bağlı bearer token |
| SqlServer:ConnectionString | Hedef SQL Server bağlantısı |
| SqlServer:CommandTimeoutSeconds | SQL komutlarının zaman aşımı süresi |

Worker ayrıca `Sync:IntervalSeconds` ayarını kullanır.
Web paneli SQL Server ayarlarını kullanır.

Örnek yapılandırma dosyalarına gerçek parola veya token eklemeyin.

## Kaynak API beklentileri

API, PostgREST tarafından kullanılan şu davranışları desteklemelidir:

- Yapılandırılan zaman ve ID alanlarına göre artan sıralama.
- `(updated_at, id)` cursor’ından sonraki kayıtları filtreleme.
- Sayfa boyutu sınırı.
- JSON dizi yanıtı.
- Zaman ve ID alanlarında işaretli 64 bit tam sayıya uygun değerler.

Bir kayıt güncellendiğinde, sonraki turda alınabilmesi için cursor’ı
kaydedilmiş checkpoint’in ilerisine taşınmalıdır.

## Veritabanı şeması

| Tablo | Açıklama |
| --- | --- |
| dbo.SyncRecords | Her iş ve kayıt ID’si için son kaydedilen JSON |
| dbo.SyncCheckpoints | Her işin son kaydedilen cursor’ı |
| dbo.SyncRuns | Senkronizasyon çalışma geçmişi |
| dbo.SchemaMigrations | Uygulanan scriptlerin adları, hash’leri ve zamanları |

Her sayfanın verileri ve checkpoint’i aynı transaction’da kaydedilir.
Çalışma geçmişi, sayfa transaction’larından ayrı kaydedilir.

Uygulanmış migration scriptlerini değiştirmeyin.
Şema değişiklikleri için yeni bir numaralı script oluşturun ve
`SqlServerDatabaseInitializer` içindeki listeye ekleyin.

## Testler

SQL Server gerektirmeyen testler:

```powershell
dotnet test tests/ApiSqlSync.Core.Tests
dotnet test tests/ApiSqlSync.PostgRest.Tests
```

Entegrasyon testleri için uygulama şeması önceden kurulmuş,
ayrı bir test veritabanı kullanın:

```powershell
$env:APISQLSYNC_TEST_CONNECTION = "Server=(localdb)\MSSQLLocalDB;Database=ApiSqlSync_Test;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
dotnet test tests/ApiSqlSync.IntegrationTests
```

Store testi, yapılandırılan veritabanını kullanır ve kendi kayıtlarını temizler.
Kurulum testi, aynı sunucuda ayrı bir geçici veritabanı oluşturur
ve test sonunda kaldırır.

GitHub Actions solution’ı derler, Core ve PostgREST testlerini çalıştırır.
SQL entegrasyon testleri şu anda ayrı çalıştırılır.

## Mevcut kapsam

- Kaynakta silinen kayıtlar hedefte otomatik silinmez.
- Cursor’ı checkpoint’in gerisinde kalan geç güncellemeler tekrar okunmaz.
- Veriler JSON olarak saklanır; ilişkisel kolonlara otomatik eşleme yoktur.
- Aynı iş için birden fazla senkronizasyon sürecini eşzamanlı çalıştırmayın.
  Dağıtık iş kilidi henüz uygulanmamıştır.
- Aynı JobId başka bir kaynak için kullanılırsa mevcut checkpoint de kullanılır.
  Kaynak değiştirirken farklı bir JobId kullanın.
- Web panelinde henüz kimlik doğrulama yoktur.
  Erişim kontrolü eklenene kadar yerel ortamda kullanın.