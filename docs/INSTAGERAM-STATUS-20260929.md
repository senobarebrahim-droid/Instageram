# گزارش وضعیت پروژه INSTAGERAM

> **به‌روزرسانی 2026-09-29:** فاز ۱ (پایدارسازی و Portable واقعی) اجرا و تأیید شد.
> جزئیات کامل در `docs\PHASE1-COMPLETION-20260929.md` — موارد ۱ تا ۵ (بحرانی)، ۶، ۷ و ۲۰ (فایل‌های خطای ریشه `E:\`) در آن فاز رفع شدند.
>
> **به‌روزرسانی 2026-09-29 (فاز ۲):** مهاجرت نسخه‌دار اسکیما اجرا شد (`user_version = 2`)،
> ۷ جدول غایب ساخته شد (اکنون ۹ جدول)، `settings.json` و `countries.json` واقعاً خوانده می‌شوند،
> `schema.sql` آینه دقیق وضعیت واقعی شد، و لاگ به جدول `logs` وصل شد.
> جزئیات در `docs\PHASE2-COMPLETION-20260929.md` — موارد ۸، ۱۰، ۱۱، ۱۴ و ۲۱ گزارش زیر رفع شدند.

تاریخ تهیه: ۱۳۰۵/۰۷/۰۸ (2026-09-29) — تهیه‌شده از طریق ترمینال PowerShell

---

## ۱. شناسنامه پروژه

| مورد | مقدار |
|---|---|
| نام | INSTAGERAM — Portable Campaign Manager |
| مسیر پروژه | `E:\instageram` |
| مسیر واقعی سورس | `E:\instageram\src\Instageram` |
| نوع برنامه | WPF (دسکتاپ ویندوز) |
| تکنولوژی | .NET 8 (`net8.0-windows`)، `UseWPF=true`، `Nullable=enable`، `ImplicitUsings=enable` |
| پکیج خارجی | `Microsoft.Data.Sqlite` نسخه `8.0.8` (تنها وابستگی) |
| فایل پروژه | `src\Instageram\Instageram.csproj` (بدون فایل `.sln`) |
| نسخه اعلامی | `0.1.0` (در `manifest.json` و `config\settings.json`) |
| حالت | `portable_mode: true` (طبق تنظیمات و README) |
| مخزن Git | وجود ندارد (فقط `.gitignore` هست) |

### محیط اجرا (تأیید‌شده)

| ابزار | وضعیت |
|---|---|
| .NET SDK | `8.0.425` — نصب است |
| Runtime ها | `Microsoft.WindowsDesktop.App 8.0.31` و `10.0.12` |
| PowerShell | `5.1.19041.1682` |
| Node.js | موجود (`C:\Program Files\nodejs\node.exe`) |
| Python | نسخه سیستمی «Store stub» است و کار نمی‌کند (برای اسکریپت‌نویسی باید از نسخه همراه DSH استفاده شد) |
| Git | در PATH پیدا نشد |

---

## ۲. نتیجه تست سلامت (مهم)

کارهای زیر همین امروز واقعاً اجرا و تأیید شد:

1. **بیلد از سورس:** `dotnet build` روی پروژه → **موفق**، ۰ Warning، ۰ Error، حدود ۳۶ ثانیه.
2. **اجرای واقعی برنامه:** فایل `Instageram.exe` اجرا شد → بعد از ۸ ثانیه **هنوز زنده بود** و عنوان پنجره‌اش `INSTAGERAM - Portable کمپین Manager` بود → **برنامه سالم بالا می‌آید**.
3. **لاگ استارتاپ:** آخرین رکورد `logs\application.log` = `2026-09-29 13:25:41 | INFO | Application | INSTAGERAM started.`
4. **خطای قدیمی:** فایل `E:\instageram_startup_error.txt` مربوط به ساعت ۲۰:۲۰ روز ۲۰۲۶-۰۹-۲۸ است (`XamlParseException` در `MainWindow.xaml` خط ۱۳۵، به‌دلیل متن مستقیم داخل `WrapPanel`) و **در نسخه فعلی رفع شده است** — اجرای امروز هیچ خطای جدیدی تولید نکرد.

> جمع‌بندی: برنامه الان **خراب نیست**؛ مشکل فعلی «کار نکردن» نیست، بلکه «تکمیل‌نشدن معماری و قابلیت‌ها» است.

---

## ۳. ساختار واقعی سورس (وضعیت موجود)

```
src\Instageram\
  App.xaml / App.xaml.cs              نقطه ورود + راه‌اندازی مسیرها/لاگ/دیتابیس
  MainWindow.xaml                     رابط کاربری (۳۳۴ خط، ۴ تب)
  MainWindow.xaml.cs                  هسته برنامه (۱۰۶۹ خط — تقریباً همه‌چیز اینجاست)
  Instageram.csproj
  Core\PortablePathManager.cs.txt     فقط «سند طراحی» — کد C# نیست
  config\instagram_users.txt          لیست کاربران (نمونه، خالی)
  Services\                          ۸ فایل کلاس کمکی
tests\Instageram.Tests\.gitkeep      پوشه تست — خالی
database\schema.sql                  اسکیمای کامل (۹ جدول) — استفاده نمی‌شود
config\settings.json                 تنظیمات
config\instagram_graph_api.json      تنظیمات Graph API (غیرفعال)
data\countries.json, target_markets.csv   داده کشورها — خوانده نمی‌شود
assets\languages\fa.json, en.json    ترجمه‌ها — خوانده نمی‌شود
```

### کلاس‌های موجود در `MainWindow.xaml.cs` (همه در یک فایل)

| کلاس | خط | نقش |
|---|---|---|
| `MainWindow` | ۴۴ | رابط کاربری + رویدادها |
| `MainWindow` (تکراری/خالی) | ۹۹۶ و ۱۰۰۰ | باقی‌مانده ویرایش‌ها |
| `PortablePaths` | ۴۶۳ | مدیریت مسیرهای قابل حمل |
| `AppLogger` | ۵۱۲ | لاگ فایل متنی |
| `InstagramValidator` | ۵۲۹ | اعتبارسنجی URL اینستاگرام |
| `Summary` | ۵۷۶ | مدل خلاصه داشبورد |
| `DatabaseService` | ۵۸۶ | همه عملیات SQLite |
| `BackupService` | ۹۴۶ | پشتیبان ZIP |

### کلاس‌های پوشه `Services` (فقط بخشی متصل شده‌اند)

| فایل | وضعیت اتصال |
|---|---|
| `OrganicFollowEngine.cs` | متصل (موتور صف workflow دستی) |
| `CentralBrain.cs` | متصل (مدیریت کمپین فعال) |
| `CampaignActivity.cs` (`ActivityTracker`) | نیمه‌متصل (فقط در حافظه، ذخیره نمی‌شود) |
| `ImportedInstagramDiscoveryProvider.cs` | **متصل نیست** — هیچ‌جا صدا زده نمی‌شود |
| `InstagramDiscoveryProviderFactory.cs` | **متصل نیست** |
| `InstagramOutreachService.cs` | **متصل نیست** |
| `IInstagramDiscoveryProvider.cs` / `InstagramDiscoveryCandidate.cs` | فقط قرارداد |

---

## ۴. وضعیت دیتابیس

- تنها دیتابیس موجود: `src\Instageram\bin\Debug\net8.0-windows\win-x64\data\database.db` (۱۶ KB)
- جدول‌های موجود: **فقط دو جدول** `projects` و `campaigns`
- رکوردها: ۳ پروژه، ۳ کمپین

| id | campaign_name | countries | target | current | status | instagram_url |
|---|---|---|---|---|---|---|
| 3 | Iran Campaign | Iran | 2,000,000 | 25,000 | Active | instagram.com/ebrahimsenobar/ |
| 2 | Main Campaign | USA \| UK | 1,000 | 0 | Active | instagram.com/testuser/ |
| 1 | Main Campaign | USA \| UK | 1,000 | 0 | Active | instagram.com/testuser/ |

**نکته مهم:** این دیتابیس داخل پوشه `bin` است؛ یعنی هر بار `Clean` یا پاک‌سازی خروجی بیلد، **داده‌های کاربر از بین می‌رود**.

طبق `database\schema.sql` قرار بود ۹ جدول داشته باشیم:
`projects, campaigns, countries, campaign_countries, campaign_statistics, profile_statistics, reports, app_settings, logs`
که ۷ جدول آخر **هرگز ساخته نمی‌شوند**، چون کد به‌جای خواندن این فایل، دو جدول را به‌صورت inline در `DatabaseService.Initialize()` می‌سازد.

---

## ۵. مشکلات شناسایی‌شده

### 🔴 بحرانی (P0)

| # | مشکل | محل |
|---|---|---|
| ۱ | **مسیر دیتابیس هاردکد شده:** `public static string Database => @"E:\instageram\src\Instageram\bin\Debug\net8.0-windows\win-x64\data\database.db";` — این نقض مستقیم قانون اصلی پروژه است («هرگز مسیر مطلق هاردکد نکن») و باعث می‌شود نسخه Release یا کپی روی USB همچنان به درایو E: همان کامپیوتر بنویسد. باید `Path.Combine(Data, "database.db")` شود. | `MainWindow.xaml.cs:473` |
| ۲ | **مسیر خطای استارتاپ هاردکد شده** + نمایش کل StackTrace به کاربر با `MessageBox` | `App.xaml.cs:22-33` |
| ۳ | **فایل‌های دیباگ روی ریشه درایو E:** نوشتن `E:\addcampaign_test.txt`، `E:\campaign_save_error.txt`، `E:\instageram_startup_error.txt` | `MainWindow.xaml.cs:645، 440-443` |
| ۴ | **MessageBox های تشخیصی در مسیر اصلی:** «Campaign ID = …»، «DATABASE SAVED CAMPAIGN ID = …»، «SAVED CAMPAIGN ID = …» — کاربر با ۳ پیام پشت‌سرهم روبه‌رو می‌شود | `MainWindow.xaml.cs:143، 434، 714` |
| ۵ | **اسکریپت اجرا به مسیر ناموجود اشاره می‌کند:** `Launch-Instageram.ps1` ریشه را `C:\Ebrahim senobar2\instageram` و خروجی را `C:\Instageram\publish\Instageram-Portable` گرفته، در حالی که پروژه در `E:\instageram` است → با خطای «Expected exactly one project, found 0» شکست می‌خورد | `Launch-Instageram.ps1:4-5` |

### 🟠 مهم (P1)

| # | مشکل | محل |
|---|---|---|
| ۶ | دو کلاس `public partial class MainWindow { }` تکراری و خالی | `MainWindow.xaml.cs:996, 1000` |
| ۷ | `MainWindow_Loaded` دنبال کنترل `CountryComboBox` می‌گردد که در XAML وجود ندارد (نام واقعی: `CountryList`) → کد مرده، همیشه return می‌کند | `MainWindow.xaml.cs:1007` |
| ۸ | کد تکراری/بی‌اثر: دو بار `if(campaignId <= 0)` در `StartManualWorkflow_Click`؛ دو بار `CentralBrain.Instance.SetActiveCampaign()` در `CreateCampaign`؛ `GetLatestProjectCampaignId` کپی دقیق `GetLatestCampaignId` | خطوط ۱۸۹-۲۱۱، ۱۴۱-۱۵۱، ۷۸۰-۸۰۰ |
| ۹ | لیست ۱۱ کشور **هاردکد** داخل کد، در حالی که `data\countries.json`، `data\target_markets.csv` و جدول `countries` وجود دارند و استفاده نمی‌شوند | `MainWindow.xaml.cs:52-64` |
| ۱۰ | `config\settings.json` هرگز **خوانده** نمی‌شود (فقط اگر نباشد یک‌بار نوشته می‌شود) → تغییر theme/language/auto_backup هیچ اثری ندارد | `PortablePaths.Initialize()` |
| ۱۱ | ناهمخوانی تنظیمات: فایل `settings.json` می‌گوید `"theme": "Dark"` ولی کد پیش‌فرض `Light` می‌سازد | `config\settings.json` vs `MainWindow.xaml.cs:496` |
| ۱۲ | ترجمه‌ها (`assets\languages\fa.json`, `en.json`) و قابلیت چندزبانه هیچ‌جا استفاده نشده‌اند | — |
| ۱۳ | `InstagramValidator` و `InstagramDiscoveryProvider` هر دو کار مشابه (استخراج username) را جدا انجام می‌دهند | — |
| ۱۴ | شناسه‌های فارسی در کد و SQL (`Summary.هدف`، `AS هدف`) — کار می‌کند ولی خواندن/نگهداری کد را سخت و ریسک ابزارهای جانبی را بیشتر می‌کند | خطوط ۵۸۰، ۹۰۲ |
| ۱۵ | **بازیابی پشتیبان (Restore) پیاده نشده** — فقط Backup هست، در حالی که README و `docs\BUILD-INSTRUCTIONS.txt` روی تست Restore تأکید دارند | `BackupService` |

### 🟡 نظم و نگهداری (P2)

| # | مشکل |
|---|---|
| ۱۶ | **۵۱ پوشه backup** با نام‌های تاریخ‌دار در `E:\instageram\backup` + انبوه فایل `.bak`/`.backup` داخل `src` (حدود ۱۲ نسخه از `MainWindow.xaml.cs`) |
| ۱۷ | مخزن Git ساخته نشده؛ با وجود `.gitignore`، هیچ تاریخچه‌ای از تغییرات وجود ندارد |
| ۱۸ | پوشه تست خالی است؛ **هیچ تستی وجود ندارد** و فایل `.sln` هم نیست |
| ۱۹ | `E:\instageram\publish` خالی است؛ خروجی واقعی در `bin\Debug\...\publish` (حالت Debug) است |
| ۲۰ | فایل‌های `instageram_startup_error.txt` و `instageram_run_error.txt` روی ریشه `E:\` باقی مانده‌اند |
| ۲۱ | `manifest.json` می‌گوید هش فایل‌ها در Release build ساخته می‌شود، ولی فرایند Release/Manifest وجود ندارد (`files: []`) |
| ۲۲ | دو DataTemplate (`CountryItemTemplate`, `CountrySelectedTemplate`) در XAML تعریف شده‌اند ولی هیچ کنترلی از آن‌ها استفاده نمی‌کند (ListBox با رشته‌های ساده پر می‌شود) |
| ۲۳ | گزارش‌گیری فقط CSV دارد؛ XLSX و PDF ذکرشده در `PROJECT-STRUCTURE.txt` وجود ندارد |

---

## ۶. قابلیت‌های فعلی برنامه (آنچه کار می‌کند)

- ✅ اعتبارسنجی URL اینستاگرام و استخراج username
- ✅ ایجاد/ذخیره کمپین در SQLite (پروژه + کمپین در یک تراکنش)
- ✅ داشبورد: تعداد پروژه/کمپین، مجموع هدف، پیشرفت درصدی + جدول کمپین‌ها
- ✅ Workflow دستی: شروع، باز کردن پیج در مرورگر، «انجام شد»، «رد کردن»، «مورد بعدی»
- ✅ شمارش پیشرفت کمپین (`current_number` + وضعیت `Active/Completed`)
- ✅ خروجی گزارش CSV در `exports`
- ✅ پشتیبان‌گیری ZIP در `data\backups`
- ✅ لاگ متنی ساده
- ✅ تنظیمات Graph API رسمی به‌صورت آماده ولی **غیرفعال** (رویکرد محافظه‌کارانه و منطبق با README)

## ۷. قابلیت‌های ناقص / وعده‌داده‌شده ولی غایب

- ❌ حالت واقعاً Portable (مسیر هاردکد) 
- ❌ بازیابی از پشتیبان (Restore) و Rollback
- ❌ جدول‌های countries / statistics / reports / settings / logs
- ❌ خواندن تنظیمات از `settings.json` و صفحه Settings
- ❌ چندزبانه‌سازی از فایل‌های `assets\languages`
- ❌ اتصال منبع کشف پیج (`instagram_users.txt`)
- ❌ گزارش XLSX / PDF
- ❌ تست خودکار، نسخه‌بندی، Manifest و Release Build
- ❌ بررسی صحت یکپارچگی فایل‌ها (`integrity_check_enabled`)

---

## ۸. پیشنهاد نقشه راه ارتقا

### فاز ۱ — پایدارسازی و Portable واقعی (پایه، کوتاه)
1. اصلاح `PortablePaths.Database` به `Path.Combine(Data, "database.db")`.
2. انتقال دیتابیس فعلی (۳ کمپین) از پوشه `bin` به محل داده دائمی + کد مهاجرت خودکار در اولین اجرا.
3. حذف تمام فایل‌های دیباگ و `MessageBox` های تشخیصی.
4. بازنویسی `Launch-Instageram.ps1` برای مسیر `E:\instageram` و خروجی `publish\Instageram-Portable`.
5. ساخت خروجی Release تمیز و تست اجرا از یک درایو/پوشه دیگر (تست Portable واقعی).

### فاز ۲ — یکپارچه‌سازی داده و تنظیمات
6. انتقال اسکیما از کد به `database\schema.sql` + اجرای مهاجرت نسخه‌دار (`database\migrations`).
7. ساخت جدول‌های `countries`, `app_settings`, `logs`, `campaign_statistics`, `reports`.
8. خواندن/ذخیره `settings.json` و `countries.json` به‌جای مقادیر هاردکد.
9. اتصال `AppLogger` به جدول `logs`.

### فاز ۳ — تکمیل قابلیت‌ها
10. پیاده‌سازی Restore از ZIP با تأیید و Rollback.
11. اتصال `ImportedInstagramDiscoveryProvider` به UI (بارگذاری از `instagram_users.txt`).
12. چندزبانه‌سازی واقعی از `assets\languages`.
13. گزارش XLSX (و در صورت نیاز PDF).
14. صفحه Settings (تم، زبان، پشتیبان‌گیری خودکار).

### فاز ۴ — کیفیت و نگهداری
15. شکستن `MainWindow.xaml.cs` به فایل‌های جدا (Services/Models/Helpers) و حذف کدهای مرده.
16. ساخت پروژه تست (xUnit) + فایل `.sln`.
17. `git init` و کامیت اولیه؛ آرشیو/حذف ۵۱ پوشه backup و فایل‌های `.bak`.
18. نسخه‌بندی و تولید `manifest.json` با هش فایل‌ها در Release.

---

## ۹. نکته حقوقی/انطباق

README برنامه صریحاً اعلام کرده: ذخیره نشدن رمز/توکن اینستاگرام، نبود فالوور مصنوعی، نبود اتوماسیون follow/unfollow، و استفاده از API رسمی برای هر ارتباط آنلاین. ساختار فعلی کد هم همین را رعایت کرده (Graph API غیرفعال، اقدامات دستی).
**توصیه: این رویکرد در فازهای ارتقا حفظ شود.**

---

*این گزارش به‌صورت خودکار از طریق بررسی فایل‌ها، بیلد مجدد سورس، اجرای آزمایشی برنامه و بازخوانی دیتابیس تهیه شده است.*
