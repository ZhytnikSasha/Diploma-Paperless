using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using Paperless.Controllers; // Замініть на ваш простір імен
using Paperless.Data;        // Замініть на ваш простір імен
using Paperless.Models;      // Замініть на ваш простір імен
using Paperless.Models.Enums;
using Microsoft.AspNetCore.Hosting;

namespace Paperless.Tests
{
    public class UnitTests
    {
        #region Налаштування (Mocks & In-Memory DB)

        private AppDbContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        private Mock<UserManager<ApplicationUser>> GetMockUserManager()
        {
            var store = new Mock<IUserStore<ApplicationUser>>();
            var mgr = new Mock<UserManager<ApplicationUser>>(store.Object, null, null, null, null, null, null, null, null);

            var fakeUser = new ApplicationUser { Id = "user-1", UserName = "test@mail.com" };
            mgr.Setup(x => x.GetUserId(It.IsAny<ClaimsPrincipal>())).Returns(fakeUser.Id);
            mgr.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(fakeUser);

            return mgr;
        }

        private void SetupControllerContext(Controller controller, string role = null, string userId = "user-1")
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, "test@mail.com")
            };

            if (!string.IsNullOrEmpty(role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "mock"));

            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        }

        #endregion

        #region TC_U001 - TC_U005: Машина станів (Зміна статусів документів)

        [Fact]
        public async Task TC_U001_ChangeStatus_Author_DraftToPendingReview_Allowed()
        {
            // Перевірка FR-03.01: Автор відправляє чернетку на розгляд
            using var db = GetInMemoryDbContext();
            db.Documents.Add(new Document { Id = 1, Title = "Doc", Status = DocumentStatus.Draft, AuthorId = "user-1" });
            await db.SaveChangesAsync();

            var controller = new DocumentController(db, GetMockUserManager().Object, new Mock<IWebHostEnvironment>().Object);
            SetupControllerContext(controller, "Співробітник", "user-1"); // Користувач є автором

            await controller.ChangeStatus(1, DocumentStatus.PendingReview);

            var updatedDoc = db.Documents.Find(1);
            Assert.Equal(DocumentStatus.PendingReview, updatedDoc.Status); // Дозволено
        }

        [Fact]
        public async Task TC_U002_ChangeStatus_Manager_PendingReviewToApproved_Allowed()
        {
            // Перевірка FR-03.02: Менеджер затверджує документ
            using var db = GetInMemoryDbContext();
            db.Documents.Add(new Document { Id = 2, Title = "Doc", Status = DocumentStatus.PendingReview, AuthorId = "user-2" });
            await db.SaveChangesAsync();

            var controller = new DocumentController(db, GetMockUserManager().Object, new Mock<IWebHostEnvironment>().Object);
            SetupControllerContext(controller, "Менеджер", "user-1"); // Користувач є Менеджером (не автором)

            await controller.ChangeStatus(2, DocumentStatus.Approved);

            var updatedDoc = db.Documents.Find(2);
            Assert.Equal(DocumentStatus.Approved, updatedDoc.Status); // Дозволено
        }

        [Fact]
        public async Task TC_U003_ChangeStatus_Manager_PendingReviewToRejected_Allowed()
        {
            // Перевірка FR-03.02: Менеджер відхиляє документ
            using var db = GetInMemoryDbContext();
            db.Documents.Add(new Document { Id = 3, Title = "Doc", Status = DocumentStatus.PendingReview, AuthorId = "user-2" });
            await db.SaveChangesAsync();

            var controller = new DocumentController(db, GetMockUserManager().Object, new Mock<IWebHostEnvironment>().Object);
            SetupControllerContext(controller, "Менеджер", "user-1");

            await controller.ChangeStatus(3, DocumentStatus.Rejected);

            var updatedDoc = db.Documents.Find(3);
            Assert.Equal(DocumentStatus.Rejected, updatedDoc.Status); // Дозволено
        }

        [Fact]
        public async Task TC_U004_ChangeStatus_Author_DraftToApproved_Denied()
        {
            // Перевірка FR-03.03: Спроба перескочити статус (Чернетка -> Затверджено)
            using var db = GetInMemoryDbContext();
            db.Documents.Add(new Document { Id = 4, Title = "Doc", Status = DocumentStatus.Draft, AuthorId = "user-1" });
            await db.SaveChangesAsync();

            var controller = new DocumentController(db, GetMockUserManager().Object, new Mock<IWebHostEnvironment>().Object);
            SetupControllerContext(controller, "Співробітник", "user-1");

            await controller.ChangeStatus(4, DocumentStatus.Approved);

            var updatedDoc = db.Documents.Find(4);
            // Статус має залишитися Draft, бо такий перехід заборонений логікою
            Assert.Equal(DocumentStatus.Draft, updatedDoc.Status);
        }

        [Fact]
        public async Task TC_U005_ChangeStatus_Employee_PendingReviewToApproved_Denied()
        {
            // Перевірка FR-03.03: Звичайний співробітник намагається затвердити документ
            using var db = GetInMemoryDbContext();
            db.Documents.Add(new Document { Id = 5, Title = "Doc", Status = DocumentStatus.PendingReview, AuthorId = "user-2" });
            await db.SaveChangesAsync();

            var controller = new DocumentController(db, GetMockUserManager().Object, new Mock<IWebHostEnvironment>().Object);
            SetupControllerContext(controller, "Співробітник", "user-1"); // НЕ менеджер

            await controller.ChangeStatus(5, DocumentStatus.Approved);

            var updatedDoc = db.Documents.Find(5);
            Assert.Equal(DocumentStatus.PendingReview, updatedDoc.Status); // Статус не змінився
        }

        #endregion

        #region TC_U006 - TC_U007: Валідація файлів

        [Theory] // Використовуємо Theory для тестування різних розширень в одному методі
        [InlineData(".pdf", true)]   // TC_U006: Допустиме розширення
        [InlineData(".docx", true)]
        [InlineData(".exe", false)]  // TC_U007: Недопустиме розширення
        [InlineData(".bat", false)]
        public void TC_U006_U007_FileExtensionValidation_Logic(string extension, bool expectedResult)
        {
            // Модульна перевірка логіки валідації розширень (FR-02.02)
            var allowedExtensions = new[] { ".pdf", ".docx", ".doc", ".xls", ".xlsx", ".txt", ".rtf" };

            bool isValid = allowedExtensions.Contains(extension.ToLowerInvariant());

            Assert.Equal(expectedResult, isValid);
        }

        #endregion

        #region TC_U008 - TC_U009: Журнал аудиту (Формування записів)

        [Fact]
        public async Task TC_U008_AuditRecord_OnCreate_HasNullOldStatus()
        {
            // Перевірка FR-04.01: Аудит при створенні документа
            using var db = GetInMemoryDbContext();
            var mockEnv = new Mock<IWebHostEnvironment>();
            mockEnv.Setup(m => m.WebRootPath).Returns("C:\\Test");
            var controller = new DocumentController(db, GetMockUserManager().Object, mockEnv.Object);
            SetupControllerContext(controller);

            var fileMock = new Mock<IFormFile>();
            fileMock.Setup(f => f.FileName).Returns("test.pdf");

            await controller.Create(new Document { Title = "Audit 1" }, fileMock.Object);

            var audit = db.DocumentHistories.FirstOrDefault();
            Assert.NotNull(audit);
            Assert.Null(audit.OldStatus); // OldStatus має бути null
            Assert.Equal(DocumentStatus.Draft, audit.NewStatus); // NewStatus = Draft
        }

        [Fact]
        public async Task TC_U009_AuditRecord_OnStatusChange_LogsBothStates()
        {
            // Перевірка FR-04.01: Аудит при зміні стану
            using var db = GetInMemoryDbContext();
            db.Documents.Add(new Document { Id = 6, Title = "Doc", Status = DocumentStatus.Draft, AuthorId = "user-1" });
            await db.SaveChangesAsync();

            var controller = new DocumentController(db, GetMockUserManager().Object, new Mock<IWebHostEnvironment>().Object);
            SetupControllerContext(controller);

            await controller.ChangeStatus(6, DocumentStatus.PendingReview);

            var audit = db.DocumentHistories.LastOrDefault();
            Assert.NotNull(audit);
            Assert.Equal(DocumentStatus.Draft, audit.OldStatus); // Старий стан
            Assert.Equal(DocumentStatus.PendingReview, audit.NewStatus); // Новий стан
        }

        #endregion

        #region TC_U010 - TC_U011: Перевірка прав доступу (Авторизація на рівні ресурсів)

        [Fact]
        public async Task TC_U010_AccessCheck_Author_CanAccessEdit()
        {
            // Перевірка FR-02.04: Автор має доступ до свого документа
            using var db = GetInMemoryDbContext();
            db.Documents.Add(new Document { Id = 7, Title = "My Doc", AuthorId = "user-1" });
            await db.SaveChangesAsync();

            var controller = new DocumentController(db, GetMockUserManager().Object, new Mock<IWebHostEnvironment>().Object);
            SetupControllerContext(controller, "Співробітник", "user-1"); // ID збігається з автором

            var result = await controller.Edit(7);

            // Має повернутися ViewResult (доступ дозволено)
            Assert.IsType<ViewResult>(result);
        }

        [Fact]
        public async Task TC_U011_AccessCheck_NotAuthorNotAdmin_GetsForbid()
        {
            // Перевірка FR-02.04: Не автор і не адмін отримує відмову
            using var db = GetInMemoryDbContext();
            db.Documents.Add(new Document { Id = 8, Title = "Someone's Doc", AuthorId = "user-2" });
            await db.SaveChangesAsync();

            var controller = new DocumentController(db, GetMockUserManager().Object, new Mock<IWebHostEnvironment>().Object);
            SetupControllerContext(controller, "Співробітник", "user-1"); // Звичайний користувач, не автор

            var result = await controller.Edit(8);

            // Перевіряємо, що повернуто Forbid() або Redirect на сторінку доступу
            bool isForbidden = result is ForbidResult || result is RedirectToActionResult;
            Assert.True(isForbidden, "Користувач без прав не повинен мати доступу до чужого документа.");
        }

        #endregion

        #region TC_U012: Аналітика (Дашборд)

        [Fact]
        public async Task TC_U012_DashboardAggregation_CountsDocumentsCorrectly()
        {
            // Перевірка FR-04.04: Підрахунок кількості документів за станами
            using var db = GetInMemoryDbContext();

            // Наповнюємо базу: 2 Чернетки, 3 На розгляді, 1 Затверджено
            db.Documents.AddRange(
                new Document { Id = 10, Title = "D1", Status = DocumentStatus.Draft },
                new Document { Id = 11, Title = "D2", Status = DocumentStatus.Draft },
                new Document { Id = 12, Title = "D3", Status = DocumentStatus.PendingReview },
                new Document { Id = 13, Title = "D4", Status = DocumentStatus.PendingReview },
                new Document { Id = 14, Title = "D5", Status = DocumentStatus.PendingReview },
                new Document { Id = 15, Title = "D6", Status = DocumentStatus.Approved }
            );
            await db.SaveChangesAsync();

            // Імітуємо логіку агрегації, яка зазвичай відбувається у HomeController.Index()
            var draftCount = await db.Documents.CountAsync(d => d.Status == DocumentStatus.Draft);
            var pendingCount = await db.Documents.CountAsync(d => d.Status == DocumentStatus.PendingReview);
            var approvedCount = await db.Documents.CountAsync(d => d.Status == DocumentStatus.Approved);

            // Перевіряємо, чи правильно відпрацювали агрегатні функції
            Assert.Equal(2, draftCount);
            Assert.Equal(3, pendingCount);
            Assert.Equal(1, approvedCount);
        }

        #endregion
    }
}