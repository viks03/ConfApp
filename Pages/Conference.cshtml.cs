// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Data;
using ConferenceApp.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Pages
{
    public class ConferenceModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        // One list per committee; the view renders a section for each.
        public List<CommitteeMemberModel> OrganizingCommittee { get; set; } = new();
        public List<CommitteeMemberModel> ProgramCommittee { get; set; } = new();
        public List<CommitteeMemberModel> StudentCommittee { get; set; } = new();

        /// <summary>The documents for authors, keyed by FileKey. Uploaded from
        /// the admin panel; a missing key means no button.</summary>
        public Dictionary<string, DownloadableFile> Documents { get; set; } = new();

        // One list per partner category.
        public List<PartnerModel> InstitutionalPartners { get; set; } = new();
        public List<PartnerModel> BusinessPartners { get; set; } = new();
        public List<PartnerModel> MediaPartners { get; set; } = new();

        public ConferenceModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task OnGetAsync()
        {
            // The documents are uploaded from the panel. Their names used to be
            // written into the markup, so replacing one meant an FTP upload
            // under exactly the same name.
            Documents = _context.Set<ConferenceApp.Models.DownloadableFile>()
                            .AsNoTracking()
                            .Where(f => f.FileKey.StartsWith("doc."))
                            .ToDictionary(f => f.FileKey);

            // One query, split in memory: the three lists together are the whole
            // table.
            var allMembers = await _context.CommitteeMembers.ToListAsync();
            OrganizingCommittee = allMembers.Where(m => m.CommitteeType == "Organizing Committee").ToList();
            ProgramCommittee = allMembers.Where(m => m.CommitteeType == "Program Committee").ToList();
            StudentCommittee = allMembers.Where(m => m.CommitteeType == "Student Committee").ToList();

            // The same for the partners.
            var allPartners = await _context.Partners.ToListAsync();
            InstitutionalPartners = allPartners.Where(p => p.Category == "Institutional").ToList();
            BusinessPartners = allPartners.Where(p => p.Category == "Business").ToList();
            MediaPartners = allPartners.Where(p => p.Category == "Media").ToList();
        }
    }
}