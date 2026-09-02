using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    public class FaqModelConfiguration : IEntityTypeConfiguration<FaqModel>
    {
        public void Configure(EntityTypeBuilder<FaqModel> builder)
        {
            // SEED данни за FAQ
            builder.HasData(
                new FaqModel
                {
                    Id = 1,
                    DisplayOrder = 1,
                    QuestionEn = "I am from the Web3 / FinTech industry. Is this conference suitable for me?",
                    QuestionBg = "Аз съм от Web3 / FinTech индустрията. Подходяща ли е тази конференция за мен?",
                    AnswerEn = "Absolutely. While rooted in academia, Blockchain Education 2026 is specifically designed as a bridge between universities, regulators, and the industry. Day 2 is heavily focused on practical Web3 technologies, DeFi, RWA tokenization, and navigating the MiCA regulatory framework, making it a vital strategic hub for industry professionals and policymakers.",
                    AnswerBg = "Абсолютно. Макар да е сакадемични корени, Blockchain Education 2026 е специално проектирана като мост между университетите, регулаторните и индустрията. Ден 2 е силно фокусиран върху практически Web3 технологии, DeFi, токенизация на реални активи (RWA) и навигиране в регулаторната рамка MiCA, което я прави жизненно стратегически център за професионалисти и есперти."
                },
                new FaqModel
                {
                    Id = 2,
                    DisplayOrder = 2,
                    QuestionEn = "What is Blockchain Education 2026?",
                    QuestionBg = "Какво представлява Blockchain Education 2026?",
                    AnswerEn = "Blockchain Education 2026 is a premier academic and institutional forum dedicated to the future of cryptoeconomics and Web3. It goes beyond a traditional conference by bridging the gap between European universities, regulatory bodies, and the digital finance industry to establish unified educational standards and research frameworks.",
                    AnswerBg = "Blockchain Education 2026 е водеща академичне и институционален форум, посветен на бъдещето на криптоикономика и Web3. Той надхвърля традиционната конференция, преодолявайки пропаства между европейските университети, регулаторните органи и индустрията за дигитални финанси с цел установяване на единни образователни стандарти и изследователски рамки."
                },
                new FaqModel
                {
                    Id = 3,
                    DisplayOrder = 3,
                    QuestionEn = "What is the conference language?",
                    QuestionBg = "Какъв е работният език на конференцията?",
                    AnswerEn = "The main conference language is English, allowing both local and international participants to collaborate easily across sessions.",
                    AnswerBg = "Основният език на конференцията е английски, което позволява на местни и международни участници да си сътрудничат лесно по време на всички сесии."
                },
                new FaqModel
                {
                    Id = 4,
                    DisplayOrder = 4,
                    QuestionEn = "How do I submit a paper?",
                    QuestionBg = "Как да изпратя доклад?",
                    AnswerEn = "You can submit your abstract and full paper through the official conference portal on this website. Detailed submission guidelines, including formatting templates and peer-review requirements, are available in the conference section.",
                    AnswerBg = "Можете да изпратите своето резюме и пълен доклад чрез официалния портал на конференцията на този уебсайт. Подробни указания за подаване, включително шаблони за форматиране и изисквания за рецензиране, са налични в секцията за конференция."
                },
                new FaqModel
                {
                    Id = 5,
                    DisplayOrder = 5,
                    QuestionEn = "Is participation free for students?",
                    QuestionBg = "Безплатно ли е участието за студенти?",
                    AnswerEn = "To encourage young academic talent, we offer specially subsidized registration tiers for undergraduate, Master's, and PhD students who wish to present a paper and actively participate in the conference. To access these rates, you will need to upload a valid student ID or enrollment certificate during the registration and submission process. Please visit the Registration page to view the exact student rates.",
                    AnswerBg = "За да насърчим младите академични таланти, предлагаме специално субсидирани такси за регистрация за студенти, магистри и докторанти, които желаят да представят доклад и да участват активно в конференцията. За да се възползвате от тези тарифи,ще рябва да прикачите валидна студентска книжка или уверение по време на процеса на регистрация. Моля, посетете страницата \"Билети\", за да видите точните студентски такси."
                },
                new FaqModel
                {
                    Id = 6,
                    DisplayOrder = 6,
                    QuestionEn = "What is the Sofia Declaration?",
                    QuestionBg = "Какво представлява Софийската декларация?",
                    AnswerEn = "The Sofia Declaration is the cornerstone outcome of Blockchain Education 2026. It represents a formal academic commitment to shared European standards in blockchain and cryptoeconomics education. Signed on Day 3 by participating universities and institutional partners, it establishes a unified framework for curriculum development, joint research, and regulatory alignment across the academic sector.",
                    AnswerBg = "Софийската декларация е крайъгълният камък на Blockchain Education 2026. Тя представлява официален академичен ангажимент към споделени европейски стандарти в образованието по блокчейн и криптоикономика. Подписана в Ден 3 от участващите университети и институционални партньори, тя установява единна рамка за разработване на учебни програми, съвместни изследвания и регулаторно съгласуване в академичния сектор."
                },
                new FaqModel
                {
                    Id = 7,
                    DisplayOrder = 7,
                    QuestionEn = "How do I get to the venue?",
                    QuestionBg = "Как да стигна до мястото на събитието?",
                    AnswerEn = "The conference is officially hosted at the University of National and World Economy (UNWE) campus in Sofia, Bulgaria. Detailed navigation guides, including public transport routes, parking information, and airport transfer options, are available in the dedicated <a href=\"/Travel\" style=\"text-decoration: underline;\">Travel section</a> of our website.",
                    AnswerBg = "Конференцията се провежда официално в кампуса на УНиверситета за национално и световно стопанство (УНСС) в София, България. Подробни ръководства за навигация, включително маршрути на градския транспорт, информация за паркиране и опции за трансфер от летището, са налични в специалната <a href=\"/Travel\" style=\"text-decoration: underline;\">секция за пътуване</a> на нашия уебсайт."
                },
                new FaqModel
                {
                    Id = 8,
                    DisplayOrder = 8,
                    QuestionEn = "Can I attend online?",
                    QuestionBg = "Мога ли да присъствам онлайн?",
                    AnswerEn = "Yes. Blockchain Education 2026 is designed as a hybrid event, allowing participants who are unable to travel to join selected sessions online. Presenting authors may also have the opportunity to deliver their presentations remotely, subject to the conference format and technical arrangements.<br><br>While online participation provides access to key conference activities, attending in person offers the most complete experience, including direct interaction with speakers, networking opportunities, informal discussions, exhibition activities, and the official signing of the Sofia Declaration. We therefore strongly encourage participants to attend on-site whenever possible.",
                    AnswerBg = "Да. Blockchain Education 2026 е организирана като хибридно събитие, което предоставя възможност на участниците, които не могат да пътуват, да се включат онлайн в избрани сесии. Авторите на научни доклади също могат да имат възможност да представят своите разработки дистанционно, в зависимост от окончателния формат на конференцията и техническите условия за провеждането ѝ.<br><br>Макар онлайн участието да предоставя достъп до ключови елементи от програмата на конференцията, присъстват на място предлага най-пълноценото преживяване, включително директен контакт с лекторите, възможности за професионално общуване, неформални дискусии, съпътстващи дейности в рамките на конференцията и участние в официалното подписване на Софийската декларация. Пради тази причина силно насърчаваме участниците да присъстват на място, когато това е възможно."
                },
                new FaqModel
                {
                    Id = 9,
                    DisplayOrder = 9,
                    QuestionEn = "Who organizes the conference?",
                    QuestionBg = "Кой организира конференцията?",
                    AnswerEn = "The conference is officially organized by the Institute of Cryptoeconomics, Blockchain and Innovation (ICBI). ICBI is a highly specialized academic and research institute within the University of National and World Economy (UNWE), established by a Decree of the Bulgarian Council of Ministers.",
                    AnswerBg = "Конференцията се организира официално от Института по криптоикономика, блокчейн и иновации (ICBI). ICBI е високо специализиран академичен и изследователски институт към Университета за национално и световно стопанство (УНСС), създаден с Постановление на Министерския съвет на Република България."
                }
            );
        }
    }
}
