using Paperless.Models;

namespace Paperless.Models.ViewModels
{
    public class DashboardViewModel
    {
        public int TotalDocuments { get; set; }
        public int DraftDocuments { get; set; }
        public int PendingDocuments { get; set; }
        public int ApprovedDocuments { get; set; }
        public int RejectedDocuments { get; set; }
        public List<Document> RecentDocuments { get; set; } = new();
    }
}