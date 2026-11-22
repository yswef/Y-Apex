# هيكل المستودع

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

- src: الكود (يُنشأ في المرحلة 1).
- tests: الاختبارات.
- docs: الوثائق.
- site: موقع التوثيق العام.
- config: إعدادات مشتركة مثل روابط الدعم.
- locales: ملفات الترجمة.
- installer: سكربتات المثبّت.
- assets/icons: الأيقونات المصدرية.
