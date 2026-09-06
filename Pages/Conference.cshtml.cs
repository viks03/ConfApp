using ConferenceApp.Data;
using ConferenceApp.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Pages
{
    public class ConferenceModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        // Списъци за Комитети
        public List<CommitteeMemberModel> OrganizingCommittee { get; set; } = new();
        public List<CommitteeMemberModel> ProgramCommittee { get; set; } = new();
        public List<CommitteeMemberModel> StudentCommittee { get; set; } = new(); // НОВО: Списък за Студентски комитет

        /// <summary>Документите за автори — качват се от админ панела.</summary>
        public Dictionary<string, DownloadableFile> Documents { get; set; } = new();

        // Списъци за Партньори
        public List<PartnerModel> InstitutionalPartners { get; set; } = new();
        public List<PartnerModel> BusinessPartners { get; set; } = new();
        public List<PartnerModel> MediaPartners { get; set; } = new();

        public ConferenceModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task OnGetAsync()
        {
            // Документите се качват от панела. Имената им бяха твърдо изписани
            // в разметката — смяна значеше качване по FTP с точно същото име.
            Documents = _context.Set<ConferenceApp.Models.DownloadableFile>()
                            .AsNoTracking()
                            .Where(f => f.FileKey.StartsWith("doc."))
                            .ToDictionary(f => f.FileKey);

            // 1. Взимаме всички членове и ги разделяме по тип
            var allMembers = await _context.CommitteeMembers.ToListAsync();
            OrganizingCommittee = allMembers.Where(m => m.CommitteeType == "Organizing Committee").ToList();
            ProgramCommittee = allMembers.Where(m => m.CommitteeType == "Program Committee").ToList();
            StudentCommittee = allMembers.Where(m => m.CommitteeType == "Student Committee").ToList(); // НОВО: Филтриране за Студентски комитет

            // 2. Взимаме всички партньори и ги разделяме по категория
            var allPartners = await _context.Partners.ToListAsync();
            InstitutionalPartners = allPartners.Where(p => p.Category == "Institutional").ToList();
            BusinessPartners = allPartners.Where(p => p.Category == "Business").ToList();
            MediaPartners = allPartners.Where(p => p.Category == "Media").ToList();
        }
    }
}