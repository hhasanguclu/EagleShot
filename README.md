# EagleShot 🦅

**EagleShot**, Windows için geliştirilmiş, hafif, modern ve kullanıcı dostu bir ekran görüntüsü alma aracıdır. Lightshot'ın hızını ve Greenshot'ın gelişmiş özelliklerini modern bir arayüzle birleştirir.

![EagleShot Logo](Resources/logo.png)

## 🌟 Özellikler

*   **Hafif ve Hızlı:** C# WinForms ve .NET 9 teknolojileriyle geliştirilmiştir, sistem kaynaklarını minimum düzeyde kullanır.
*   **Modern Arayüz:** Materyal tasarımdan esinlenen, şık ve karanlık (#4D3961) tema.
*   **Akıllı Yakalama:**
    *   **Bölge Seçimi:** Ekranın herhangi bir alanını sürükleyerek seçin.
    *   **Pencere Algılama:** Fare ile üzerine geldiğiniz pencereleri otomatik olarak algılar ve vurgular.
    *   **Büyüteç (Magnifier):** Piksel hassasiyetinde seçim yapmanız için imleç etrafını yakınlaştırır.
*   **Gelişmiş Düzenleme Araçları:**
    *   🖊️ **Kalem:** Serbest çizim yapın.
    *   / **Çizgi:** Düz çizgiler çizin.
    *   ➔ **Ok:** Önemli detayları işaretleyin.
    *   ⬜ **Dikdörtgen:** Alanları çerçeveleyin.
    *   📝 **Metin:** Ekrana doğrudan yazı yazın (Inline Editing). Yazı boyutunu hemen yanındaki **+** / **-** butonları ile ayarlayın.
    *   🔦 **Vurgulama (Highlight):** Metinlerin veya alanların üzerini şeffaf sarı ile çizin.
*   **Kullanıcı Deneyimi:**
    *   **Görsel Araç Çubuğu:** Metin yerine anlaşılır ikonlar (Segoe MDL2 Assets) kullanılır.
    *   **Dil Desteği:** Sistem dilinize göre Türkçe veya İngilizce ipuçları (Tooltips).
    *   **Sıçrama Ekranı (Splash Screen):** Uygulama açılışında şık bir karşılama efekti.
*   **Çıktı Seçenekleri:**
    *   💾 Dosyaya Kaydet (PNG/JPG)
    *   📋 Panoya Kopyala

## 🚀 Kurulum ve Çalıştırma

Bu projeyi yerel makinenizde çalıştırmak için:

1.  **Gereksinimler:**
    *   .NET 9.0 SDK yüklü olmalıdır.
    *   Windows İşletim Sistemi.

2.  **Projeyi Klonlayın:**
    ```bash
    git clone https://github.com/kullaniciadi/EagleShot.git
    cd EagleShot
    ```

3.  **Çalıştırın:**
    Terminal veya komut satırında şu komutu girin:
    ```bash
    dotnet run
    ```

## 🎮 Kullanım

1.  Uygulama çalıştığında sistem tepsisine (System Tray) yerleşir.
2.  Klavyenizdeki **PrintScreen** (PrtSc) tuşuna basın veya tepsi ikonuna sağ tıklayıp "Take Screenshot" seçeneğini kullanın.
3.  Ekran kararacak ve seçim modu aktif olacaktır.
4.  Bir alan seçin. Seçimden sonra düzenleme araç çubuğu belirir.
5.  İstediğiniz düzenlemeleri yapın ve Kaydet/Kopyala butonlarını kullanın.

## 🛠️ Teknolojiler

*   **Dil:** C#
*   **Framework:** .NET 9.0 (WinForms)
*   **API:** Win32 API (User32.dll) - Global Hotkey ve Pencere işlemleri için.

## 📝 Lisans

Bu proje MIT Lisansı ile lisanslanmıştır. Detaylar için `LICENSE` dosyasına bakınız.
