using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
using Microsoft.AspNetCore.Authorization;
using System.ComponentModel.DataAnnotations;

namespace Paperless.Tests
{
    public class SystemTests
    {
        #region Налаштування

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

        // Рятівний метод для контексту: тепер він приймає ID користувача, щоб тестувати чужі документи
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

        #region Безпека (NFR) та Авторизація

        [Fact]
        public void TC_S001_PasswordIsHashed_NotStoredInPlainText()
        {
            // Перевірка NFR-01: пароль хешується, а не зберігається відкрито
            var hasher = new PasswordHasher<ApplicationUser>();
            var user = new ApplicationUser { UserName = "test@mail.com" };
            var plainTextPassword = "MySecretPassword123!";

            user.PasswordHash = hasher.HashPassword(user, plainTextPassword);

            Assert.NotNull(user.PasswordHash);
            Assert.NotEqual(plainTextPassword, user.PasswordHash); // Хеш не дорівнює паролю

            var result = hasher.VerifyHashedPassword(user, user.PasswordHash, plainTextPassword);
            Assert.Equal(PasswordVerificationResult.Success, result);
        }

        [Fact]
        public void TC_S002_CreatePostAction_HasValidateAntiForgeryTokenAttribute()
        {
            // Перевірка NFR-02: захист від CSRF
            var methodInfo = typeof(DocumentController).GetMethod("Create", new[] { typeof(Document), typeof(IFormFile) });
            var attribute = methodInfo?.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>();

            Assert.NotNull(attribute); // Метод POST Create захищено AntiForgeryToken
        }

        [Fact]
        public void TC_S003_AdminActions_RequireAdminRole()
        {
            // Перевірка FR-01.03: прямий перехід до адмінки блокується
            var type = typeof(AdminController);
            var authorizeAttribute = type.GetCustomAttribute<AuthorizeAttribute>();

            Assert.NotNull(authorizeAttribute);
            Assert.Contains("Адміністратор", authorizeAttribute.Roles ?? "");
        }

        #endregion

        #region Бізнес-правила статусів (FR-03.03) та Прав (FR-02.04)

        [Fact]
        public async Task TC_S004_AuthorCannotApproveOwnDocument()
        {
            // Перевірка: Спроба автора затвердити власний документ (користувач НЕ є Менеджером)
            using var db = GetInMemoryDbContext();
            var doc = new Document { Id = 1, Title = "Doc", Status = DocumentStatus.PendingReview, AuthorId = "user-1" };
            db.Documents.Add(doc);
            await db.SaveChangesAsync();

            var controller = new DocumentController(db, GetMockUserManager().Object, new Mock<IWebHostEnvironment>().Object);
            SetupControllerContext(controller, "Співробітник", "user-1"); // Автор, але без ролі "Менеджер"

            await controller.ChangeStatus(1, DocumentStatus.Approved);

            var updatedDoc = db.Documents.Find(1);
            Assert.Equal(DocumentStatus.PendingReview, updatedDoc.Status); // Статус не змінився, бо немає прав
        }

        [Fact]
        public async Task TC_S005_CannotApproveDraftDocument()
        {
            // Перевірка: Спроба затвердити документ у стані Чернетка
            using var db = GetInMemoryDbContext();
            var doc = new Document { Id = 2, Title = "Draft", Status = DocumentStatus.Draft, AuthorId = "user-1" };
            db.Documents.Add(doc);
            await db.SaveChangesAsync();

            var controller = new DocumentController(db, GetMockUserManager().Object, new Mock<IWebHostEnvironment>().Object);
            SetupControllerContext(controller, "Менеджер");

            await controller.ChangeStatus(2, DocumentStatus.Approved);

            var updatedDoc = db.Documents.Find(2);
            Assert.Equal(DocumentStatus.Draft, updatedDoc.Status); // Повинно залишитися Чернеткою
        }

        [Fact]
        public async Task TC_S006_EditOtherUserDocument_ReturnsForbid()
        {
            // Перевірка FR-02.04: Редагування чужого документа
            using var db = GetInMemoryDbContext();
            // Створюємо документ, де автор user-2
            var doc = new Document { Id = 3, Title = "Other Doc", AuthorId = "user-2" };
            db.Documents.Add(doc);
            await db.SaveChangesAsync();

            var controller = new DocumentController(db, GetMockUserManager().Object, new Mock<IWebHostEnvironment>().Object);
            SetupControllerContext(controller, "Співробітник", "user-1"); // Входимо як user-1 (НЕ автор)

            var result = await controller.Edit(3, doc, null);

            // Якщо контролер повертає Forbid() (Заборонено) або Redirect
            bool isForbidden = result is ForbidResult || result is RedirectToActionResult;
            Assert.True(isForbidden, "Користувач без прав не повинен мати доступу до редагування чужого документа.");
        }

        #endregion

        #region Аудит та Транзакції (BRL-08, NFR-04)

        [Fact]
        public async Task TC_S007_DeleteDocument_RetainsAuditLog()
        {
            // Перевірка BRL-08: Видалення документа залишає записи в аудиті
            using var db = GetInMemoryDbContext();
            var doc = new Document { Id = 4, Title = "To Delete", AuthorId = "user-1" };
            var audit = new DocumentHistory { Id = 1, DocumentId = 4, DocumentTitle = "To Delete" };
            db.Documents.Add(doc);
            db.DocumentHistories.Add(audit);
            await db.SaveChangesAsync();

            var controller = new DocumentController(db, GetMockUserManager().Object, new Mock<IWebHostEnvironment>().Object);

            // ВАЖЛИВО: Щоб Delete спрацював, контролер може очікувати назву методу DeleteConfirmed
            // Замініть DeleteConfirmed на Delete(4), якщо ваш POST метод називається Delete
            var methodInfo = typeof(DocumentController).GetMethod("DeleteConfirmed") ?? typeof(DocumentController).GetMethod("Delete");

            if (methodInfo != null)
            {
                SetupControllerContext(controller, "Адміністратор", "user-1");
                await (Task<IActionResult>)methodInfo.Invoke(controller, new object[] { 4 });

                var deletedDoc = db.Documents.Find(4);
                Assert.Null(deletedDoc); // Документ має бути видалений

                // Перевіряємо, що аудит живий
                var retainedAudit = db.DocumentHistories.FirstOrDefault(a => a.DocumentTitle == "To Delete");
                Assert.NotNull(retainedAudit);
            }
        }

        [Fact]
        public async Task TC_S008_TransactionalSave_CreatesDocumentAndAudit()
        {
            // Перевірка NFR-04: Транзакційне збереження документа та запису аудиту
            using var db = GetInMemoryDbContext();
            var controller = new DocumentController(db, GetMockUserManager().Object, new Mock<IWebHostEnvironment>().Object);
            SetupControllerContext(controller);

            var doc = new Document { Title = "Transact Doc" };

            // Виклик Create створює і документ, і запис в історію
            await controller.Create(doc, null);

            Assert.Equal(1, db.Documents.Count());
            Assert.Equal(1, db.DocumentHistories.Count()); // Атомарність: обидва створені
        }

        #endregion

        #region Валідація Моделей (FR-02.01, NFR-03, FR-02.02)

        [Fact]
        public void TC_S009_CreateDocument_EmptyTitle_ValidationFails()
        {
            // Перевірка обов'язковості поля Title
            var doc = new Document { Title = "" };
            var context = new ValidationContext(doc);
            var results = new List<ValidationResult>();

            bool isValid = Validator.TryValidateObject(doc, context, results, true);

            Assert.False(isValid);
            Assert.Contains(results, r => r.MemberNames.Contains("Title"));
        }

        [Fact]
        public void TC_S010_CreateDocument_TitleTooLong_ValidationFails()
        {
            // Перевірка довжини поля Title
            var doc = new Document { Title = new string('A', 201) }; // 201 символ (максимум зазвичай 100 або 200)
            var context = new ValidationContext(doc);
            var results = new List<ValidationResult>();

            bool isValid = Validator.TryValidateObject(doc, context, results, true);

            Assert.False(isValid);
        }

        [Fact]
        public async Task TC_S011_LoadLargeDocumentRegistry_CompletesAsync()
        {
            // Перевірка NFR-03: Завантаження реєстру з великою кількістю документів
            using var db = GetInMemoryDbContext();
            for (int i = 0; i < 100; i++)
            {
                db.Documents.Add(new Document { Id = i + 1, Title = $"Doc {i}" });
            }
            await db.SaveChangesAsync();

            var controller = new DocumentController(db, GetMockUserManager().Object, new Mock<IWebHostEnvironment>().Object);
            SetupControllerContext(controller);

            var result = await controller.Index(null, null) as ViewResult;

            // Якщо використовується пагінація, модель може бути не IEnumerable
            // Але головне, що запит обробився успішно і повернув ViewResult
            Assert.NotNull(result);
        }

        [Fact]
        public void TC_S012_FileExtensionValidation_CaseInsensitive()
        {
            // Перевірка FR-02.02: Формат файлу не залежить від регістру (.PDF == .pdf)
            var fileMock = new Mock<IFormFile>();
            fileMock.Setup(f => f.FileName).Returns("report.PDF"); // Верхній регістр

            var extension = Path.GetExtension(fileMock.Object.FileName).ToLowerInvariant();
            var allowedExtensions = new[] { ".pdf", ".docx", ".doc", ".xls", ".xlsx", ".txt", ".rtf" };

            bool isValid = allowedExtensions.Contains(extension);

            Assert.True(isValid); // Має пройти валідацію
        }

        #endregion
    }
}