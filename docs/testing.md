# الاختبار

## اختبارات آلية

- `tests/YApex.Core.Tests`: تحقق مسار Windows، رسائل Pipe ذات طول/إصدار/حقول مكررة، إدخالات fuzz حتمية، متجه Ed25519 من RFC 8032، ومعاينة/تأكيد/تراجع/مؤقت/طوارئ باستخدام ساعة وجدار وهميين.
- `tests/YApex.Service.Tests`: اختبار Windows فقط يقرأ جداول الاتصالات وقواعد Y-Apex دون إجراء أي تغيير. يحتاج Windows؛ لا يشغّل الاختبار تغيير قواعد الجدار.
- أوامر Core عبر الأنظمة:

```powershell
dotnet test tests/YApex.Core.Tests/YApex.Core.Tests.csproj -c Release
```

- مجموعة Windows كاملة:

```powershell
dotnet test YApex.sln -c Release
```

## اختبار الحجب اليدوي داخل VM فقط

1. أنشئ لقطة (snapshot) لجهاز Windows وهمي يعمل على شبكة اختبار. لا تجرّب الحجب على جهازك الأساسي.
2. أنشئ مجلداً مثل `C:\Y-Apex-Test` وانسخ إليه برنامج اختبار شبكي تملكه. لا تختَر ملف Y-Apex أو Windows أو برنامج الحماية.
3. ابنِ الحل. من PowerShell مسؤول في جلسة سطح المكتب نفسها، سجّل SID المستخدم المستهدف:

```powershell
dotnet run --project src/YApex.Cli/YApex.Cli.csproj -- register-pipe-user --interactive
```

4. شغّل الخدمة للاختبار من نافذة مسؤول باستخدام `dotnet run --project src/YApex.Service/YApex.Service.csproj -- --service`. اتركها مفتوحة، ثم شغّل `YApex.UI` بصلاحيات عادية. تحقق من حالة الخدمة وظهور اسم ومسار برنامج الاختبار. قارن TCP مع:

```powershell
Get-NetTCPConnection | Sort-Object OwningProcess, LocalPort
```

5. حدّد صف برنامج الاختبار. اختر «حجب»، واقرأ معاينة المسار وبصمة الملف والقواعد قبل الموافقة. ارفض المعاينة إن لم تكن القاعدة تخص ملف الاختبار.
6. داخل الـ VM فقط، تحقّق من انقطاع اتصال ذلك البرنامج دون توقف بقية التطبيقات. استخدم زر التراجع أو اترك المؤقت 30 ثانية، ثم تحقق من عودة الاتصال.
7. جرّب «تأكيد أن الإنترنت يعمل»، ثم «تراجع». أخيراً جرّب الطوارئ، وتحقق من إزالة قواعد Y-Apex فقط:

```powershell
Get-NetFirewallRule | Where-Object { $_.Name -like 'Y-Apex *' } | Select-Object Name, Enabled, Direction, Action
```

8. أعد لقطة الـ VM بعد الاختبار. لا تشغّل الوضع الصارم: غير متاح في هذا الإصدار.

## قياس الأداء على Windows

بعد البناء، قِس فتح الواجهة من تشغيلها حتى ظهور النافذة، وخمولها وخمول الخدمة في Task Manager لمدة خمس دقائق، والذاكرة الخاصة لكل عملية. اختبر الواجهة العادية والمخفاة إلى الصينية. سجّل متوسط CPU والذاكرة وزمن الفتح في تقرير مستقل؛ لا تُستنتج الحدود من اختبار الوحدة.
