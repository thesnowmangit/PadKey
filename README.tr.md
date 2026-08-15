# PadKey

Bir gamepad'in **arka tuşlarını gerçek klavye tuşlarına** çeviren küçük bir Windows arka
plan programı. Beitong / Betop KP40D (VID `20BC`, PID `5127`) için yazıldı — o kumandanın
arka tuşlarını oyunlar hiç göremiyor — ama tetikleme mekanizması geneldir, başka HID
kumandalarda da çalışır.

Varsayılan: **sol arka tuş → F12** (Steam ekran görüntüsü), **sağ arka tuş → F5**.

Steam Input'a hiç dokunmaz, girdi zincirine hiç girmez; bu yüzden stick drift'i yapamaz ve
polling hızını düşürmez. Kumandanın hafızasına hiçbir şey yazmaz.

---

## Neden gerekli

Kumanda Windows'a tam olarak iki HID koleksiyonu bildiriyor:

| Koleksiyon | Ne | Arka tuşlar |
|---|---|---|
| `01:05` (`IG_01`) | Gamepad, 15 baytlık rapor, 10 buton | yok |
| `FF:03` (`MI_01`) | Satıcı tanımlı, 64 baytlık rapor | **var** |

**Klavye koleksiyonu (`01:06`) yok.** Yani cihaz, kendi yazılımına ne söylenirse söylensin,
fiziksel olarak tuş basamaz. Üstelik arka tuşlar gamepad koleksiyonunda hiç görünmüyor;
yalnızca satıcı borusunda varlar. Dolayısıyla bilgisayarda bir şeyin o boruyu okuyup tuşa
basması gerekiyor. PadKey bu.

Arka tuşları satıcı uygulamasında **atamasız** bırak. Orada bir gamepad tuşuna da
atarsan, o tuş oyuna da gider.

## Protokol

Satıcı borusu selamlanana kadar susar. Dizi, satıcı uygulamasının USB trafiği yakalanarak
çözüldü (USBPcap, yalnızca kumandanın aygıt adresiyle sınırlı). Uygulama sorgulamıyor,
abone oluyor:

| Yön | Paket | Anlamı |
|---|---|---|
| host → cihaz | `02 CD` + `08` dolgu | bağlan |
| cihaz → host | `02 CD 09 0A 08 09 ...` | cevap |
| host → cihaz | `02 A9 08 A8` + `08` dolgu | **durum akışını başlat** |
| cihaz → host | `02 6D ...` | durum raporları, ~24 Hz |

Durum raporunun **10. baytı** arka tuşları taşır:

| Değer | Anlam |
|---|---|
| `0x08` | boşta |
| `0x09` | sağ arka tuş (bit `0x01`) |
| `0x0A` | sol arka tuş (bit `0x02`) |

İki ayrıntı kritik, ikisi de yanlış yapılırsa saatler götürür:

- Dolgu **sıfır değil `0x08`**. Satıcı uygulaması böyle gönderiyor; telde doğrulandı.
- Komutlar **interrupt OUT** ucundan (`ep 0x04`) gitmeli. `HidD_SetOutputReport` kontrol
  borusunu kullanır; cihaz kabul edip yok sayar.

PadKey bu diziyi kendisi gönderdiği için satıcı uygulamasının çalışmasına gerek yok.

### Kumandanın iki bağlantı modu var

| VID | Mod | Not |
|---|---|---|
| `20BC` | Normal. Xbox 360 kumandası + satıcı borusu olarak görünür. | Bayt 10 düzeni yukarıdaki gibi. |
| `20DD` | Şarj / alternatif. Tek satıcı arayüzü, **gamepad arayüzü hiç yok**. | Bayt 10 başka bir şey — orada `0x02`, `0xA9` gibi değerler çıkıyor. |

Bu yüzden kurallar `vid = 0x20BC` değerini **bilerek** sabitliyor. İki modu birden
eşleştirmek tuşların kendi kendine basılmasına yol açıyor. Zaten `20DD` modunda kumanda
oyun kumandası olarak kullanılamıyor.

### Bilinmeye değer tuzaklar

- **Tek tanıtıcı, sıraya giren G/Ç.** Windows eşzamanlı dosya tanıtıcısında G/Ç'yi sıraya
  sokar. Tek tanıtıcıyla yazma, bekleyen `ReadFile`'ın arkasında sonsuza kadar bloke olur —
  tam bir istek geçer, sonrası donar. PadKey okuma ve yazma için ayrı tanıtıcı açar.
- **Canlı tutma paketleri akışı kirletir.** Kumanda, isteklere durum akışının kullandığı
  borudan cevap veriyor ve o cevaplar durum raporu değil. Uyandırmayı birkaç saniyede bir
  tekrarlamak, basılı tutulan tuşu kısa süre bırakılmış gibi gösterip ikinci kez
  tetikliyordu. Artık uyandırma **yalnızca kumanda sessizken** gönderiliyor.
- **Tek karelik parazitler.** Başka rapor türleri ara sıra tesadüfen bir kurala uyuyor.
  Ölçüm: her sahte tetiklenme tek kare sürdü, her gerçek basış 48 ms ve üzeri. Bu yüzden
  bir kural, tetiklemeden önce iki ardışık karede kararlı olmak zorunda (`arm_ms`).

## Kullanım

1. `PadKey.exe` çalıştır. Saat yanında simge çıkar; çift tıkla → ayarlar.
2. Tuşu değiştirmek için **KEYBOARD KEY** kutusuna tıkla ve istediğin tuşa bas.
   Ctrl/Shift/Alt kombinasyonları çalışır, Esc iptal eder.
3. Başka bir gamepad tuşu bağlamak için **Learn gamepad button** → elini kumandadan çek,
   iki saniye bekle, sonra tuşa bas ve bırak. Yanlış yakalarsa **Try another trigger** ile
   diğer adaylara geç, lambadan doğrula.

Değişiklikler anında geçerli olur ve kendiliğinden kaydedilir. Öğretme sürerken tuş
gönderilmez.

**Modlar** — *Tap* bir kez basıp bırakır (ekran görüntüsü için bu). *Hold* parmağın arka
tuşta durduğu sürece klavye tuşunu basılı tutar.

**Profiller** — kurallar `profiles\<ad>.ini` içinde; `padkey.ini` yalnızca hangi profilin
etkin olduğunu yazar. İkisi de `%APPDATA%\PadKey` altında, böylece exe tek başına
istediğin yerde durabilir.

**Otomatik başlatma** — *Start with Windows* kutusu. Windows başlatınca doğrudan tepsiye
iner; elle çalıştırınca ayar penceresi açılır. Zaten çalışırken tekrar çalıştırmak mevcut
pencereyi öne getirir.

## Derleme

```
build.cmd
```

.NET SDK gerekmez; Windows'ta hazır gelen `csc.exe` (.NET Framework 4.x) kullanılır. Çıktı,
bağımlılığı olmayan tek bir ~110 KB exe.

## Teşhis modları

| Komut | Ne yapar |
|---|---|
| `padkey.exe list` | Tüm HID cihazları, usage'ları, buton aralıkları, rapor boyları |
| `padkey.exe learn [VID]` | Canlı rapor akışı; hangi bayt/bit değişiyor gösterir |
| `padkey.exe hold` | **Kesin tuş bulucu.** Boştaki değer kümesiyle tuş basılıyken oluşan kümeyi karşılaştırır; titreşen telemetri baytları elenir |
| `padkey.exe keytest` | Profildeki tuşları gönderip düşük seviye klavye kancasıyla gerçekten göründüklerini doğrular |

Hepsi `%APPDATA%\PadKey\padkey-log.txt` dosyasına da yazar. Log çalıştırmalar arasında
silinmez, 512 KB'ı geçerse sıfırlanır.

## Ölçülen maliyet

| | |
|---|---|
| Boştaki CPU | bir çekirdeğin ~%0,2–0,4'ü |
| Özel bellek | ~24 MB |
| USB yazma | kararlı durumda ~0 |
| USB okuma | satıcı borusunda ~20 rapor/sn (kumanda zaten gamepad ucundan ~740/sn gönderiyor) |
| Algılama gecikmesi | ortalama ~60 ms — kumandanın ~42 ms akış kadansından, PadKey'den değil |

## Sınırlar

- Oyun **yönetici olarak** çalışıyorsa PadKey de yönetici olmalı; yoksa Windows (UIPI)
  enjekte edilen tuşu o pencereye geçirmez.
- Steam F12 ile ekran görüntüsünü yalnızca **oyun içinde, overlay açıkken** alır.
  Masaüstünde F12'ye basmak gerçek klavyede de bir şey yapmaz.
- Anti-cheat'i agresif bazı oyunlar enjekte edilen klavye girdisini yok sayabilir; Steam'in
  kendi kancası genelde etkilenmez.

## Lisans

MIT — [LICENSE](LICENSE).
