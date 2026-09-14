// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Text;
using System.Text.Json;

namespace ConferenceApp.Services.Theming
{
    /// <summary>
    /// Builds the file an administrator downloads and hands to a language
    /// model.
    ///
    /// <para>
    /// The file is JSON, but it carries its own instructions inside, in the
    /// <c>_instructions</c> field. The reason: whoever sends it off will not
    /// write an explanation themselves, and instructions kept in a separate
    /// document are forgotten, which makes the result arbitrary.
    /// </para>
    ///
    /// <para>
    /// Fields starting with an underscore are ignored on import — they are there
    /// to be read, not applied. The instruction text is written for the
    /// administrator and the model, so it stays in Bulgarian.
    /// </para>
    /// </summary>
    public static class ThemeTemplate
    {
        public static string Build(IDictionary<string, string>? current = null)
        {
            string Val(TokenSpec s) =>
                current is not null && current.TryGetValue(s.Name, out var v) ? v : s.Default;

            var tokens = new Dictionary<string, object>();
            foreach (var s in ThemeTokens.All) tokens[s.Name] = Val(s);

            var doc = new Dictionary<string, object>
            {
                ["_instructions"] = Instructions(),
                ["_tokenGuide"] = ThemeTokens.All.ToDictionary(
                    s => s.Name,
                    s => new Dictionary<string, string>
                    {
                        ["type"] = s.Kind switch
                        {
                            TokenKind.Color   => "цвят — #rgb, #rrggbb или #rrggbbaa",
                            TokenKind.Triplet => "три числа 0-255, разделени със запетаи",
                            TokenKind.Rgba    => "rgba(r, g, b, алфа)",
                            TokenKind.Length  => "число с мерна единица — 1180px",
                            TokenKind.Shadow  => "сянка — 0 30px 80px rgba(0,0,0,0.35)",
                            TokenKind.Clamp   => "clamp(мин, предпочитано, макс)",
                            _ => "текст"
                        },
                        ["description"] = s.Description,
                        ["current"] = Val(s)
                    }),
                ["name"] = "Моята тема",
                ["tokens"] = tokens
            };

            return JsonSerializer.Serialize(doc, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }

        private static string[] Instructions() => new[]
        {
            "ЗАДАЧА: направи нова тема за този сайт, като промениш стойностите в „tokens“.",
            "",
            "ВЪРНИ САМО JSON със същата структура. Без обяснения около него, без",
            "код-огради. Полетата, започващи с долна черта, може да ги махнеш.",
            "",
            "ПРАВИЛА, КОИТО СА ЗАДЪЛЖИТЕЛНИ:",
            "",
            "1. Не измисляй нови ключове. Приемат се само изброените в „_tokenGuide“.",
            "   Всичко друго се пропуска при внасяне.",
            "",
            "2. Спазвай типа на всяка стойност. Цвят значи #1a1a1a, не „red“ и не",
            "   „rgb(26,26,26)“. Стойност от друг тип се отказва цялата.",
            "",
            "3. --ink е ТРИ ЧИСЛА, не цвят. Той е основата на около 490 полупрозрачни",
            "   рамки и фонове из целия сайт. На тъмна тема е „255, 255, 255“; на",
            "   светла — „0, 0, 0“. Ако сбъркаш този, темата изглежда счупена, а не",
            "   различна.",
            "",
            "4. --line и --panel-soft трябва да са СЪЩИЯТ цвят като --ink, но с алфа.",
            "   При светла тема: rgba(0, 0, 0, 0.08) и rgba(0, 0, 0, 0.04).",
            "",
            "5. Състоянията --ok, --warn, --danger НЕ се обръщат. Зеленото остава",
            "   зелено и на светъл фон. Може да ги затъмниш за контраст, но не",
            "   сменяй тона им.",
            "",
            "6. КОНТРАСТ. Това е академичен сайт и текстът трябва да се чете:",
            "      --text спрямо --bg      поне 7:1",
            "      --muted спрямо --bg     поне 4.5:1",
            "      --text-dim спрямо --bg  поне 4.5:1",
            "      --accent спрямо --bg    поне 3:1",
            "   Провери ги, преди да върнеш отговор. Ниският контраст е най-честата",
            "   грешка при генерирани теми.",
            "",
            "7. Един акцент. Сайтът има ЕДИН акцентен цвят. Не въвеждай втори — той",
            "   ще се бори с първия на всяка страница.",
            "",
            "8. --shadow при светла тема иска по-малка алфа. 0.35 върху бяло е",
            "   мръсно петно; 0.12 е сянка.",
            "",
            "ЗА КОНТЕКСТ: сайтът е на международна научна конференция за",
            "криптоикономика и блокчейн (УНСС, София). Шрифтът е Oswald, ъглите са",
            "прави — нула радиус. Стилът е технически и сдържан, не игрив.",
            "",
            "Ако правиш светла тема, знай че три неща няма да се обърнат само от",
            "токените и ще изглеждат различно: глобалният фонов слой, наслагванията",
            "върху снимките в банерите, и сенките. Първите две се оправят отделно."
        };
    }
}
