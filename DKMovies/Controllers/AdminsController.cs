// AdminsController.cs - Fixed Version (No Orders)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using DKMovies.Models;
using System.Text;
using System.Security.Cryptography;
using DKMovies.Models.ViewModels;

namespace DKMovies.Controllers
{
    public class AdminsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Admins Dashboard
        public async Task<IActionResult> Index()
        {
            try
            {
                var totalUsers = await _context.Users.CountAsync();
                var totalEmployees = await _context.Employees.CountAsync();
                var totalMovies = await _context.Movies.CountAsync();
                var totalShowTimes = await _context.ShowTimes.CountAsync();
                var totalConcessions = await _context.Concessions.CountAsync();

                // Calculate total revenue from tickets only (no Orders table)
                var totalRevenue = await _context.Tickets
                    .Include(t => t.ShowTime)
                    .SumAsync(t => t.ShowTime.Price);

                var model = new DashboardViewModel
                {
                    TotalUsers = totalUsers,
                    TotalEmployees = totalEmployees,
                    TotalMovies = totalMovies,
                    TotalShowTimes = totalShowTimes,
                    TotalConcessions = totalConcessions,
                    TotalRevenue = totalRevenue
                };

                // Pass data through ViewBag for the view
                ViewBag.TotalUsers = totalUsers;
                ViewBag.TotalEmployees = totalEmployees;
                ViewBag.TotalMovies = totalMovies;
                ViewBag.TotalShowTimes = totalShowTimes;
                ViewBag.TotalConcessions = totalConcessions;
                ViewBag.TotalRevenue = totalRevenue;

                return View(model);
            }
            catch (Exception ex)
            {
                // Log the error and return a safe fallback
                var emptyModel = new DashboardViewModel
                {
                    TotalUsers = 0,
                    TotalEmployees = 0,
                    TotalMovies = 0,
                    TotalShowTimes = 0,
                    TotalConcessions = 0,
                    TotalRevenue = 0
                };

                ViewBag.TotalUsers = 0;
                ViewBag.TotalEmployees = 0;
                ViewBag.TotalMovies = 0;
                ViewBag.TotalShowTimes = 0;
                ViewBag.TotalConcessions = 0;
                ViewBag.TotalRevenue = 0;
                ViewBag.ErrorMessage = "Có lỗi xảy ra khi tải dữ liệu dashboard.";

                return View(emptyModel);
            }
        }

        public async Task<IActionResult> MovieDashboard()
        {
            try
            {
                // Check if we have any tickets first
                var hasTickets = await _context.Tickets.AnyAsync();
                if (!hasTickets)
                {
                    return View(new List<MovieScoreViewModel>());
                }

                var movieStats = await _context.Tickets
                    .Include(t => t.ShowTime)
                    .ThenInclude(st => st.Movie)
                    .ThenInclude(m => m.Reviews)
                    .Where(t => t.ShowTime != null && t.ShowTime.Movie != null)
                    .GroupBy(t => t.ShowTime.MovieID)
                    .Select(g => new
                    {
                        MovieID = g.Key,
                        Title = g.First().ShowTime.Movie.Title,
                        TicketsSold = g.Count(),
                        TotalRevenue = g.Sum(t => t.ShowTime.Price),
                        AvgRating = g.First().ShowTime.Movie.Reviews.Any()
                            ? g.First().ShowTime.Movie.Reviews.Average(r => r.Rating)
                            : 0
                    })
                    .ToListAsync();

                if (!movieStats.Any())
                {
                    return View(new List<MovieScoreViewModel>());
                }

                double maxRevenue = movieStats.Max(s => (double)s.TotalRevenue);
                double maxTickets = movieStats.Max(s => (double)s.TicketsSold);
                double maxRating = 5.0;

                var scored = movieStats.Select(s => new MovieScoreViewModel
                {
                    MovieID = s.MovieID,
                    Title = s.Title ?? "Unknown Movie",
                    TicketsSold = s.TicketsSold,
                    TotalRevenue = s.TotalRevenue,
                    AvgRating = s.AvgRating,
                    PriorityScore = maxRevenue > 0 && maxTickets > 0
                        ? ((double)s.TotalRevenue / maxRevenue) * 50
                          + ((double)s.TicketsSold / maxTickets) * 40
                          + (s.AvgRating / maxRating) * 10
                        : 0
                })
                .OrderByDescending(s => s.PriorityScore)
                .Take(5)
                .ToList();

                return View(scored);
            }
            catch (Exception ex)
            {
                // Log error and return empty data
                ViewBag.ErrorMessage = "Có lỗi xảy ra khi tải dữ liệu thống kê phim.";
                return View(new List<MovieScoreViewModel>());
            }
        }

        // Add a simple Home action to handle navigation to main dashboard
        public IActionResult Home()
        {
            return RedirectToAction("Index");
        }
    }
}