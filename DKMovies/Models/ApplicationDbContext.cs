using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;
using DKMovies.Models;

namespace DKMovies.Models
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<Country> Countries { get; set; }
        public DbSet<Genre> Genres { get; set; }
        public DbSet<Rating> Ratings { get; set; }
        public DbSet<Language> Languages { get; set; }
        public DbSet<Director> Directors { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Theater> Theaters { get; set; }
        public DbSet<Auditorium> Auditoriums { get; set; }
        public DbSet<Seat> Seats { get; set; }
        public DbSet<Movie> Movies { get; set; }
        public DbSet<MovieUserFavourite> MovieUserFavourites { get; set; }
        public DbSet<MovieGenre> MovieGenres { get; set; }
        public DbSet<WatchListSingular> WatchList { get; set; }
        public DbSet<CartItem> CartItems { get; set; }
        public DbSet<ShowTime> ShowTimes { get; set; }
        public DbSet<Ticket> Tickets { get; set; }
        public DbSet<TicketSeat> TicketSeats { get; set; }
        public DbSet<TicketPayment> TicketPayments { get; set; }
        public DbSet<PaymentMethod> PaymentMethods { get; set; }
        public DbSet<EmployeeRole> EmployeeRoles { get; set; }
        public DbSet<Employee> Employees { get; set; }
        public DbSet<Concession> Concessions { get; set; }
        public DbSet<TheaterConcession> TheaterConcessions { get; set; }
        public DbSet<Admin> Admins { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<Order> Role { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<OrderPayment> OrderPayments { get; set; }
        public DbSet<Review> Reviews { get; set; }
        public DbSet<Sale> Sales { get; set; }
        public DbSet<SaleDetail> SaleDetails { get; set; }
        public DbSet<WatchlistItem> WatchlistItems { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ===== TABLE NAMES =====
            modelBuilder.Entity<User>().ToTable("Users");
            modelBuilder.Entity<Role>().ToTable("Roles");
            modelBuilder.Entity<Theater>().ToTable("Theaters");
            modelBuilder.Entity<Employee>().ToTable("Employees");
            modelBuilder.Entity<EmployeeRole>().ToTable("EmployeeRoles");
            modelBuilder.Entity<Auditorium>().ToTable("Auditoriums");
            modelBuilder.Entity<MovieUserFavourite>().ToTable("MovieUserFavourites");
            modelBuilder.Entity<Seat>().ToTable("Seats");
            modelBuilder.Entity<Movie>().ToTable("Movies");
            modelBuilder.Entity<MovieGenre>().ToTable("MovieGenres");
            modelBuilder.Entity<WatchListSingular>().ToTable("WatchList");
            modelBuilder.Entity<ShowTime>().ToTable("ShowTimes");
            modelBuilder.Entity<Ticket>().ToTable("Tickets");
            modelBuilder.Entity<TicketSeat>().ToTable("TicketSeats");
            modelBuilder.Entity<PaymentMethod>().ToTable("PaymentMethods");
            modelBuilder.Entity<TicketPayment>().ToTable("TicketPayments");
            modelBuilder.Entity<Concession>().ToTable("Concessions");
            modelBuilder.Entity<TheaterConcession>().ToTable("TheaterConcessions");
            modelBuilder.Entity<Order>().ToTable("Orders");
            modelBuilder.Entity<OrderItem>().ToTable("OrderItems");
            modelBuilder.Entity<OrderPayment>().ToTable("OrderPayments");
            modelBuilder.Entity<Review>().ToTable("Reviews");
            modelBuilder.Entity<Country>().ToTable("Countries");
            modelBuilder.Entity<Genre>().ToTable("Genres");
            modelBuilder.Entity<Rating>().ToTable("Ratings");
            modelBuilder.Entity<Director>().ToTable("Directors");
            modelBuilder.Entity<Language>().ToTable("Languages");
            modelBuilder.Entity<Admin>().ToTable("Admins");
            modelBuilder.Entity<CartItem>().ToTable("CartItems");
            modelBuilder.Entity<Sale>().ToTable("Sales");
            modelBuilder.Entity<SaleDetail>().ToTable("SaleDetails");
            modelBuilder.Entity<WatchlistItem>().ToTable("WatchlistItems");

            // ===== ENUMS =====
            modelBuilder.Entity<Ticket>()
                .Property(t => t.Status)
                .HasConversion<string>(); // stores enum as string

            // ===== DECIMAL PRECISION =====
            modelBuilder.Entity<Ticket>()
                .Property(t => t.TotalPrice)
                .HasColumnType("decimal(10,2)");

            modelBuilder.Entity<TheaterConcession>()
                .Property(tc => tc.Price)
                .HasColumnType("decimal(6,2)"); // ✅ SỬA: decimal(6,2) theo database

            modelBuilder.Entity<OrderItem>()
                .Property(oi => oi.PriceAtPurchase)
                .HasColumnType("decimal(6,2)");

            modelBuilder.Entity<ShowTime>()
                .Property(st => st.Price)
                .HasColumnType("decimal(8,2)");

            modelBuilder.Entity<Employee>()
                .Property(e => e.Salary)
                .HasColumnType("decimal(10,2)");

            modelBuilder.Entity<Order>()
                .Property(o => o.TotalAmount)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<TicketPayment>()
                .Property(tp => tp.PaidAmount)
                .HasColumnType("decimal(10,2)");

            modelBuilder.Entity<OrderPayment>()
                .Property(op => op.PaidAmount)
                .HasColumnType("decimal(10,2)");

            modelBuilder.Entity<SaleDetail>()
                .Property(sd => sd.UnitPrice)
                .HasColumnType("decimal(18,2)");

            // ===== PRIMARY KEYS =====
            modelBuilder.Entity<Country>().HasKey(c => c.ID);
            modelBuilder.Entity<Role>().HasKey(r => r.ID);
            modelBuilder.Entity<Genre>().HasKey(g => g.ID);
            modelBuilder.Entity<Rating>().HasKey(r => r.ID);
            modelBuilder.Entity<Language>().HasKey(l => l.ID);
            modelBuilder.Entity<Director>().HasKey(d => d.ID);
            modelBuilder.Entity<User>().HasKey(u => u.ID);
            modelBuilder.Entity<Theater>().HasKey(t => t.ID);
            modelBuilder.Entity<Auditorium>().HasKey(a => a.ID);
            modelBuilder.Entity<Seat>().HasKey(s => s.ID);
            modelBuilder.Entity<Movie>().HasKey(m => m.ID);
            modelBuilder.Entity<MovieGenre>().HasKey(mg => mg.ID);
            modelBuilder.Entity<ShowTime>().HasKey(st => st.ID);
            modelBuilder.Entity<Ticket>().HasKey(t => t.ID);
            modelBuilder.Entity<TicketSeat>().HasKey(ts => ts.ID);
            modelBuilder.Entity<PaymentMethod>().HasKey(pm => pm.ID);
            modelBuilder.Entity<TicketPayment>().HasKey(tp => tp.ID);
            modelBuilder.Entity<EmployeeRole>().HasKey(er => er.ID);
            modelBuilder.Entity<Employee>().HasKey(e => e.ID);
            modelBuilder.Entity<Concession>().HasKey(c => c.ID);
            modelBuilder.Entity<TheaterConcession>().HasKey(tc => tc.ID);
            modelBuilder.Entity<Order>().HasKey(o => o.ID);
            modelBuilder.Entity<OrderItem>().HasKey(oi => oi.ID);
            modelBuilder.Entity<OrderPayment>().HasKey(op => op.ID);
            modelBuilder.Entity<Review>().HasKey(r => r.ID);
            modelBuilder.Entity<Admin>().HasKey(a => a.ID);
            modelBuilder.Entity<CartItem>().HasKey(ci => ci.ID);
            modelBuilder.Entity<Sale>().HasKey(s => s.ID);
            modelBuilder.Entity<SaleDetail>().HasKey(sd => sd.ID);
            modelBuilder.Entity<WatchlistItem>().HasKey(wi => wi.ID);
            modelBuilder.Entity<MovieUserFavourite>().HasKey(muf => muf.ID);
            modelBuilder.Entity<WatchListSingular>().HasKey(wls => wls.ID);

            // ===== UNIQUE INDEXES =====
            modelBuilder.Entity<Country>().HasIndex(c => c.Name).IsUnique();
            modelBuilder.Entity<Genre>().HasIndex(g => g.Name).IsUnique();
            modelBuilder.Entity<Rating>().HasIndex(r => r.Value).IsUnique();
            modelBuilder.Entity<Language>().HasIndex(l => l.Name).IsUnique();
            modelBuilder.Entity<User>().HasIndex(u => u.Username).IsUnique();
            modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();
            modelBuilder.Entity<Theater>().HasIndex(t => new { t.Name, t.Location }).IsUnique();
            modelBuilder.Entity<Seat>().HasIndex(s => new { s.AuditoriumID, s.RowLabel, s.SeatNumber }).IsUnique();
            modelBuilder.Entity<Movie>().HasIndex(m => m.Title).IsUnique();
            modelBuilder.Entity<MovieGenre>().HasIndex(mg => new { mg.MovieID, mg.GenreID }).IsUnique();
            modelBuilder.Entity<PaymentMethod>().HasIndex(pm => pm.Name).IsUnique();
            modelBuilder.Entity<EmployeeRole>().HasIndex(er => er.Name).IsUnique();
            modelBuilder.Entity<Employee>().HasIndex(e => e.Email).IsUnique();
            modelBuilder.Entity<Employee>().HasIndex(e => e.CitizenID).IsUnique();
            modelBuilder.Entity<Admin>().HasIndex(a => a.EmployeeID).IsUnique();
            modelBuilder.Entity<Admin>().HasIndex(a => a.Username).IsUnique();

            // ✅ NEW: Additional unique constraints
            modelBuilder.Entity<TheaterConcession>()
                .HasIndex(tc => new { tc.TheaterID, tc.ConcessionID })
                .IsUnique();

            modelBuilder.Entity<TicketSeat>()
                .HasIndex(ts => new { ts.TicketID, ts.SeatID })
                .IsUnique();

            modelBuilder.Entity<WatchlistItem>()
                .HasIndex(wi => new { wi.UserID, wi.MovieID })
                .IsUnique();

            modelBuilder.Entity<MovieUserFavourite>()
                .HasIndex(muf => new { muf.UserID, muf.MovieID })
                .IsUnique();

            // ===== FOREIGN KEY RELATIONSHIPS =====

            // ✅ THÊM: User -> Role relationship
            modelBuilder.Entity<User>()
                .HasOne(u => u.Role)
                .WithMany(r => r.Users)
                .HasForeignKey(u => u.RoleID)
                .OnDelete(DeleteBehavior.Restrict);

            // Countries
            modelBuilder.Entity<Country>()
                .HasMany<Director>()
                .WithOne(d => d.Country)
                .HasForeignKey(d => d.CountryID)
                .OnDelete(DeleteBehavior.Cascade); // ✅ SỬA: CASCADE theo database

            modelBuilder.Entity<Country>()
                .HasMany<Movie>()
                .WithOne(m => m.Country)
                .HasForeignKey(m => m.CountryID)
                .OnDelete(DeleteBehavior.NoAction); // ✅ SỬA: NO ACTION theo database

            // Ratings
            modelBuilder.Entity<Rating>()
                .HasMany(r => r.Movies)
                .WithOne(m => m.Rating)
                .HasForeignKey(m => m.RatingID)
                .OnDelete(DeleteBehavior.Cascade); // ✅ SỬA: CASCADE theo database

            // Languages
            modelBuilder.Entity<Language>()
                .HasMany(l => l.Movies)
                .WithOne(m => m.Language)
                .HasForeignKey(m => m.LanguageID)
                .OnDelete(DeleteBehavior.Cascade); // ✅ SỬA: CASCADE theo database

            modelBuilder.Entity<Language>()
                .HasMany(l => l.ShowTimes)
                .WithOne(st => st.SubtitleLanguage)
                .HasForeignKey(st => st.SubtitleLanguageID)
                .OnDelete(DeleteBehavior.NoAction);

            // Directors
            modelBuilder.Entity<Director>()
                .HasMany(d => d.Movies)
                .WithOne(m => m.Director)
                .HasForeignKey(m => m.DirectorID)
                .OnDelete(DeleteBehavior.SetNull);

            // Users
            modelBuilder.Entity<User>()
                .HasMany(u => u.Tickets)
                .WithOne(t => t.User)
                .HasForeignKey(t => t.UserID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<User>()
                .HasMany(u => u.Reviews)
                .WithOne(r => r.User)
                .HasForeignKey(r => r.UserID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<User>()
                .HasMany(u => u.Orders)
                .WithOne(o => o.User)
                .HasForeignKey(o => o.UserID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<User>()
                .HasMany(u => u.CartItems)
                .WithOne(ci => ci.User)
                .HasForeignKey(ci => ci.UserID)
                .OnDelete(DeleteBehavior.Cascade);

            // Theaters
            modelBuilder.Entity<Theater>()
                .HasMany(t => t.Auditoriums)
                .WithOne(a => a.Theater)
                .HasForeignKey(a => a.TheaterID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Theater>()
                .HasMany(t => t.Employees)
                .WithOne(e => e.Theater)
                .HasForeignKey(e => e.TheaterID)
                .OnDelete(DeleteBehavior.Cascade);

            // TheaterConcessions
            modelBuilder.Entity<TheaterConcession>()
                .HasMany(tc => tc.OrderItems)
                .WithOne(oi => oi.TheaterConcession)
                .HasForeignKey(oi => oi.TheaterConcessionID)
                .OnDelete(DeleteBehavior.NoAction); // ✅ SỬA: NO ACTION theo database

            modelBuilder.Entity<TheaterConcession>()
                .HasMany(tc => tc.CartItems)
                .WithOne(ci => ci.TheaterConcession)
                .HasForeignKey(ci => ci.TheaterConcessionID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TheaterConcession>()
                .HasMany(tc => tc.SaleDetails)
                .WithOne(sd => sd.TheaterConcession)
                .HasForeignKey(sd => sd.TheaterConcessionID)
                .OnDelete(DeleteBehavior.NoAction); // ✅ SỬA: NO ACTION theo database

            modelBuilder.Entity<Order>()
                .HasMany(o => o.OrderItems)
                .WithOne(oi => oi.Order)
                .HasForeignKey(oi => oi.OrderID)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired(false); // ✅ Optional relationship

            modelBuilder.Entity<Order>()
                .HasMany(o => o.OrderPayments)
                .WithOne(op => op.Order)
                .HasForeignKey(op => op.OrderID)
                .OnDelete(DeleteBehavior.Cascade);

            // Auditoriums
            modelBuilder.Entity<Auditorium>()
                .HasMany(a => a.Seats)
                .WithOne(s => s.Auditorium)
                .HasForeignKey(s => s.AuditoriumID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Auditorium>()
                .HasMany(a => a.ShowTimes)
                .WithOne(st => st.Auditorium)
                .HasForeignKey(st => st.AuditoriumID)
                .OnDelete(DeleteBehavior.Cascade);


            // Seats -> TicketSeats
            modelBuilder.Entity<Seat>()
                .HasMany(s => s.TicketSeats)
                .WithOne(ts => ts.Seat)
                .HasForeignKey(ts => ts.SeatID)
                .OnDelete(DeleteBehavior.NoAction);

            // Movies
            modelBuilder.Entity<Movie>()
                .HasMany(m => m.MovieGenres)
                .WithOne(mg => mg.Movie)
                .HasForeignKey(mg => mg.MovieID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Movie>()
                .HasMany(m => m.ShowTimes)
                .WithOne(st => st.Movie)
                .HasForeignKey(st => st.MovieID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Movie>()
                .HasMany(m => m.Reviews)
                .WithOne(r => r.Movie)
                .HasForeignKey(r => r.MovieID)
                .OnDelete(DeleteBehavior.Cascade);

            // ShowTimes
            modelBuilder.Entity<ShowTime>()
                .HasMany(st => st.Tickets)
                .WithOne(t => t.ShowTime)
                .HasForeignKey(t => t.ShowTimeID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Ticket>()
                .HasMany(t => t.TicketPayments)
                .WithOne(tp => tp.Ticket)
                .HasForeignKey(tp => tp.TicketID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Ticket>()
                .HasMany(t => t.TicketSeats)
                .WithOne(ts => ts.Ticket)
                .HasForeignKey(ts => ts.TicketID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Ticket>()
                .HasMany(t => t.OrderItems)
                .WithOne(oi => oi.Ticket)
                .HasForeignKey(oi => oi.TicketID)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired(false); // ✅ Optional relationship


            // PaymentMethods
            modelBuilder.Entity<PaymentMethod>()
                .HasMany(pm => pm.TicketPayments)
                .WithOne(tp => tp.PaymentMethod)
                .HasForeignKey(tp => tp.MethodID)
                .OnDelete(DeleteBehavior.Cascade);

            // EmployeeRoles
            modelBuilder.Entity<EmployeeRole>()
                .HasMany(er => er.Employees)
                .WithOne(e => e.Role)
                .HasForeignKey(e => e.RoleID)
                .OnDelete(DeleteBehavior.SetNull);

            // Employees
            modelBuilder.Entity<Employee>()
                .HasMany(e => e.Admins)
                .WithOne(a => a.Employee)
                .HasForeignKey(a => a.EmployeeID)
                .OnDelete(DeleteBehavior.Cascade);

            // Concessions
            modelBuilder.Entity<Concession>()
                .HasMany(c => c.TheaterConcessions)
                .WithOne(tc => tc.Concession)
                .HasForeignKey(tc => tc.ConcessionID)
                .OnDelete(DeleteBehavior.Cascade);

            // TheaterConcessions
            modelBuilder.Entity<TheaterConcession>()
                .HasMany(tc => tc.OrderItems)
                .WithOne(oi => oi.TheaterConcession)
                .HasForeignKey(oi => oi.TheaterConcessionID)
                .OnDelete(DeleteBehavior.Restrict); // Don't delete if orders exist

            modelBuilder.Entity<TheaterConcession>()
                .HasMany(tc => tc.CartItems)
                .WithOne(ci => ci.TheaterConcession)
                .HasForeignKey(ci => ci.TheaterConcessionID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Order>()
                .HasMany(o => o.OrderPayments)
                .WithOne(op => op.Order)
                .HasForeignKey(op => op.OrderID)
                .OnDelete(DeleteBehavior.Cascade);

            // ✅ NEW: OrderItem flexible relationships
            // OrderItem can belong to either Ticket (for movie concessions) OR Order (for standalone purchases)
            modelBuilder.Entity<OrderItem>()
                .HasOne(oi => oi.Ticket)
                .WithMany(t => t.OrderItems)
                .HasForeignKey(oi => oi.TicketID)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired(false); // Optional relationship


            // Sales
            modelBuilder.Entity<Sale>()
                .HasMany(s => s.SaleDetails)
                .WithOne(sd => sd.Sale)
                .HasForeignKey(sd => sd.SaleID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<SaleDetail>()
                .HasOne(sd => sd.TheaterConcession)
                .WithMany()
                .HasForeignKey(sd => sd.TheaterConcessionID)
                .OnDelete(DeleteBehavior.Restrict);

            // WatchlistItems
            modelBuilder.Entity<WatchlistItem>()
                .HasOne(wi => wi.User)
                .WithMany(u => u.WatchlistItems)
                .HasForeignKey(wi => wi.UserID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<WatchlistItem>()
                .HasOne(wi => wi.Movie)
                .WithMany()
                .HasForeignKey(wi => wi.MovieID)
                .OnDelete(DeleteBehavior.Cascade);

            // MovieUserFavourites
            modelBuilder.Entity<MovieUserFavourite>()
                .HasOne(muf => muf.User)
                .WithMany(u => u.MovieUserFavourites)
                .HasForeignKey(muf => muf.UserID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<MovieUserFavourite>()
                .HasOne(muf => muf.Movie)
                .WithMany()
                .HasForeignKey(muf => muf.MovieID)
                .OnDelete(DeleteBehavior.Cascade);

            // WatchListSingular
            modelBuilder.Entity<WatchListSingular>()
                .HasOne(wls => wls.User)
                .WithMany(u => u.WatchList)
                .HasForeignKey(wls => wls.UserID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<WatchListSingular>()
                .HasOne(wls => wls.Movie)
                .WithMany()
                .HasForeignKey(wls => wls.MovieID)
                .OnDelete(DeleteBehavior.Cascade);

            // ===== DATA SEEDING =====
            SeedData(modelBuilder);
        }

        private void SeedData(ModelBuilder modelBuilder)
        {
            // ✅ Seed Payment Methods
            modelBuilder.Entity<PaymentMethod>().HasData(
                new PaymentMethod
                {
                    ID = 1,
                    Name = "Stripe",
                    Description = "Thanh toán trực tuyến qua thẻ tín dụng/ghi nợ"
                },
                new PaymentMethod
                {
                    ID = 2,
                    Name = "Cash",
                    Description = "Thanh toán bằng tiền mặt tại quầy"
                },
                new PaymentMethod
                {
                    ID = 3,
                    Name = "Bank Transfer",
                    Description = "Chuyển khoản ngân hàng"
                }
            );

        }
    }
}   

