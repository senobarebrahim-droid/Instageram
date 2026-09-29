# گزارش اجرای فاز ۱ — INSTAGERAM

تاریخ: 2026-09-29 | نوع کار: پایدارسازی، ضدخطا، اتمیک
پروژه: `E:\instageram` — سورس: `E:\instageram\src\Instageram`

---

## ۱. خلاصه اجرایی

| معیار | قبل | بعد |
|---|---|---|
| مسیر دیتابیس | هاردکد `E:\instageram\src\...\bin\Debug\...\data\database.db` | نسبی: `Path.Combine(Root, "data", "database.db")` |
| پنجره‌های باز‌شده در هر اجرا | **۲ پنجره تکراری** | **۱ پنجره** |
| نوشتن روی ریشه درایو `E:\` | ۳ فایل (`addcampaign_test.txt`، `campaign_save_error.txt`، `instageram_startup_error.txt`) | **صفر** |
| پیام‌های `MessageBox` تشخیصی | ۳ پیام پشت‌سرهم | حذف شد |
| اسکریپت اجرا | مسیر ناموجود `C:\Ebrahim senobar2\instageram` → شکست | مسیر خودکار نسبت به محل اسکریپت → موفق |
| بیلد Release قابل حمل | وجود نداشت | `publish\Instageram-Portable` (۱۶۱.۹ مگابایت، self-contained) |
| بیلد | ۰ خطا / ۰ هشدار | ۰ خطا / ۰ هشدار |
| داده کاربر | داخل پوشه `bin` (با هر Clean نابود می‌شد) | مهاجرت خودکار + نسخه قابل حمل شامل داده |

**نتیجه تست‌ها: همه موفق.** برنامه الان روی هر درایو و هر مسیری اجرا می‌شود و دیتابیس خودش را کنار خودش می‌سازد.

---

## ۲. تغییرات کد (اتمیک، هر تغییر جداگانه)

| # | فایل | تغییر |
|---|---|---|
| ۱ | `MainWindow.xaml.cs` → `PortablePaths.Database` | حذف مسیر مطلق هاردکد؛ مسیر نسبی به محل فایل اجرایی |
| ۲ | `MainWindow.xaml.cs` → `PortablePaths` | افزودن `MigrateLegacyDatabase()`: اگر دیتابیس قابل حمل نبود و دیتابیس قدیمی وجود داشت، **بدون بازنویسی** کپی می‌شود (+ فایل‌های `-wal`/`-shm`) و در لاگ ثبت می‌گردد |
| ۳ | `MainWindow.xaml.cs` → `DatabaseService.AddCampaign` | حذف `File.WriteAllText(@"E:\addcampaign_test.txt", ...)` و حذف `MessageBox` تشخیصی داخل لایه داده |
| ۴ | `MainWindow.xaml.cs` → `CreateCampaign` | حذف `MessageBox.Show("Campaign ID = ...")` |
| ۵ | `MainWindow.xaml.cs` → `SaveCampaign_Click` | حذف نوشتن `E:\campaign_save_error.txt`؛ خطا به لاگ + پیام کوتاه؛ تأیید موفقیت در نوار وضعیت |
| ۶ | `MainWindow.xaml.cs` → `MainWindow_Loaded` | کد مرده (`FindName("CountryComboBox")` که وجود نداشت) با پیاده‌سازی درست روی `CountryList` جایگزین شد؛ اکنون ایران اول لیست و انتخاب‌شده است |
| ۷ | `MainWindow.xaml.cs` | حذف کلاس `MainWindow` تکراری و خالی |
| ۸ | `App.xaml.cs` | خطای استارتاپ به `logs\startup-error.log` کنار فایل اجرایی (به‌جای مسیر مطلق) + پیام فارسی کوتاه + `Shutdown(1)` |
| ۹ | `App.xaml.cs` | افزودن `using System.IO;` — **نکته مهم:** در پروژه‌های WPF، usingهای ضمنی .NET 8 شامل `System.IO` نیستند (تداخل با `System.Windows.Shapes.Path`)، بنابراین `Path`/`File`/`Directory` بدون آن resolve نمی‌شوند |
| ۱۰ | `App.xaml` | حذف `StartupUri="MainWindow.xaml"` — علت اصلی باز شدن دو پنجره تکراری |
| ۱۱ | `Launch-Instageram.ps1` | مسیرها از محل خود اسکریپت مشتق می‌شوند (`src\Instageram` و `publish\Instageram-Portable`) |

### اثر انگشت فایل‌ها

| فایل | SHA256 قبل | SHA256 بعد |
|---|---|---|
| `MainWindow.xaml.cs` | `074AE5FB…20B76F` | `BE9BBDD4…094B9` |
| `App.xaml.cs` | `601A203D…D869F` | `0E534CB7…BE920` |
| `App.xaml` | `543CC290…9F639A` | `87D5A388…D336A1` |
| `Launch-Instageram.ps1` | `7274D17A…FFB5E9` | `617F9004…894233` |

---

## ۳. تست‌های انجام‌شده (همه واقعی، نه فرضی)

| تست | روش | نتیجه |
|---|---|---|
| صحت پشتیبان | مقایسه جدول‌ها و رکوردها با دیتابیس زنده | ✅ یکسان (۳ پروژه، ۳ کمپین) |
| بیلد از سورس | `dotnet build -c Debug` | ✅ ۰ خطا / ۰ هشدار |
| پنجره تکراری | شمارش پنجره‌های واقعی با `EnumWindows` | ✅ ۲ → **۱** |
| اجرای برنامه | اجرا و بررسی زنده‌بودن + عنوان پنجره | ✅ سالم |
| Release قابل حمل | `dotnet publish -c Release -r win-x64 --self-contained` | ✅ ۱۶۱.۹ MB |
| **تست A — مهاجرت داده** | کپی پکیج به مسیر دیگر، حذف دیتابیس، اجرا | ✅ دیتابیس محلی ساخته شد و **۳ کمپین خودکار مهاجرت کرد** |
| **تست B — نصب تازه** | مخفی‌کردن موقت دیتابیس قدیمی + حذف دیتابیس تست + اجرا | ✅ دیتابیس خالی و سالم ساخته شد (۰ کمپین) |
| بازگشت داده تست B | مقایسه SHA256 دیتابیس اصلی با پشتیبان | ✅ بایت‌به‌بایت یکسان |
| اسکریپت اجرا (حالت تغییر) | اجرای واقعی اسکریپت | ✅ انتشار + استارت، خروجی کد ۰ |
| اسکریپت اجرا (حالت بدون تغییر) | اجرای دوباره | ✅ «No source changes» + استارت |

هش دیتابیس زنده (بدون تغییر): `7F566E92BA69FB87CADCE058D46C1AD88E5BB14AD4631CED42E7433269465AE9`

---

## ۴. نقاط بازگشت (Rollback)

پشتیبان کامل و تأییدشده در:
```
E:\instageram\backup\PHASE1_PRESTART_20260929_141033\
```
شامل: نسخه قبل از تغییر `MainWindow.xaml.cs`، `App.xaml.cs`، `App.xaml`، `MainWindow.xaml`، `Launch-Instageram.ps1`، `Program.cs`، `manifest.json`، `settings.json` و دیتابیس.

فایل‌های منسوخ ریشه `E:\` نیز (به‌جای حذف) به پوشه `obsolete-root-files\` در همان مسیر منتقل شدند.

**بازگردانی:** فایل‌های همان پوشه را با نام اصلی برگردانید (نام‌ها با `__` تخت شده‌اند).

---

## ۵. عمداً انجام نشد (خارج از دامنه فاز ۱)

- خواندن `settings.json` و رفع ناهمخوانی `theme` (نیازمند موتور تنظیمات — فاز ۲)
- انتقال اسکیما از کد به `database\schema.sql` و ساخت ۷ جدول غایب (فاز ۲)
- اتصال `ImportedInstagramDiscoveryProvider` و صفحه Settings (فاز ۳)
- پیاده‌سازی Restore از ZIP (فاز ۳)
- تست خودکار، Git، حذف ۵۱ پوشه backup (فاز ۴)
- `Program.cs` در ریشه پروژه (اسکریپت دیباگ قدیمی، خارج از csproj) دست‌نخورده ماند

---

## ۶. تذکر دامنه

دکمه **«شروع ارسال دستی»** دست‌نخورده باقی مانده است: کاملاً انسانی، یکی‌یکی، با تصمیم و کلیک کاربر.
هیچ قابلیت ارسال خودکار یا انبوه پیام در این فاز اضافه نشد و افزوده نخواهد شد.

---

## ۷. قدم بعدی پیشنهادی

**فاز ۲ — یکپارچه‌سازی داده و تنظیمات:** اجرای واقعی `database\schema.sql` با مهاجرت نسخه‌دار، ساخت جدول‌های `countries`/`app_settings`/`logs`/`campaign_statistics`/`reports`، خواندن `settings.json` و `countries.json` به‌جای مقادیر هاردکد.
