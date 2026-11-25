# هيكل المستودع

```text
YApex.sln
SPEC.md
Directory.Build.props
src/
  YApex.Core/       نماذج، بروتوكول، سياسة قواعد، التحقق
  YApex.Service/    خدمة Windows، Named Pipe، Firewall COM، IP Helper، SQLite
  YApex.UI/         تطبيق WPF وصينية النظام واللغات
  YApex.Wfp/        طبقة WFP تجريبية مغلقة افتراضياً
  YApex.Cli/        تشخيص وطوارئ عبر Named Pipe
tests/
  YApex.Core.Tests/
  YApex.Service.Tests/
assets/
  icons/            SVG ومخرجات PNG/ICO
  placeholders/     عناصر مؤقتة للصور التي لم تُطلب بعد
config/             روابط الدعم وخيارات الطبقة التجريبية
locales/            ar.json وen.json
installer/          سكربت Inno Setup
tools/              تصدير الأيقونات وPortable وSHA-256 وفحص موقع الوثائق
site/               موقع توثيق ثابت بالعربية والإنجليزية
.github/workflows/  CI والتحقق من الموقع وworkflow مُعدّ لمحاولة نشره إلى Pages عند تحديث main؛ النشر الحي غير متحقق بعد
docs/               المعمارية والتهديدات والخصوصية والمقارنة والبناء والاختبار
_private/           ملفات خاصة محلية؛ لا تُرفع وفق .gitignore
```

- `Directory.Build.props` يفعّل nullable والتحذيرات كأخطاء وبيانات الإصدار.
- ملفات المصدر C# وXAML وPowerShell وJavaScript تبدأ بترويسة حقوق ENG.YSWEF والبريد الموجود في إعداد Git.
- بيانات الخدمة المشتركة في `%ProgramData%\YSWEF\Y-Apex`، وإعدادات الواجهة في `%APPDATA%\YSWEF\Y-Apex`.
