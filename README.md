# Y-Apex

مراقب اتصالات Windows ومدير محلي لقواعد جدار الحماية. الواجهة تعمل بصلاحيات المستخدم، وتطلب معاينة وتأكيداً قبل تغيير قواعد Y-Apex. لا يستخدم التطبيق `netsh` ولا يرسل بيانات إلى خادم.

## حالة التنفيذ

هذه نسخة تطويرية أولية وليست إصداراً عاماً أو جاهزاً للاستخدام على جهاز أساسي. لا يوجد مثبّت عام منشور، ولم يُتحقق بعد من التثبيت وسلوك تغييرات الجدار يدوياً داخل جهاز Windows وهمي تفاعلي. يتضمن الحل .NET 10 خدمة Windows وواجهة WPF وCLI وطبقة WFP تجريبية غير منفذة. الشيفرة تعرض لقطات اتصالات TCP/UDP، وتدير قواعد Windows Firewall المملوكة للتطبيق عبر معاينة وتأكيد وتراجع مؤقت، وتسجل التغييرات، وتجمع عدادات ETW منذ بدء الخدمة، وتصدر البيانات، وتقدم مطالبة عند أول اتصال.

الحجب ينشئ قاعدتين للصادر والوارد؛ أما السماح فيضيف قاعدة صادرة فقط ولا يفتح الاتصالات الواردة. تعتمد قاعدة Windows Firewall على مسار الملف لا على بصمته أو ناشره. واجهة IP Helper لا تحدد اتجاه الاتصال الموثوق، ومنافذ UDP المعروضة محلية. عدادات الاستخدام في الذاكرة وتبدأ من تشغيل الخدمة. موفر WFP الحقيقي والوضع الصارم ومصدر تحديثات الشبكة غير منفذة؛ فاحص التحديث الحالي لا يرسل طلب شبكة. لا تجرّب تغييرات الجدار إلا داخل جهاز وهمي بلقطة استعادة.

## المشاريع

- `src/YApex.Core` — نماذج، تحقق مسارات، بروتوكول Pipe، إدارة القواعد والتراجع، تحقق توقيع Ed25519.
- `src/YApex.Service` — Windows Service وNamed Pipe وWindows Firewall COM وIP Helper وسجل SQLite.
- `src/YApex.UI` — WPF بواجهة عربية/إنجليزية وثيم فاتح/داكن وصينية نظام.
- `src/YApex.Cli` — فحص حالة الخدمة وإزالة الطوارئ.
- `src/YApex.Wfp` — طبقة اختبار مغلقة افتراضياً مع رجوع لـWindows Firewall.
- `tests/` — اختبارات Core وأدوات Windows للقراءة فقط.

## البناء

الحل الكامل يتطلب Windows و.NET 10 SDK:

```powershell
dotnet restore YApex.sln
dotnet build YApex.sln -c Release --no-restore
dotnet test YApex.sln -c Release --no-build
```

التفاصيل وتشغيل الخدمة والواجهة في [`docs/build-local.md`](docs/build-local.md). اختبارات Core تعمل عبر الأنظمة. لاختبار الحجب استخدم VM فقط واتبع [`docs/testing.md`](docs/testing.md).

## البيانات والخصوصية

- إعدادات الواجهة في `%APPDATA%\YSWEF\Y-Apex`.
- journal الخدمة في `%ProgramData%\YSWEF\Y-Apex` بصلاحيات مقيدة.
- لا توجد اتصالات تحديث أو إرسال بيانات تلقائية.
- روابط الدعم تُقرأ من `config/shared-links.json`.

## الوثائق

ابدأ بـ [`docs/architecture.md`](docs/architecture.md)، [`docs/threat-model.md`](docs/threat-model.md)، [`docs/privacy.md`](docs/privacy.md)، [`docs/comparison.md`](docs/comparison.md)، [`docs/testing.md`](docs/testing.md)، ثم [`docs/build-local.md`](docs/build-local.md). موقع الوثائق الثابت بالعربية والإنجليزية موجود في [`site/`](site/).

## الرخصة والتواصل

MIT؛ انظر `LICENSE`. التواصل: `170477919+yswef@users.noreply.github.com`.
