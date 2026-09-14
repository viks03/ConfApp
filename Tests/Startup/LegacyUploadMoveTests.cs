// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Startup;

/// <summary>
/// Part 1, moving the uploaded files on a first run. Phase 2 took them out of
/// <c>wwwroot</c> ([F-02]), so the static-file middleware can no longer serve
/// them anonymously.
/// <para>
/// The probe instance gets a webroot of its OWN: the real one holds live
/// uploads, and startup MOVES whatever it finds. A test that touches those is
/// more dangerous than the bug it is looking for.
/// </para>
/// </summary>
public class LegacyUploadMoveTests
{
    [Fact]
    public async Task Файловете_от_wwwroot_се_преместват_и_старият_адрес_връща_404()
    {
        const string paperName = "legacy-paper.pdf";
        const string docName   = "legacy-id.jpg";

        // The files are put into the probe's webroot BEFORE it starts: as far as it
        // is concerned, this is a first run.
        var second = await StartupProbe.StartAsync("legacy-move", prepareWebRoot: probe =>
        {
            var papers = Path.Combine(probe.WebRoot, "uploads", "papers26");
            var docs   = Path.Combine(probe.WebRoot, "uploads", "submitted-documents");

            Directory.CreateDirectory(papers);
            Directory.CreateDirectory(docs);

            File.WriteAllText(Path.Combine(papers, paperName), "доклад");
            File.WriteAllText(Path.Combine(docs,   docName),   "документ");
            File.WriteAllText(Path.Combine(papers, ".gitkeep"), string.Empty);
        });

        Assert.Null(second.StartupError);

        // ── The log has to say how many files were moved ───────────────
        var line = await second.WaitForLogLineAsync("Преместени", TimeSpan.FromSeconds(20));

        Assert.NotNull(line);
        Assert.Contains("файла", line!);
        Assert.Contains("uploads/papers26", second.ReadLog());

        // ── The file is under the private root now, not in wwwroot ─────
        var privateRoot  = Path.Combine(second.Scratch, "private");
        var legacyPapers = Path.Combine(second.WebRoot, "uploads", "papers26");
        var legacyDocs   = Path.Combine(second.WebRoot, "uploads", "submitted-documents");

        Assert.True(File.Exists(Path.Combine(privateRoot, "uploads", "papers26", paperName)),
            "Докладът не е под частния корен.");
        Assert.True(File.Exists(Path.Combine(privateRoot, "uploads", "submitted-documents", docName)),
            "Документът за верификация не е под частния корен.");

        Assert.False(File.Exists(Path.Combine(legacyPapers, paperName)),
            "Докладът е още в wwwroot — раздава се анонимно.");
        Assert.False(File.Exists(Path.Combine(legacyDocs, docName)),
            "Документът е още в wwwroot — раздава се анонимно.");

        // .gitkeep is skipped deliberately: the folder has to stay in git.
        Assert.True(File.Exists(Path.Combine(legacyPapers, ".gitkeep")),
            ".gitkeep е преместен, а не бива.");

        // ── The old address answers 404 ────────────────────────────────
        using var client = second.NewClient(followRedirects: false);

        foreach (var url in new[]
                 {
                     $"/uploads/papers26/{paperName}",
                     $"/uploads/submitted-documents/{docName}"
                 })
        {
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task Старите_папки_в_хранилището_са_празни()
    {
        // If a file is still in the real wwwroot, the move has not happened on this
        // machine, and every such file is being served anonymously.
        foreach (var folder in new[] { "uploads/papers26", "uploads/submitted-documents" })
        {
            var path = Path.Combine(TestPaths.RepoRoot, "wwwroot",
                folder.Replace('/', Path.DirectorySeparatorChar));

            if (!Directory.Exists(path)) continue;

            var leftovers = Directory
                .EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Where(f => Path.GetFileName(f) != ".gitkeep")
                .ToList();

            Assert.True(leftovers.Count == 0,
                $"В wwwroot/{folder} са останали {leftovers.Count} файла: " +
                string.Join(", ", leftovers.Select(Path.GetFileName)) +
                ". Раздават се анонимно — вдигни приложението веднъж или ги премести на ръка.");
        }
    }
}
