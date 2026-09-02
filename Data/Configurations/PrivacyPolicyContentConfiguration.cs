using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    public class PrivacyPolicyContentConfiguration : IEntityTypeConfiguration<PrivacyPolicyContent>
    {
        public void Configure(EntityTypeBuilder<PrivacyPolicyContent> builder)
        {
            // SEED: singleton ред за Privacy Policy / GDPR съдържанието —
            // мигрирано 1:1 от старите Pages.Privacy.en/bg.resx файлове,
            // за да не се промени нищо визуално в деня на deploy. Оттук
            // нататък се редактира от админ панела, не от resx.
            builder.HasData(
                new PrivacyPolicyContent
                {
                    Id = 1,
                    ContentEn = """
                    <p>PRIVACY POLICY</p>
                    <p>Effective Date: April 2026</p>
                    
                    <p>1. Introduction Welcome to the official website of the Blockchain Education 2026 conference. We respect your privacy and are committed to protecting your personal data in compliance with the General Data Protection Regulation (GDPR). This Privacy Policy explains how we collect, process, and safeguard your information when you visit our website, register for the event, make purchases, or subscribe to our communications.</p>
                    <p>2. The Data We Collect About You We may collect, use, and store different kinds of personal data about you, including:<br>Identity & Contact Data: First name, last name, email address, phone number, and professional/academic affiliation provided during registration.<br>Financial & Transaction Data: If you make a purchase, we collect details about payments to and from you and other details of products or services you have purchased from us. We do not store full credit card numbers or crypto wallet private keys; all financial transactions are securely processed by our authorized third-party payment gateways.<br>Technical Data: Internet protocol (IP) address, browser type and version, time zone setting, and operating system collected automatically when you use our site. Marketing & Communications Data: Your preferences in receiving marketing and newsletters from us.</p>
                    <p>3. How We Use Your Personal Data We will only use your personal data when the law allows us to. Most commonly, we use your data to: Register you as an attendee, process your purchases, and manage event logistics. Send you administrative information, schedule updates, and practical information. Send you relevant newsletters and marketing communications regarding future events, provided you have explicitly opted in (Active Consent). You may opt out at any time using the "unsubscribe" link in our emails.<br><br>Improve our website layout and user experience through analytics.</p>
                    <p>4. Data Sharing and Third Parties We do not sell your personal data. We may share your data with trusted third parties strictly to facilitate event operations:<br>Payment Processors: We share necessary transaction data with authorized payment gateways (e.g., card processors, crypto payment providers) strictly for the purpose of completing your purchases and preventing fraud.<br>Event Services: Email marketing service providers and secure ticketing platforms. These third parties are bound by strict data processing agreements and comply with GDPR standards.</p>
                    <p>5. Cookie Policy Our website uses cookies to distinguish you from other users, providing a better browsing experience and allowing us to improve our site. Essential Cookies: Required for the operation of our website (e.g., submitting forms, processing cart payments).<br>Analytical Cookies: Allow us to recognize and count the number of visitors and see how visitors move around our website. By continuing to browse the site, you consent to our use of essential cookies. You may disable cookies in your browser settings, but some parts of the site may become inaccessible.</p>
                    <p>6. Your Legal Rights (GDPR) Under certain circumstances, you have rights under data protection laws in relation to your personal data, including the right to: Request access to your personal data.<br>Request correction of incomplete or inaccurate data.<br>Request erasure of your personal data ("Right to be forgotten"). Object to processing of your data for direct marketing purposes. Withdraw your consent at any time. To exercise any of these rights, please contact us at conference.education@unwe.bg.</p>
                    <p>7. Contact Details If you have any questions about this Privacy Policy or our privacy practices, please contact us at:<br>Email: conference.education@unwe.bg</p>
                    """,
                    ContentBg = """
                    <p>ПОЛИТИКА ЗА ПОВЕРИТЕЛНОСТ</p>
                    <p>В сила от: Април 2026</p>

                    <p>1. Въведение Добре дошли на официалния уебсайт на конференцията Blockchain Education 2026. Ние уважаваме вашата поверителност и се ангажираме да защитаваме вашите лични данни в съответствие с Общия регламента за защита на данните (GDPR). Тази Политика за поверителност обяснява как събираме, обработваме и съхраняваме вашата информация, когато посещавате нашия уебсайт, регистрирате за събитието, правите покупки или се абонирате за нашите съобщения.</p>
                    <p>2. Данните, които събираме за вас Можем да събираме, използваме и съхраняваме различни видове лични данни за вас, включително:<br>Данни за идентичност и контакт: Име, фамилия, имейл адрес, телефонен номер и професионална/академична принадлежност, предоставени по време на регистрация.<br>Финансови и транзакционни данни: Ако направите покупка, събираме подробности за плащанията към и от вас и други подробности за продукти или услуги, които сте закупили от нас. Ние не съхраняваме пълни номера на кредитни карти или частни ключове на крипто портфейли; всички финансови транзакции се обработват сигурно от нашите оторизирани платежни портали на трети страни.<br>Технически данни: IP адрес, тип и версия на браузъра, настройка на часовата зона и операционна система, събирани автоматично, когато използвате нашия сайт. Маркетингови и комуникационни данни: Вашите предпочитания за получаване на маркетингови съобщения и бюлетини от нас.</p>
                    <p>3. Как използваме вашите лични данни Ще използваме вашите лични данни само когато законът ни позволява. Най-често използваме вашите данни, за да: Ви регистрираме като присъстващ, обработваме вашите покупки и управляваме логистиката на събитието. Изпращаме ви административна информация, актуализации на графика и практическа информация. Изпращаме ви подходящи бюлетини и маркетингови съобщения относно бъдещи събития, при условие че изрично сте се съгласили (активно съгласие). Можете да се откажете по всяко време, като използвате връзката „отписване“ в нашите имейли.<br><br>Подобряваме оформлението на нашия уебсайт и потребителското изживяване чрез анализи.</p>
                    <p>4. Споделяне на данни и трети страни Ние не продаваме вашите лични данни. Можем да споделяме вашите данни с доверени трети страни единствено за улесняване на операциите по събитието:<br>Платежни оператори: Споделяме необходимите данни за транзакциите с оторизирани платежни портали (напр. картови оператори, доставчици на крипто плащания) стриктно с цел завършване на вашите покупки и предотвратяване на измами.<br>Услуги за събития: Доставчици на услуги за имейл маркетинг и сигурни платформи за продажба на билети. Тези трети страни са обвързани от строги договори за обработка на данни и спазват стандартите на GDPR.</p>
                    <p>5. Политика за бисквитки Нашият уебсайт използва бисквитки, за да ви отличи от другите потребители, осигурявайки по-добро изживяване при сърфиране и позволявайки ни да подобряваме нашия сайт. Основни бисквитки: Необходими за работата на нашия уебсайт (напр. изпращане на формуляри, обработка на плащания в количката).<br>Аналитични бисквитки: Позволяват ни да разпознаваме и преброяваме броя на посетителите и да виждаме как посетителите се движат из нашия уебсайт. Продължавайки да разглеждате сайта, вие се съгласявате с използването на основни бисквитки. Можете да деактивирате бисквитките в настройките на вашия браузър, но някои части от сайта могат да станат недостъпни.</p>
                    <p>6. Вашите законни права (GDPR) При определени обстоятелства имате права съгласно законите за защита на данните във връзка с вашите лични данни, включително правото на: Изискване на достъп до вашите лични данни.<br>Изискване на коригиране на непълни или неточни данни.<br>Изискване на изтриване на вашите лични данни („Право да бъдеш забравен“). Възражение срещу обработването на вашите данни за целите на директния маркетинг. Оттегляне на вашето съгласие по всяко време. За да упражните някое от тези права, моля, свържете се с нас на conference.education@unwe.bg.</p>
                    <p>7. Данни за контакт Ако имате въпроси относно тази Политика за поверителност или нашите практики за поверителност, моля, свържете се с нас на:<br>Имейл: conference.education@unwe.bg</p>
                    """,
                    LastUpdatedAt = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            );
        }
    }
}
