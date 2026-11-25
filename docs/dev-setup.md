# إعداد بيئة التطوير

## المتطلبات

- Windows 11 x64 لبناء WPF والخدمة واختبار COM/ACL.
- .NET 10 SDK، وVisual Studio مع حمل **.NET desktop development** اختياري للتصحيح المرئي.
- Inno Setup 6 لإنشاء المثبّت.
- Python 3 اختياري لتصدير الأيقونات.

مثال تثبيت SDK والمثبّت عبر Windows Package Manager:

```powershell
winget install Microsoft.DotNet.SDK.10
winget install JRSoftware.InnoSetup
```

تحقق من SDK:

```powershell
dotnet --list-sdks
```

يجب أن ترى `10.0.x`. لا يحتاج `YApex.Core` إلى Windows، لكنه يستهدف `.NET 10`.

## البناء والاختبار

التعليمات المفصلة وأوامر تشغيل الخدمة والواجهة في [`build-local.md`](build-local.md).

```powershell
dotnet restore YApex.sln
dotnet build YApex.sln -c Release --no-restore
dotnet test YApex.sln -c Release --no-build
```

للتجربة التي تعدل Firewall استخدم Windows Sandbox أو VM بلقطة استعادة. لا تشغّلها على جهاز العمل.

## تنزيل الحزم

NuGet ينزّل الحزم عند أول `dotnet restore` ويخزنها في `%USERPROFILE%\.nuget\packages`. تُعاد الاستفادة منها في البناء اللاحق؛ لا ينزّل التطبيق شيئاً عند التشغيل. تنزيل الحزم أثناء التطوير منفصل عن سياسة التطبيق التي لا تتصل بالإنترنت تلقائياً.
