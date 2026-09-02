using System;
using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    public class CookieNoticeContentConfiguration : IEntityTypeConfiguration<CookieNoticeContent>
    {
        public void Configure(EntityTypeBuilder<CookieNoticeContent> builder)
        {
            // Инициализиране на главния текст на банера (заглавие и параграф в един Quill блок).
            // Съдържанието е мигрирано директно от стария статичен код.
            builder.HasData(
                new CookieNoticeContent
                {
                    Id = 1,
                    ContentEn = """
                    <h2>We value your privacy</h2>
                    <p>We use cookies to ensure the site functions properly, analyze usage, and enhance your overall experience. You can choose your preferences and update them at any time. For more details, please review our <a href="/Cookies">Cookie Policy</a>.</p>
                    """,
                    ContentBg = """
                    <h2>Ценим вашата поверителност</h2>
                    <p>Използваме бисквитки, за да осигурим правилното функциониране на сайта, да анализираме потреблението му и да подобрим вашето изживяване. Можете да изберете своите предпочитания и да ги промените по всяко време. За повече информация, моля, разгледайте нашата <a href="/Cookies">Политика за бисквитки</a>.</p>
                    """,
                    LastUpdatedAt = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            );
        }
    }
}