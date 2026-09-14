# Компоненти на трети лица

Настоящият файл е неразделна част от `LICENSE.md` (чл. 9) и съпътства Софтуера при всяко разпространение, включително в компилирания му вид.

Софтуерът съдържа компоненти, създадени от трети лица и разпространявани под собствени лицензи. Изброените по-долу условия се прилагат само към съответните компоненти и не засягат режима на самия Софтуер.

**Няма компоненти под GPL, AGPL, LGPL или друг copyleft лиценз.** Всички използвани лицензи са permissive и са съвместими със собственическия режим на Софтуера.

---

## 1. Сървърни компоненти (.NET)

### 1.1. Компоненти под MIT

Следните пакети и техните зависимости се разпространяват под лиценза MIT:

- **.NET и ASP.NET Core** — © Microsoft Corporation и .NET Foundation
  Включително: `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.AspNetCore.Diagnostics.EntityFrameworkCore`, `Microsoft.AspNetCore.Cryptography.*` (9.0.12)
- **Entity Framework Core** — © Microsoft Corporation и .NET Foundation
  `Microsoft.EntityFrameworkCore` и свързаните с него пакети (9.0.12), `Microsoft.EntityFrameworkCore.Sqlite` (9.0.12)
- **Microsoft.Data.Sqlite** (10.0.7) — © Microsoft Corporation
- **Newtonsoft.Json** (13.0.3) — © James Newton-King
- Библиотеките от семейството `System.*`, разпространявани отделно от .NET runtime — © Microsoft Corporation и .NET Foundation

**Текст на лиценза MIT:**

> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

### 1.2. Компоненти под Apache License 2.0

- **Stripe.net** (51.1.0) — © Stripe, Inc.
- **SQLitePCLRaw** (2.1.13) — © SourceGear LLC
  Включително: `SQLitePCLRaw.core`, `SQLitePCLRaw.bundle_e_sqlite3`, `SQLitePCLRaw.lib.e_sqlite3`, `SQLitePCLRaw.provider.e_sqlite3`

Пълният текст на Apache License 2.0 е достъпен на `https://www.apache.org/licenses/LICENSE-2.0`.

Съгласно § 4, буква „г“ от Apache License 2.0, ако съответният компонент съдържа файл `NOTICE`, неговото съдържание се възпроизвежда тук. Към момента на съставяне на настоящия файл използваните компоненти под Apache 2.0 не изискват допълнителни означения извън посочените по-горе.

### 1.3. Компоненти в обществено достояние

- **SQLite** — вграденият машинен код на SQLite (`e_sqlite3`) е поставен от неговите автори в обществено достояние (public domain) и не поражда задължения за атрибуция. Отбелязва се за пълнота.

### 1.4. Инструменти за разработка

Следните пакети се използват само по време на разработка и **не се включват в разпространявания Софтуер**: `Microsoft.EntityFrameworkCore.Tools`, `Microsoft.EntityFrameworkCore.Design`, `Microsoft.VisualStudio.Web.CodeGeneration.*`, `Microsoft.Build.*`, `Microsoft.CodeAnalysis.*`, `NuGet.*`, `Mono.TextTemplating`, `Humanizer`.

Двата пакета, които ги влачат, носят `PrivateAssets="all"` в `ConferenceApp.csproj`, тоест твърдението по-горе е наложено от build-а, а не само декларирано.

Тъй като не се разпространяват, техните лицензи не пораждат задължения по отношение на разпространения Софтуер. Отбелязват се за прозрачност.

---

## 2. Клиентски библиотеки

Софтуерът не разпространява клиентски библиотеки на трети страни. Bootstrap, jQuery,
jQuery Validation и jQuery Validation Unobtrusive бяха премахнати заедно с
`wwwroot/lib/` — нито един ред от тях не се е ползвал. Фронтендът е на чист
JavaScript, а валидацията от страна на клиента е собствена
(`wwwroot/js/validation.js`).

---

## 3. Шрифтове (`wwwroot/fonts`)

| Шрифт | Автор | Лиценз | Лицензен файл |
|---|---|---|---|
| Oswald | © The Oswald Project Authors (Vernon Adams, Kalapi Gajjar, Cyreal) | SIL Open Font License 1.1 | ✅ в `wwwroot/fonts` |

**Условия по SIL OFL 1.1, които остават в сила:**

- лицензният текст съпътства файловете на шрифта при всяко разпространение;
- файловете на шрифта не могат да се продават самостоятелно;
- модифицирани версии не могат да носят името „Oswald“;
- софтуер, който вгражда шрифта, може да се разпространява при всякакви условия, включително собственически.

---

## 4. Икони и графично оформление

Всички икони в Софтуера са **векторна графика (SVG), създадена от Автора**. Не се използват Font Awesome, Bootstrap Icons или друга външна иконна библиотека.

Стиловите файлове в `wwwroot/css`, съдържанието на `wwwroot/templates` и цялото графично оформление са **изцяло създадени от Автора**. Не се използва закупена или изтеглена HTML тема.

Този раздел не поражда задължения за атрибуция и се включва за пълнота.

---

## 5. Марки и означения

Логата и означенията в `wwwroot/images/logos`, `wwwroot/images/icbi` и `wwwroot/uploads/homepagelogos`, както и фотографиите на лектори и участници, принадлежат на съответните им притежатели, не са компоненти на трети лица по смисъла на настоящия файл и не са предмет на лиценза на Софтуера. Виж чл. 10 от `LICENSE.md`.

---

## 6. Външни услуги

Софтуерът се свързва с външни услуги, чиито условия за ползване са отделни от настоящия документ и не представляват лицензирани компоненти:

| Услуга | Приложими условия |
|---|---|
| Stripe | Stripe Services Agreement; изисквания на PCI DSS |
| Go28 CRM | Общи условия на доставчика; необходим е договор за обработване по чл. 28 GDPR |
| Microsoft 365 (SMTP) | Условия на институционалния акаунт |

---

## 7. Поддържане на файла

При добавяне или премахване на компонент настоящият файл се актуализира. За извличане на пълния списък:

```
dotnet list package --include-transitive
```

Файлът се включва в изхода на публикуването чрез запис в `ConferenceApp.csproj`:

```xml
<Content Include="THIRD-PARTY-NOTICES.md" CopyToOutputDirectory="Always" />
```