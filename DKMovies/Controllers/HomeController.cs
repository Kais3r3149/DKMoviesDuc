// Controller: HomeController.cs
using Microsoft.AspNetCore.Mvc;
using DKMovies.Models;
using Microsoft.EntityFrameworkCore;
using System; // Thêm System để dùng Random và Path
using System.IO; // Thêm System.IO để dùng Path.GetFileName
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

namespace DKMovies.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Home/Index
        public async Task<IActionResult> Index()
        {
            var dates = await _context.ShowTimes
                .Select(s => s.StartTime.Date)
                .Distinct()
                .ToListAsync();

            if (!dates.Any())
            {
                ViewBag.HotMovies = new List<Movie>();
                return View(new List<Movie>());
            }

            var random = new Random();
            var randomDate = dates[random.Next(dates.Count)];

            var movieIds = await _context.ShowTimes
                .Where(s => s.StartTime.Date == randomDate)
                .Select(s => s.MovieID)
                .Distinct()
                .ToListAsync();

            var movies = await _context.Movies
                .Where(m => movieIds.Contains(m.ID))
                .ToListAsync();


            // Xử lý PosterImagePath cho view Index
            // Giả định view Index.cshtml dùng <img src="~/@movie.PosterImagePath" />
            // và PosterImagePath trong DB chỉ là tên file.
            // Nếu PosterImagePath trong DB là "ten_file.jpg", nó sẽ thành "assets/images/movie_posters/ten_file.jpg"
            foreach (var movie in movies)
            {
                if (!string.IsNullOrEmpty(movie.PosterImagePath) && !movie.PosterImagePath.StartsWith("assets/images/movie_posters/"))
                {
                    movie.PosterImagePath = "assets/images/movie_posters/" + Path.GetFileName(movie.PosterImagePath);
                }
                else if (string.IsNullOrEmpty(movie.PosterImagePath))
                {
                    movie.PosterImagePath = "assets/images/movie_posters/default.jpg";
                }
            }

            ViewBag.RandomShowDate = randomDate; // giữ nguyên kiểu DateTime


            var hotMovies = await _context.Tickets
                .Include(t => t.ShowTime)
                    .ThenInclude(s => s.Movie)
                .GroupBy(t => t.ShowTime.Movie)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .Take(5)
                .ToListAsync();

            foreach (var movie in hotMovies)
            {
                if (string.IsNullOrEmpty(movie.PosterImagePath))
                {
                    movie.PosterImagePath = "assets/images/movie_posters/default.jpg";
                }
                // Đảm bảo rằng nếu PosterImagePath đã có tiền tố thì không thêm nữa,
                // và nếu chưa có thì thêm và chỉ lấy tên file.
                else if (!movie.PosterImagePath.StartsWith("assets/images/movie_posters/"))
                {
                    movie.PosterImagePath = "assets/images/movie_posters/" + Path.GetFileName(movie.PosterImagePath);
                }
            }

            ViewBag.HotMovies = hotMovies;

            // LẤY CÁC MÓN ĂN NỔI BẬT
            var topConcessions = await _context.Concessions
                .Include(c => c.TheaterConcessions)
                .Where(c => c.IsActive && c.TheaterConcessions.Any(tc => tc.IsAvailable && tc.StockLeft > 0))
                .Take(4)
                .ToListAsync();

            ViewBag.TopConcessions = topConcessions;

            return View(movies);



        }

        public async Task<IActionResult> Details(int id)
        {
            var movie = await _context.Movies
                .Include(m => m.Language)
                .Include(m => m.Country)
                .Include(m => m.Rating)
                .Include(m => m.Director)
                .Include(m => m.MovieGenres)
                    .ThenInclude(mg => mg.Genre)
                .Include(m => m.Reviews)
                    .ThenInclude(r => r.User)
                .FirstOrDefaultAsync(m => m.ID == id);

            if (movie == null)
            {
                return NotFound();
            }

            // Đảm bảo PosterImagePath và WallpaperImagePath chỉ là tên file cho View Details
            // View Details.cshtml dùng: Url.Content($"~/images/movie_posters/{Model.PosterImagePath}")
            // nên Model.PosterImagePath phải là tên file.
            if (!string.IsNullOrEmpty(movie.PosterImagePath))
            {
                movie.PosterImagePath = Path.GetFileName(movie.PosterImagePath);
            }

            if (!string.IsNullOrEmpty(movie.WallpaperImagePath))
            {
                movie.WallpaperImagePath = Path.GetFileName(movie.WallpaperImagePath);
            }

            var reviews = movie.Reviews;
            double avgRating = reviews != null && reviews.Any() ? reviews.Average(r => r.Rating) : 0;
            ViewData["AverageRating"] = avgRating;

            return View("Details", movie);
        }
    }
}
