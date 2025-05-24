using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DKMovies.Models;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Linq;
using System;

namespace DKMovies.Controllers
{
    public class ReviewsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ReviewsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // POST: /Reviews/LeaveReview
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LeaveReview([Bind("MovieID,Rating,Comment")] Review review)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out int userId))
                return Forbid();

            // Kiểm tra xem người dùng đã đánh giá phim này chưa
            var hasReviewed = await _context.Reviews
                .AnyAsync(r => r.MovieID == review.MovieID && r.UserID == userId);

            if (hasReviewed)
            {
                TempData["ToastError"] = "Bạn đã đánh giá phim này rồi.";
                return RedirectToAction("Details", "MoviesList", new { id = review.MovieID });
            }

            if (review.Rating < 1 || review.Rating > 5 || string.IsNullOrWhiteSpace(review.Comment))
            {
                TempData["ToastError"] = "Vui lòng nhập đầy đủ và hợp lệ.";
                return RedirectToAction("Details", "MoviesList", new { id = review.MovieID });
            }

            review.UserID = userId;
            review.CreatedAt = DateTime.Now;
            review.IsApproved = true; // Đặt false nếu cần duyệt thủ công

            _context.Reviews.Add(review);
            await _context.SaveChangesAsync();

            TempData["ToastSuccess"] = "Cảm ơn bạn đã đánh giá!";
            return RedirectToAction("Details", "MoviesList", new { id = review.MovieID });
        }

        // (Optional) Chức năng duyệt review (chỉ gọi từ admin)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleApproval(int id, bool isApproved)
        {
            var review = await _context.Reviews.FindAsync(id);
            if (review == null)
                return NotFound();

            review.IsApproved = isApproved;
            await _context.SaveChangesAsync();

            TempData["ToastSuccess"] = "Đã cập nhật trạng thái duyệt.";
            return RedirectToAction("Index", "AdminReviews");
        }

        // ❌ Ẩn các chức năng CRUD cho admin
        // Index, Create, Edit, Delete → sẽ dùng riêng tại AdminReviewController
    }
}
