# Y-Apex

جدار ناري ومراقب اتصالات لويندوز. يعرض اتصالات كل برنامج ويتيح حجبها بنقرة.

## حالة المستودع
المستودع في مرحلته الأولى: هيكل المشروع والتوثيق والقواعد فقط. لا يوجد كود تشغيلي بعد ولا إصدار.

## الموجود الآن
- خطة المشروع وقواعد العمل: docs/plan.md و docs/rules.md
- قائمة المهام وطرق اختبارها: TASKS.md
- الرخصة والإشعارات: LICENSE و NOTICE.md
- إعدادات CI وفحص الأسرار وتحديث الحزم: .github/
- موقع التوثيق العام: site/

## الهيكل
```
.editorconfig
.gitattributes
.github/
  ISSUE_TEMPLATE/
    bug_report.md
  PULL_REQUEST_TEMPLATE.md
  dependabot.yml
  workflows/
    ci.yml
    pages.yml
.gitignore
CHANGELOG.md
CONTRIBUTING.md
Directory.Build.props
LICENSE
NOTICE.md
PRIVACY.md
README.en.md
README.md
SECURITY.md
TASKS.md
THIRD-PARTY-NOTICES.md
assets/
  icons/
catalog.entry.json
config/
  shared-links.json
docs/
  architecture.md
  dev-setup.md
  glossary.md
  icons.md
  localization.md
  plan.md
  release.md
  rules.md
  spec.md
  structure.md
  testing.md
  threat-model.md
installer/
locales/
  ar.json
  en.json
site/
  .nojekyll
  app.js
  i18n/
    ar.json
    en.json
  index.html
  style.css
src/
tests/
tools/
```

## البناء
لا شيء يُبنى بعد. عند وجود الكود نتبع docs/dev-setup.md.

## الخصوصية والأمان
يعمل بلا إنترنت افتراضياً ولا يرسل بيانات. التفاصيل في PRIVACY.md و SECURITY.md.

## الرخصة
MIT مبدئياً، وتُراجع قبل أول إصدار علني. انظر LICENSE.

## التواصل
170477919+yswef@users.noreply.github.com