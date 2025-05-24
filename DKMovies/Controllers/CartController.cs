using DKMovies.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

[Authorize]
public class CartController : Controller
{
    private readonly ApplicationDbContext _context;

    public CartController(ApplicationDbContext context)
    {
        _context = context;
    }

    // Xem giỏ hàng
    public async Task<IActionResult> Index()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out int userId)) return Forbid();

        var cartItems = await _context.CartItems
            .Include(ci => ci.TheaterConcession)
                .ThenInclude(tc => tc.Concession)
            .Include(ci => ci.TheaterConcession.Theater)
            .Where(ci => ci.UserID == userId)
            .ToListAsync();

        return View(cartItems);
    }

    [HttpGet]
    public async Task<IActionResult> OrderConfirmation(int id)
    {
        var sale = await _context.Sales
            .Include(s => s.SaleDetails)
                .ThenInclude(d => d.TheaterConcession)
                    .ThenInclude(tc => tc.Concession)
            .Include(s => s.SaleDetails)
                .ThenInclude(d => d.TheaterConcession.Theater)
            .FirstOrDefaultAsync(s => s.ID == id);

        if (sale == null) return NotFound();
        return View(sale); // View ở Views/Cart/OrderConfirmation.cshtml
    }



    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmOrder()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out int userId)) return Forbid();

        var cartItems = await _context.CartItems
            .Include(c => c.TheaterConcession)
            .Where(c => c.UserID == userId)
            .ToListAsync();

        if (!cartItems.Any())
        {
            TempData["ToastError"] = "Giỏ hàng của bạn đang trống.";
            return RedirectToAction("Index");
        }

        // 1. Tạo đơn hàng mới
        var sale = new Sale
        {
            UserID = userId,
            CreatedAt = DateTime.Now,
            Status = "Đang xử lý"
        };
        _context.Sales.Add(sale);
        await _context.SaveChangesAsync();

        // 2. Tạo chi tiết đơn hàng và trừ kho
        foreach (var item in cartItems)
        {
            if (item.Quantity > item.TheaterConcession.StockLeft)
            {
                TempData["ToastError"] = $"Không đủ hàng cho {item.TheaterConcession.Concession.Name}.";
                return RedirectToAction("Index");
            }

            var detail = new SaleDetail
            {
                SaleID = sale.ID,
                TheaterConcessionID = item.TheaterConcessionID,
                Quantity = item.Quantity,
                UnitPrice = item.TheaterConcession.Price
            };
            _context.SaleDetails.Add(detail);

            // Trừ kho
            item.TheaterConcession.StockLeft -= item.Quantity;
            _context.TheaterConcession.Update(item.TheaterConcession);
        }

        // 3. Xóa giỏ hàng
        _context.CartItems.RemoveRange(cartItems);

        await _context.SaveChangesAsync();
        TempData["ToastSuccess"] = "Đơn hàng đã được tạo thành công!";
        return RedirectToAction("OrderConfirmation", new { id = sale.ID });
    }


    [HttpPost]
    public async Task<IActionResult> UpdateQuantity(int id, int quantity)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out int userId)) return Json(new { success = false });

        var item = await _context.CartItems
            .Include(c => c.TheaterConcession)
            .FirstOrDefaultAsync(c => c.ID == id && c.UserID == userId);

        if (item == null || quantity < 1 || quantity > item.TheaterConcession.StockLeft)
            return Json(new { success = false });

        item.Quantity = quantity;
        await _context.SaveChangesAsync();

        return Json(new
        {
            success = true,
            newSubtotal = (item.Quantity * item.TheaterConcession.Price).ToString("n0"),
            newTotal = (await _context.CartItems
                .Where(c => c.UserID == userId)
                .SumAsync(c => c.Quantity * c.TheaterConcession.Price)).ToString("n0")
        });
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(int id)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out int userId)) return Forbid();

        var item = await _context.CartItems.FirstOrDefaultAsync(c => c.ID == id && c.UserID == userId);
        if (item != null)
        {
            _context.CartItems.Remove(item);
            await _context.SaveChangesAsync();
            TempData["ToastSuccess"] = "Đã xoá món khỏi giỏ hàng.";
        }

        return RedirectToAction("Index");
    }


    // Thêm món vào giỏ hàng
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddConcession(int theaterConcessionId, int quantity)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out int userId)) return Forbid();

        // Kiểm tra người dùng có vé đã xác nhận
        var hasTicket = await _context.Tickets
            .AnyAsync(t => t.UserID == userId && t.Status == TicketStatus.CONFIRMED);

        if (!hasTicket)
        {
            TempData["ToastError"] = "Bạn cần có vé đã xác nhận để đặt món ăn.";
            return RedirectToAction("Menu", "Concessions");
        }

        // Kiểm tra món ăn tại rạp có khả dụng không
        var item = await _context.TheaterConcession
            .Include(tc => tc.Concession)
            .Include(tc => tc.Theater)
            .FirstOrDefaultAsync(tc => tc.ID == theaterConcessionId && tc.IsAvailable && tc.StockLeft >= quantity);

        if (item == null)
        {
            TempData["ToastError"] = "Món ăn không khả dụng hoặc đã hết hàng.";
            return RedirectToAction("Menu", "Concessions");
        }

        // Kiểm tra xem đã có trong giỏ chưa
        var existingItem = await _context.CartItems
            .FirstOrDefaultAsync(c => c.UserID == userId && c.TheaterConcessionID == theaterConcessionId);

        if (existingItem != null)
        {
            existingItem.Quantity += quantity;
            _context.CartItems.Update(existingItem);
        }
        else
        {
            var newItem = new CartItem
            {
                UserID = userId,
                TheaterConcessionID = theaterConcessionId,
                Quantity = quantity
            };
            _context.CartItems.Add(newItem);
        }

        await _context.SaveChangesAsync();

        TempData["ToastSuccess"] = $"Đã thêm {item.Concession.Name} x{quantity} vào giỏ hàng!";
        return RedirectToAction("Index");
    }
}
