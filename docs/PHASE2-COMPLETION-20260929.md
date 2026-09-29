# گزارش اجرای فاز ۲ — یکپارچه‌سازی داده و تنظیمات

تاریخ: 2026-09-29 | نوع کار: مهاجرت نسخه‌دار، تنظیمات واقعی، جدول‌های غایب
پروژه: `E:\instageram` — پشتیبان قبل از شروع: `backup\PHASE2_PRESTART_20260929_142159`

---

## ۱. خلاصه اجرایی

| مورد | قبل (فاز ۱) | بعد (فاز ۲) |
|---|---|---|
| نسخه اسکیما | بدون نسخه (`user_version = 0`) | **`user_version = 2`** با مهاجرت نسخه‌دار |
| جدول‌های دیتابیس | ۲ جدول | **۹ جدول** |
| ایندکس‌ها | ۰ | **۴ ایندکس** |
| `settings.json` | فقط نوشته می‌شد، هرگز خوانده نمی‌شد | **واقعاً خوانده می‌شود** (`language`، `theme`، `auto_backup`، `integrity_check_enabled`) |
| کشورها | آرایه هاردکد داخل `MainWindow.xaml.cs` | خوانده‌شده از `data\countries.json` + همگام‌سازی با جدول `countries` |
| لاگ | فقط فایل متنی | فایل + **جدول `logs`** |
| بررسی یکپارچگی | نداشت | `PRAGMA integrity_check` در هر استارتاپ |
| پشتیبان خودکار | فقط دستی | **خودکار، یک‌بار در روز** (طبق `auto_backup`) |
| `schema.sql` | آرزوی ناهمخوان با کد (۹ جدول که ساخته نمی‌شد) | **آینه دقیق** وضعیت واقعی نسخه ۲ |

**داده کاربر در تمام مراحل دست‌نخورده ماند:** ۳ پروژه و ۳ کمپین، با تمام مقادیر اصلی.

---

## ۲. سه ماژول جدید

### `Core\SchemaMigrator.cs` — مهاجرت نسخه‌دار
- نسخه در `PRAGMA user_version` نگهداری می‌شود.
- **v1:** ساخت `projects` و `campaigns` + افزودن ستون‌های غایب به دیتابیس‌های قدیمی
  (`projects.updated_at/status/notes`، `campaigns.countries/instagram_url/created_at/updated_at/end_date/notes`).
- **v2:** ساخت ۷ جدول غایب (`countries`, `campaign_countries`, `campaign_statistics`,
  `profile_statistics`, `reports`, `app_settings`, `logs`) + ۴ ایندکس + پرکردن ستون‌های زمانی
  جدید از داده موجود (`created_at = start_date`، `updated_at = created_at`).
- کل مهاجرت داخل **یک تراکنش** انجام می‌شود؛ خطا = Rollback کامل.
- **کاملاً افزایشی:** دیتابیس موجود بازسازی نمی‌شود و اجرای دوباره هیچ تغییری ایجاد نمی‌کند.
- تصمیم مستند: روی `projects.instagram_url` ایندکس UNIQUE گذاشته **نشد**، چون داده فعلی
  URL تکراری دارد و مهاجرت شکست می‌خورد.

### `Core\AppSettings.cs` — تنظیمات واقعی
- `config\settings.json` خوانده می‌شود؛ کلید ناشناخته نادیده گرفته می‌شود، کلید غایب مقدار
  پیش‌فرض امن می‌گیرد، و فایل خراب هرگز مانع اجرا نمی‌شود.
- اثر واقعی: `integrity_check_enabled` بررسی یکپارچگی را در استارتاپ اجرا می‌کند و
  `auto_backup` یک پشتیبان روزانه می‌سازد.

### `Core\CountryCatalog.cs` — کشورها از فایل
- `data\countries.json` منبع اصلی است؛ لیست داخلی فقط «تور نجات» است تا UI هرگز خالی نشود.
- ادغام بر اساس `code`: مقدار فایل، مقدار داخلی را بازنویسی یا تکمیل می‌کند.
- قالب نمایش دقیقاً همان قالب قبلی است: `Iran (IR)`.
- `SyncToDatabase()` جدول `countries` را با `ON CONFLICT ... DO UPDATE` همگام می‌کند (idempotent).

---

## ۳. تغییرات فایل‌ها

| فایل | تغییر |
|---|---|
| `Core\SchemaMigrator.cs` | **جدید** — موتور مهاجرت نسخه‌دار |
| `Core\AppSettings.cs` | **جدید** — خواندن تنظیمات |
| `Core\CountryCatalog.cs` | **جدید** — کاتالوگ کشورها + همگام‌سازی دیتابیس |
| `MainWindow.xaml.cs` | `DatabaseService.Initialize` بازنویسی شد (WAL جدا + فراخوانی مهاجرت)، `RunStartupMaintenance` اضافه شد، `ConnectionString` به `internal` تغییر کرد، `AppLogger` به جدول `logs` وصل شد، آرایه هاردکد کشورها حذف و با `CountryCatalog` جایگزین شد، شیم تکراری `ALTER TABLE` در `AddCampaign` حذف شد |
| `App.xaml.cs` | ترتیب راه‌اندازی: مسیرها → اسکیما → تنظیمات → کشورها → لاگ → نگهداری خودکار → پنجره |
| `database\schema.sql` | بازنویسی کامل به‌عنوان آینه دقیق نسخه ۲ + توضیح انحرافات عامدانه |
| `manifest.json` | `database_schema_version` از ۱ به **۲** |

### هش SHA256 (۱۶ رقم اول)

| فایل | SHA256 |
|---|---|
| `MainWindow.xaml.cs` | `2A3096788779D061` |
| `App.xaml.cs` | `AF0FEAFB9AACD441` |
| `SchemaMigrator.cs` | `8889B53F636396ED` |
| `AppSettings.cs` | `A65F5085920ECC18` |
| `CountryCatalog.cs` | `4B23738B3D8805B7` |
| `schema.sql` | `935079A67952597C` |
| `manifest.json` | `BD07DCDBCFC49C4B` |

---

## ۴. تست‌های انجام‌شده

| تست | روش | نتیجه |
|---|---|---|
| بیلد | `dotnet build -c Debug` | ✅ ۰ خطا / ۰ هشدار |
| مهاجرت دیتابیس واقعی | اجرای برنامه روی دیتابیس `user_version = 0` | ✅ به نسخه ۲ ارتقا یافت |
| شمارش جدول‌ها | بازخوانی مستقیم SQLite | ✅ ۹ جدول و ۴ ایندکس |
| ستون‌های جدید | `PRAGMA table_info` | ✅ ۸ ستون در `projects`، ۱۳ ستون در `campaigns` |
| **حفظ داده** | مقایسه رکوردها قبل/بعد | ✅ ۳ پروژه و ۳ کمپین با مقادیر اصلی |
| پرکردن ستون‌های زمانی | بازخوانی مقادیر | ✅ `created_at = start_date`، `updated_at = created_at`، `status = Active` |
| **Idempotency** | اجرای دوباره برنامه | ✅ بدون خطا، بدون تغییر ساختار، پشتیبان روزانه تکرار نشد |
| جدول `logs` | شمارش رکورد پس از اجرا | ✅ ۷ رکورد ثبت شد |
| جدول `countries` | همگام‌سازی از JSON | ✅ ۱۱ کشور، ایران با `is_target_market=1` و `priority=1` |
| یکپارچگی دیتابیس | `PRAGMA integrity_check` | ✅ `ok` در همه اجراها |
| پشتیبان خودکار | بررسی پوشه `data\backups` | ✅ یک ZIP در روز، بدون تکرار |
| پنجره | شمارش پنجره واقعی | ✅ دقیقاً ۱ |
| خواندن `countries.json` | لاگ استارتاپ در پکیج قابل حمل | ✅ `from_file=True` |
| **قابل حمل بودن** | کپی پکیج به `E:\_INSTAGERAM_PORTABLE_TEST_B` و اجرا | ✅ schema=v2، ۳ کمپین حفظ شد، ۱۱ کشور، تنظیمات و لاگ مخصوص همان پوشه |
| پشتیبان قبل از فاز | بازخوانی `PHASE2_PRESTART` | ✅ `user_version=0` و ۳ کمپین — دست‌نخورده |

---

## ۵. نقاط بازگشت (Rollback)

```
E:\instageram\backup\PHASE2_PRESTART_20260929_142159\
```
شامل نسخه قبل از تغییر `MainWindow.xaml.cs`، `App.xaml.cs`، `schema.sql`، `manifest.json`،
`settings.json`، `countries.json`، `target_markets.csv`، `csproj` و دیتابیس نسخه ۰.

سه فایل جدید `Core\*.cs` صرفاً حذف می‌شوند (کد قدیمی به آن‌ها وابسته نبود).

---

## ۶. باقی‌مانده برای فازهای بعد

- **فاز ۳:** پیاده‌سازی Restore از ZIP، صفحه Settings، استفاده واقعی از `theme`/`language`،
  اتصال `ImportedInstagramDiscoveryProvider`، گزارش XLSX/PDF، نوشتن در جدول‌های
  `campaign_statistics` / `profile_statistics` و داشبورد رشد.
- **فاز ۴:** دو فایل تنظیمات موازی وجود دارد (`E:\instageram\config\settings.json` سطح پروژه و
  `config\settings.json` کنار فایل اجرایی) — باید یکی شود. همچنین جداسازی `MainWindow.xaml.cs`،
  تست خودکار، Git و آرشیو ۵۲ پوشه backup.

---

## ۷. تذکر دامنه

در این فاز هیچ قابلیت ارسال پیام اضافه نشد. جدول‌های `campaign_statistics` و
`profile_statistics` برای **اندازه‌گیری رشد واقعی فالوور** ساخته شدند، نه برای ارسال انبوه.
دکمه «شروع ارسال دستی» همچنان کاملاً انسانی و یک‌به‌یک است.
