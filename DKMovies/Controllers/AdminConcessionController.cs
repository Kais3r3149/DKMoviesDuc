using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DKMovies.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DKMovies.Controllers
{
    public class AdminConcessionController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminConcessionController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: AdminConcession
        public async Task<IActionResult> Index(string searchString, string sortOrder, string category, string status, int page = 1)
        {
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentSort"] = sortOrder;
            ViewData["CurrentCategory"] = category;
            ViewData["CurrentStatus"] = status;

            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["CategorySortParm"] = sortOrder == "Category" ? "category_desc" : "Category";

            var concessions = from c in _context.Concessions
                              .Include(c => c.TheaterConcessions)
                              select c;

            // Search
            if (!String.IsNullOrEmpty(searchString))
            {
                concessions = concessions.Where(c => c.Name.Contains(searchString)
                                                  || (c.Description != null && c.Description.Contains(searchString))
                                                  || (c.Category != null && c.Category.Contains(searchString)));
            }

            // Filter by category
            if (!String.IsNullOrEmpty(category))
            {
                concessions = concessions.Where(c => c.Category == category);
            }

            // Filter by status
            if (!String.IsNullOrEmpty(status))
            {
                if (status == "active")
                    concessions = concessions.Where(c => c.IsActive);
                else if (status == "inactive")
                    concessions = concessions.Where(c => !c.IsActive);
                else if (status == "available")
                    concessions = concessions.Where(c => c.TheaterConcessions.Any(tc => tc.IsAvailable));
                else if (status == "low_stock")
                    concessions = concessions.Where(c => c.TheaterConcessions.Any(tc => tc.StockLeft <= 10));
            }

            // Sort
            switch (sortOrder)
            {
                case "name_desc":
                    concessions = concessions.OrderByDescending(c => c.Name);
                    break;
                case "Category":
                    concessions = concessions.OrderBy(c => c.Category);
                    break;
                case "category_desc":
                    concessions = concessions.OrderByDescending(c => c.Category);
                    break;
                default:
                    concessions = concessions.OrderBy(c => c.Name);
                    break;
            }

            // Pagination
            int pageSize = 12;
            var totalConcessions = await concessions.CountAsync();
            var concessionList = await concessions
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalConcessions / pageSize);
            ViewBag.TotalConcessions = totalConcessions;

            // Statistics
            ViewBag.ActiveConcessions = await _context.Concessions.CountAsync(c => c.IsActive);
            ViewBag.AvailableConcessions = await _context.TheaterConcessions.CountAsync(tc => tc.IsAvailable);
            ViewBag.LowStockItems = await _context.TheaterConcessions.CountAsync(tc => tc.StockLeft <= 10);

            return View(concessionList);
        }

        // GET: AdminConcession/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var concession = await _context.Concessions
                .Include(c => c.TheaterConcessions)
                    .ThenInclude(tc => tc.Theater)
                .FirstOrDefaultAsync(c => c.ID == id);

            if (concession == null) return NotFound();

            return View(concession);
        }

        // GET: AdminConcession/Create
        public IActionResult Create()
        {
            ViewData["Categories"] = new SelectList(new[]
            {
                new { Value = "Food", Text = "Đồ ăn" },
                new { Value = "Drink", Text = "Đồ uống" },
                new { Value = "Combo", Text = "Combo" },
                new { Value = "Snack", Text = "Snack" },
                new { Value = "Dessert", Text = "Tráng miệng" },
                new { Value = "Other", Text = "Khác" }
            }, "Value", "Text");

            return View();
        }

        // POST: AdminConcession/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Name,Description,Category,IsActive,ImagePath")] Concession concession, IFormFile imageFile)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    // Handle image upload
                    if (imageFile != null && imageFile.Length > 0)
                    {
                        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets", "images", "concessions");
                        Directory.CreateDirectory(uploadsFolder);

                        var uniqueFileName = Guid.NewGuid().ToString() + "_" + imageFile.FileName;
                        var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await imageFile.CopyToAsync(fileStream);
                        }

                        concession.ImagePath = "/assets/images/concessions/" + uniqueFileName;
                    }

                    _context.Add(concession);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Đã thêm sản phẩm thành công.";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Lỗi khi thêm sản phẩm: {ex.Message}";
                }
            }

            ViewData["Categories"] = new SelectList(new[]
            {
                new { Value = "Food", Text = "Đồ ăn" },
                new { Value = "Drink", Text = "Đồ uống" },
                new { Value = "Combo", Text = "Combo" },
                new { Value = "Snack", Text = "Snack" },
                new { Value = "Dessert", Text = "Tráng miệng" },
                new { Value = "Other", Text = "Khác" }
            }, "Value", "Text", concession.Category);

            return View(concession);
        }

        // GET: AdminConcession/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var concession = await _context.Concessions.FindAsync(id);
            if (concession == null) return NotFound();

            ViewData["Categories"] = new SelectList(new[]
            {
                new { Value = "Food", Text = "Đồ ăn" },
                new { Value = "Drink", Text = "Đồ uống" },
                new { Value = "Combo", Text = "Combo" },
                new { Value = "Snack", Text = "Snack" },
                new { Value = "Dessert", Text = "Tráng miệng" },
                new { Value = "Other", Text = "Khác" }
            }, "Value", "Text", concession.Category);

            return View(concession);
        }

        // POST: AdminConcession/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("ID,Name,Description,Category,IsActive,ImagePath")] Concession concession, IFormFile imageFile)
        {
            if (id != concession.ID) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    // Handle new image upload
                    if (imageFile != null && imageFile.Length > 0)
                    {
                        // Delete old image if exists
                        if (!string.IsNullOrEmpty(concession.ImagePath))
                        {
                            var oldImagePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot" + concession.ImagePath);
                            if (System.IO.File.Exists(oldImagePath))
                            {
                                System.IO.File.Delete(oldImagePath);
                            }
                        }

                        // Upload new image
                        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets", "images", "concessions");
                        Directory.CreateDirectory(uploadsFolder);

                        var uniqueFileName = Guid.NewGuid().ToString() + "_" + imageFile.FileName;
                        var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await imageFile.CopyToAsync(fileStream);
                        }

                        concession.ImagePath = "/assets/images/concessions/" + uniqueFileName;
                    }

                    _context.Update(concession);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Đã cập nhật sản phẩm.";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ConcessionExists(concession.ID))
                        return NotFound();
                    else
                        throw;
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Lỗi khi cập nhật: {ex.Message}";
                }
            }

            ViewData["Categories"] = new SelectList(new[]
            {
                new { Value = "Food", Text = "Đồ ăn" },
                new { Value = "Drink", Text = "Đồ uống" },
                new { Value = "Combo", Text = "Combo" },
                new { Value = "Snack", Text = "Snack" },
                new { Value = "Dessert", Text = "Tráng miệng" },
                new { Value = "Other", Text = "Khác" }
            }, "Value", "Text", concession.Category);

            return View(concession);
        }

        // GET: AdminConcession/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var concession = await _context.Concessions
                .Include(c => c.TheaterConcessions)
                    .ThenInclude(tc => tc.Theater)
                .FirstOrDefaultAsync(c => c.ID == id);

            if (concession == null) return NotFound();

            return View(concession);
        }

        // POST: AdminConcession/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var concession = await _context.Concessions.FindAsync(id);
                if (concession != null)
                {
                    // Delete associated image
                    if (!string.IsNullOrEmpty(concession.ImagePath))
                    {
                        var imagePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot" + concession.ImagePath);
                        if (System.IO.File.Exists(imagePath))
                        {
                            System.IO.File.Delete(imagePath);
                        }
                    }

                    _context.Concessions.Remove(concession);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Đã xóa sản phẩm.";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Lỗi khi xóa sản phẩm: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // API: Toggle Active Status
        [HttpPost]
        public async Task<JsonResult> ToggleActiveStatus([FromBody] ToggleActiveStatusRequest request)
        {
            try
            {
                var concession = await _context.Concessions.FindAsync(request.ConcessionId);
                if (concession == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy sản phẩm" });
                }

                concession.IsActive = !concession.IsActive;
                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    newStatus = concession.IsActive,
                    message = concession.IsActive ? "Đã kích hoạt sản phẩm" : "Đã vô hiệu hóa sản phẩm"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private bool ConcessionExists(int id)
        {
            return _context.Concessions.Any(c => c.ID == id);
        }

        public class ToggleActiveStatusRequest
        {
            public int ConcessionId { get; set; }
        }
    }
}