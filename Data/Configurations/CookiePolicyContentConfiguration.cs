using System;
using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    public class CookiePolicyContentConfiguration : IEntityTypeConfiguration<CookiePolicyContent>
    {
        public void Configure(EntityTypeBuilder<CookiePolicyContent> builder)
        {
            // Инициализиране на съдържанието за страницата /Cookies.
            // Текстът представлява стандартна Политика за бисквитки, която се 
            // визуализира заедно с динамичния списък от категории (управляван отделно в кода).
            builder.HasData(
                new CookiePolicyContent
                {
                    Id = 1,
                    ContentEn = """
                    <p>Effective Date: April 2026</p>
                    <p>This Cookie Policy explains what cookies are, how the Blockchain Education 2026 conference website uses them, and how you can control your preferences at any time.</p>
                    <h2>What Are Cookies?</h2>
                    <p>Cookies are small text files placed on your device when you visit a website. They are widely used to make websites work more efficiently, remember your preferences between visits, and provide information to site owners about how the site is used.</p>
                    <h2>How We Use Cookies</h2>
                    <p>We use cookies across several categories, ranging from those strictly necessary for the site to function, to optional ones that help us analyze traffic and display relevant information about the conference. You can see exactly which categories are currently in use and control your preferences in the "Cookie categories we use" section below.</p>
                    <h2>Third-Party Cookies</h2>
                    <p>Certain cookies may be set by trusted third parties we work with, such as payment processors, analytics providers, or platforms providing embedded content. These third parties are responsible for their own privacy and data protection practices.</p>
                    <h2>Managing Cookies in Your Browser</h2>
                    <p>In addition to the preference center on this site, most browsers allow you to control or delete cookies through their settings. Please note that disabling cookies may affect the functionality of this and other websites you visit.</p>
                    <h2>Changes to This Policy</h2>
                    <p>We may update this Cookie Policy periodically to reflect changes in our practices or for legal reasons. Any updates will be posted directly on this page.</p>
                    <h2>Contact Us</h2>
                    <p>If you have any questions about our use of cookies, please contact us at conference.education@unwe.bg.</p>
                    """,
                     ContentBg = """
                    <p>В сила от: Април 2026</p>
                    <p>Тази Политика за бисквитки обяснява какво представляват бисквитките, как уебсайтът на конференцията Blockchain Education 2026 ги използва и как можете да управлявате своите предпочитания по всяко време.</p>
                    <h2>Какво са бисквитките?</h2>
                    <p>Бисквитките са малки текстови файлове, които се запазват на вашето устройство при посещение на даден уебсайт. Те се използват широко, за да осигурят по-ефективната работа на сайтовете, да запомнят вашите предпочитания между отделните посещения и да предоставят информация на собствениците за начина на потребление.</p>
                    <h2>Как използваме бисквитките</h2>
                    <p>Използваме бисквитки, разпределени в няколко категории, вариращи от строго необходими за работата на сайта до опционални, които ни помагат да анализираме трафика и да показваме подходяща информация за конференцията. В секцията „Категории бисквитки, които използваме“ по-долу можете да видите точно кои категории са активни в момента и да управлявате своите предпочитания.</p>
                    <h2>Бисквитки от трети страни</h2>
                    <p>Определени бисквитки могат да бъдат поставени от доверени трети страни, с които си партнираме, като например платежни оператори, доставчици на анализи или платформи за вградено съдържание. Тези организации носят отговорност за собствените си практики за поверителност и защита на данните.</p>
                    <h2>Управление на бисквитките в браузъра</h2>
                    <p>Освен чрез центъра за предпочитания на този сайт, повечето браузъри ви позволяват да контролирате или изтривате бисквитките чрез собствените си настройки. Имайте предвид, че ограничаването на бисквитките може да повлияе на пълноценното функциониране на този и други уебсайтове.</p>
                    <h2>Промени в тази политика</h2>
                    <p>Възможно е периодично да актуализираме тази Политика за бисквитки, за да отразим промени в нашите практики или поради законови изисквания. Всички промени ще бъдат публикувани на тази страница.</p>
                    <h2>Свържете се с нас</h2>
                    <p>Ако имате въпроси относно използването на бисквитки, моля, свържете се с нас на conference.education@unwe.bg.</p>
                    """,
                    LastUpdatedAt = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            );
        }
    }
}