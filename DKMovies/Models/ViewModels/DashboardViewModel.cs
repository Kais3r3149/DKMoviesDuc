// DashboardViewModel.cs - Hoàn thiện
using System.Collections.Generic;
using System.Linq;

namespace DKMovies.Models.ViewModels
{
    public class DashboardViewModel
    {
        // ===== BASIC STATISTICS =====
        public int TotalUsers { get; set; }
        public int TotalEmployees { get; set; }
        public int TotalMovies { get; set; }
        public int TotalShowTimes { get; set; }
        public int TotalConcessions { get; set; }
        public decimal TotalRevenue { get; set; }

        // ===== MOVIE ANALYTICS =====
        public List<MovieScoreViewModel> TopMovies { get; set; } = new List<MovieScoreViewModel>();

        // ===== ADDITIONAL METRICS =====
        public int TodayTickets { get; set; }
        public decimal ThisMonthRevenue { get; set; }
        public int ActiveShowtimes { get; set; }
        public double AverageRating { get; set; }

        // ===== PERFORMANCE METRICS =====
        public int PendingTickets { get; set; }
        public int ConfirmedTickets { get; set; }
        public int CancelledTickets { get; set; }
        public decimal ThisWeekRevenue { get; set; }
        public decimal LastWeekRevenue { get; set; }

        // ===== AUTO MANAGEMENT STATS =====
        public int AutoAddedShowtimes { get; set; }
        public int AutoRemovedShowtimes { get; set; }
        public DateTime? LastAutoManagementRun { get; set; }

        // ===== HELPER PROPERTIES =====
        public bool HasMovieData => TopMovies != null && TopMovies.Any();
        public string FormattedTotalRevenue => TotalRevenue.ToString("N0") + " ₫";
        public string FormattedMonthRevenue => ThisMonthRevenue.ToString("N0") + " ₫";
        public string FormattedWeekRevenue => ThisWeekRevenue.ToString("N0") + " ₫";

        // ===== CALCULATED PROPERTIES =====
        public decimal RevenueGrowthPercentage
        {
            get
            {
                if (LastWeekRevenue == 0) return 0;
                return ((ThisWeekRevenue - LastWeekRevenue) / LastWeekRevenue) * 100;
            }
        }

        public double TicketConversionRate
        {
            get
            {
                var totalTickets = PendingTickets + ConfirmedTickets + CancelledTickets;
                if (totalTickets == 0) return 0;
                return ((double)(ConfirmedTickets + PendingTickets) / totalTickets) * 100;
            }
        }

        public string PopularMovieTitle
        {
            get
            {
                return TopMovies?.FirstOrDefault()?.Title ?? "Chưa có dữ liệu";
            }
        }

        // ===== QUICK STATS FOR CARDS =====
        public List<QuickStatViewModel> QuickStats => new List<QuickStatViewModel>
        {
            new QuickStatViewModel
            {
                Title = "Người dùng",
                Value = TotalUsers.ToString(),
                Icon = "fas fa-users",
                Color = "primary",
                SubText = $"+{TodayTickets} vé hôm nay"
            },
            new QuickStatViewModel
            {
                Title = "Phim",
                Value = TotalMovies.ToString(),
                Icon = "fas fa-film",
                Color = "warning",
                SubText = $"{ActiveShowtimes} suất đang chiếu"
            },
            new QuickStatViewModel
            {
                Title = "Doanh thu tháng",
                Value = FormattedMonthRevenue,
                Icon = "fas fa-money-bill-wave",
                Color = "success",
                SubText = $"Tổng: {FormattedTotalRevenue}"
            },
            new QuickStatViewModel
            {
                Title = "Đánh giá TB",
                Value = $"{AverageRating:F1}/5",
                Icon = "fas fa-star",
                Color = "warning",
                SubText = "Từ khách hàng"
            }
        };

        // ===== THEATER PERFORMANCE =====
        public List<TheaterPerformanceViewModel> TheaterPerformance { get; set; } = new List<TheaterPerformanceViewModel>();

        // ===== UPCOMING EVENTS =====
        public List<UpcomingEventViewModel> UpcomingEvents { get; set; } = new List<UpcomingEventViewModel>();
    }

    // ===== SUPPORTING VIEW MODELS =====
    public class QuickStatViewModel
    {
        public string Title { get; set; }
        public string Value { get; set; }
        public string Icon { get; set; }
        public string Color { get; set; }
        public string SubText { get; set; }
    }

    public class TheaterPerformanceViewModel
    {
        public int TheaterID { get; set; }
        public string TheaterName { get; set; }
        public string Location { get; set; }
        public int TotalShows { get; set; }
        public int TicketsSold { get; set; }
        public decimal Revenue { get; set; }
        public double OccupancyRate { get; set; }
        public string FormattedRevenue => Revenue.ToString("N0") + " ₫";
        public string PerformanceLevel
        {
            get
            {
                if (OccupancyRate >= 80) return "Xuất sắc";
                if (OccupancyRate >= 60) return "Tốt";
                if (OccupancyRate >= 40) return "Trung bình";
                return "Cần cải thiện";
            }
        }
    }

    public class UpcomingEventViewModel
    {
        public DateTime EventDate { get; set; }
        public string EventType { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Icon { get; set; }
        public string Color { get; set; }
        public bool IsUrgent { get; set; }
        public string FormattedDate => EventDate.ToString("dd/MM/yyyy HH:mm");
        public string RelativeTime
        {
            get
            {
                var diff = EventDate - DateTime.Now;
                if (diff.TotalDays > 1)
                    return $"Sau {(int)diff.TotalDays} ngày";
                if (diff.TotalHours > 1)
                    return $"Sau {(int)diff.TotalHours} giờ";
                if (diff.TotalMinutes > 0)
                    return $"Sau {(int)diff.TotalMinutes} phút";
                return "Ngay bây giờ";
            }
        }
    }
}