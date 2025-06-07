using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DKMovies.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DKMovies.Controllers
{
    public class AdminEmployeeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminEmployeeController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: AdminEmployee
        public async Task<IActionResult> Index(string searchString, string sortOrder, string roleFilter, int page = 1)
        {
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentSort"] = sortOrder;
            ViewData["CurrentRole"] = roleFilter;

            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["DateSortParm"] = sortOrder == "Date" ? "date_desc" : "Date";
            ViewData["RoleSortParm"] = sortOrder == "Role" ? "role_desc" : "Role";
            ViewData["SalarySortParm"] = sortOrder == "Salary" ? "salary_desc" : "Salary";

            // ✅ SỬA: Include Role và Theater theo database schema
            var employees = from e in _context.Employees
                            .Include(e => e.Theater)
                            .Include(e => e.Role) // ✅ THÊM: Include Role
                            select e;

            // Search
            if (!String.IsNullOrEmpty(searchString))
            {
                employees = employees.Where(e => e.FullName.Contains(searchString)
                                              || e.Email.Contains(searchString)
                                              || e.Phone.Contains(searchString)
                                              || e.CitizenID.Contains(searchString)); // ✅ SỬA: Thêm CitizenID
            }

            // Filter by role
            if (!String.IsNullOrEmpty(roleFilter))
            {
                if (int.TryParse(roleFilter, out int roleId))
                {
                    employees = employees.Where(e => e.RoleID == roleId);
                }
            }

            // Sort
            switch (sortOrder)
            {
                case "name_desc":
                    employees = employees.OrderByDescending(e => e.FullName);
                    break;
                case "Date":
                    employees = employees.OrderBy(e => e.HireDate);
                    break;
                case "date_desc":
                    employees = employees.OrderByDescending(e => e.HireDate);
                    break;
                case "Role":
                    employees = employees.OrderBy(e => e.Role.Name); // ✅ SỬA: Sort by Role.Name
                    break;
                case "role_desc":
                    employees = employees.OrderByDescending(e => e.Role.Name);
                    break;
                case "Salary":
                    employees = employees.OrderBy(e => e.Salary);
                    break;
                case "salary_desc":
                    employees = employees.OrderByDescending(e => e.Salary);
                    break;
                default:
                    employees = employees.OrderBy(e => e.FullName);
                    break;
            }

            // Pagination
            int pageSize = 10;
            var totalEmployees = await employees.CountAsync();
            var employeeList = await employees
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalEmployees / pageSize);
            ViewBag.TotalEmployees = totalEmployees;

            // Statistics for ViewBag - ✅ SỬA: Bỏ IsActive (không có trong database)
            ViewBag.TotalEmployees = totalEmployees;
            ViewBag.NewEmployeesThisMonth = await _context.Employees
                .CountAsync(e => e.HireDate.Month == DateTime.Now.Month && e.HireDate.Year == DateTime.Now.Year);
            ViewBag.TotalSalaryExpense = await _context.Employees.SumAsync(e => e.Salary);

            return View(employeeList);
        }

        // GET: AdminEmployee/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var employee = await _context.Employees
                .Include(e => e.Theater)
                .Include(e => e.Role) // ✅ THÊM: Include Role
                .Include(e => e.Admins) // ✅ THÊM: Include Admins if exists
                .FirstOrDefaultAsync(e => e.ID == id);

            if (employee == null) return NotFound();

            return View(employee);
        }

        // GET: AdminEmployee/Create
        public async Task<IActionResult> Create()
        {
            ViewData["TheaterID"] = new SelectList(_context.Theaters, "ID", "Name");

            // ✅ SỬA: Load EmployeeRoles từ database
            ViewData["RoleID"] = new SelectList(_context.EmployeeRoles, "ID", "Name");

            return View();
        }

        // POST: AdminEmployee/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("FullName,Email,Phone,RoleID,Salary,HireDate,TheaterID,Address,DateOfBirth,Gender,CitizenID")] Employee employee)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    // ✅ SỬA: HireDate sẽ được set default trong database
                    if (employee.HireDate == default(DateTime))
                    {
                        employee.HireDate = DateTime.Now;
                    }

                    _context.Add(employee);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Đã thêm nhân viên thành công.";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Lỗi khi thêm nhân viên: {ex.Message}";
                }
            }

            ViewData["TheaterID"] = new SelectList(_context.Theaters, "ID", "Name", employee.TheaterID);
            ViewData["RoleID"] = new SelectList(_context.EmployeeRoles, "ID", "Name", employee.RoleID);

            return View(employee);
        }

        // GET: AdminEmployee/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var employee = await _context.Employees.FindAsync(id);
            if (employee == null) return NotFound();

            ViewData["TheaterID"] = new SelectList(_context.Theaters, "ID", "Name", employee.TheaterID);
            ViewData["RoleID"] = new SelectList(_context.EmployeeRoles, "ID", "Name", employee.RoleID);

            return View(employee);
        }

        // POST: AdminEmployee/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("ID,FullName,Email,Phone,RoleID,Salary,HireDate,TheaterID,Address,DateOfBirth,Gender,CitizenID,ProfileImagePath")] Employee employee)
        {
            if (id != employee.ID) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(employee);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Đã cập nhật thông tin nhân viên.";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!EmployeeExists(employee.ID))
                        return NotFound();
                    else
                        throw;
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Lỗi khi cập nhật: {ex.Message}";
                }
            }

            ViewData["TheaterID"] = new SelectList(_context.Theaters, "ID", "Name", employee.TheaterID);
            ViewData["RoleID"] = new SelectList(_context.EmployeeRoles, "ID", "Name", employee.RoleID);

            return View(employee);
        }

        // GET: AdminEmployee/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var employee = await _context.Employees
                .Include(e => e.Theater)
                .Include(e => e.Role)
                .FirstOrDefaultAsync(e => e.ID == id);

            if (employee == null) return NotFound();

            return View(employee);
        }

        // POST: AdminEmployee/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var employee = await _context.Employees.FindAsync(id);
                if (employee != null)
                {
                    _context.Employees.Remove(employee);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Đã xóa nhân viên.";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Lỗi khi xóa nhân viên: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // API: Get Employee Statistics
        [HttpGet]
        public async Task<JsonResult> GetEmployeeStatistics()
        {
            try
            {
                var totalEmployees = await _context.Employees.CountAsync();
                var newEmployeesThisMonth = await _context.Employees
                    .CountAsync(e => e.HireDate.Month == DateTime.Now.Month && e.HireDate.Year == DateTime.Now.Year);
                var totalSalaryExpense = await _context.Employees.SumAsync(e => e.Salary);

                // ✅ THÊM: Statistics by role
                var employeesByRole = await _context.Employees
                    .Include(e => e.Role)
                    .GroupBy(e => e.Role.Name)
                    .Select(g => new { Role = g.Key, Count = g.Count() })
                    .ToListAsync();

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        totalEmployees,
                        newEmployeesThisMonth,
                        totalSalaryExpense,
                        employeesByRole
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ✅ API: Get Employee Roles
        [HttpGet]
        public async Task<JsonResult> GetEmployeeRoles()
        {
            try
            {
                var roles = await _context.EmployeeRoles
                    .Select(r => new { r.ID, r.Name, r.Description })
                    .ToListAsync();

                return Json(new { success = true, data = roles });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ✅ API: Validate Employee Data
        [HttpPost]
        public async Task<JsonResult> ValidateEmployeeData([FromBody] ValidateEmployeeRequest request)
        {
            try
            {
                var errors = new List<string>();

                // Check email uniqueness
                if (await _context.Employees.AnyAsync(e => e.Email == request.Email && e.ID != request.EmployeeId))
                {
                    errors.Add("Email đã tồn tại trong hệ thống");
                }

                // Check citizen ID uniqueness
                if (!string.IsNullOrEmpty(request.CitizenID) &&
                    await _context.Employees.AnyAsync(e => e.CitizenID == request.CitizenID && e.ID != request.EmployeeId))
                {
                    errors.Add("Số CCCD/CMND đã tồn tại trong hệ thống");
                }

                return Json(new
                {
                    success = errors.Count == 0,
                    errors = errors
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private bool EmployeeExists(int id)
        {
            return _context.Employees.Any(e => e.ID == id);
        }

        public class ValidateEmployeeRequest
        {
            public int EmployeeId { get; set; }
            public string Email { get; set; }
            public string CitizenID { get; set; }
        }
    }
}