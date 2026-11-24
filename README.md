# Y-Apex

مراقب اتصالات Windows ومدير محلي لقواعد جدار الحماية. الواجهة تعمل بصلاحيات المستخدم، وتطلب معاينة وتأكيداً قبل تغيير قواعد Y-Apex. لا يستخدم التطبيق `netsh` ولا يرسل بيانات إلى خادم.

## حالة التنفيذ

هذه نسخة تطويرية أولية وليست إصداراً جاهزاً للاستخدام على جهاز أساسي. يتضمن الحل .NET 10 خدمة Windows وواجهة WPF وCLI وطبقة WFP تجريبية. الشيفرة تضم مراقبة TCP/UDP، قواعد Windows Firewall المملوكة للتطبيق مع معاينة وتأكيد وتراجع مؤقت، سجل تغييرات، سجل استخدام ETW منذ بدء الخدمة، تصديراً، ومطالبة أول اتصال. لكنها **لم تُبنَ أو تُختبر على Windows في هذه البيئة**؛ يجب التحقق منها في CI وعلى جهاز وهمي قبل الاستخدام. اتجاه بعض الاتصالات غير معروف، ومنافذ UDP المعروضة محلية.

عدادات الاستخدام محفوظة في الذاكرة منذ بدء الخدمة ولا تستعيد بيانات ما قبل التشغيل. موفر WFP الحقيقي والوضع الصارم ومصدر تحديثات الشبكة غير منفّذة؛ فاحص التحديث الحالي لا يرسل أي طلب شبكة. لا تفعّل حجباً فعلياً إلا داخل جهاز وهمي بلقطة استعادة.

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

ابدأ بـ [`docs/architecture.md`](docs/architecture.md)، [`docs/threat-model.md`](docs/threat-model.md)، [`TASKS.md`](TASKS.md)، ثم [`docs/build-local.md`](docs/build-local.md).

## الرخصة والتواصل

MIT؛ انظر `LICENSE`. التواصل: `170477919+yswef@users.noreply.github.com`.
