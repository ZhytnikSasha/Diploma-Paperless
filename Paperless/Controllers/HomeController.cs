using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paperless.Data;
using Paperless.Models;
using Paperless.Models.Enums;
using Paperless.Models.ViewModels;
using System.Diagnostics;

namespace Paperless.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly AppDbContext _context;

        public HomeController(ILogger<HomeController> logger, AppDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return View(new DashboardViewModel());
            }

            var model = new DashboardViewModel
            {
                TotalDocuments = await _context.Documents.CountAsync(),
                DraftDocuments = await _context.Documents.CountAsync(d => d.Status == DocumentStatus.Draft),
                PendingDocuments = await _context.Documents.CountAsync(d => d.Status == DocumentStatus.PendingReview),
                ApprovedDocuments = await _context.Documents.CountAsync(d => d.Status == DocumentStatus.Approved),
                RejectedDocuments = await _context.Documents.CountAsync(d => d.Status == DocumentStatus.Rejected),
                RecentDocuments = await _context.Documents
                    .Include(d => d.Author)
                    .OrderByDescending(d => d.CreatedAt)
                    .Take(5)
                    .ToListAsync()
            };

            return View(model);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}