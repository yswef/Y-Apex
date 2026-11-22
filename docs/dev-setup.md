# إعداد بيئة التطوير (مرة واحدة)

```
winget install Git.Git
winget install Microsoft.DotNet.SDK.10
winget install JRSoftware.InnoSetup
```
ثم Visual Studio Community مع حمل ".NET desktop development".
فعّل Windows Sandbox أو جهازاً وهمياً لاختبار الحجب.

## البناء
```
dotnet restore
dotnet build -c Release
dotnet test
dotnet publish -c Release -r win-x64 --self-contained false -o out
```
المكتبات (NuGet) تُحمَّل تلقائياً في أول restore إلى مخزن عام على الجهاز، ولا تحتاج تنزيلاً كل مرة.