// ✅ FIXED PaymentController.cs
using Microsoft.AspNetCore.Mvc;
using Stripe.Checkout;
using Stripe;
using DKMovies.Models;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace DKMovies.Controllers
{
    public class PaymentController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;

        public PaymentController(ApplicationDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;

            // Initialize Stripe
            StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"];
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> CreateCheckoutSession(int ticketId)
        {
            try
            {
                // Get ticket with all related data
                var ticket = await _context.Tickets
                    .Include(t => t.ShowTime)
                        .ThenInclude(st => st.Movie)
                    .Include(t => t.ShowTime)
                        .ThenInclude(st => st.Auditorium)
                            .ThenInclude(a => a.Theater)
                    .Include(t => t.TicketSeats)
                        .ThenInclude(ts => ts.Seat)
                    .Include(t => t.OrderItems)
                        .ThenInclude(oi => oi.TheaterConcession)
                            .ThenInclude(tc => tc.Concession)
                    .Include(t => t.User)
                    .FirstOrDefaultAsync(t => t.ID == ticketId);

                if (ticket == null)
                {
                    TempData["ToastError"] = "Không tìm thấy vé.";
                    return RedirectToAction("Index", "MoviesList");
                }

                // Verify ownership
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (int.TryParse(userIdStr, out int userId) && ticket.UserID != userId && !User.IsInRole("Admin"))
                {
                    return Forbid("Bạn không có quyền thanh toán vé này.");
                }

                // Check ticket status
                if (ticket.Status != TicketStatus.PENDING)
                {
                    TempData["ToastError"] = "Vé này không ở trạng thái chờ thanh toán.";
                    return RedirectToAction("OrderConfirmation", "Tickets", new { ticketId });
                }

                // Check payment window (15 minutes)
                if (ticket.PurchaseTime.AddMinutes(15) < DateTime.Now)
                {
                    await CancelExpiredTicket(ticket);
                    TempData["ToastError"] = "Vé đã hết hạn thanh toán (15 phút). Vui lòng đặt vé lại.";
                    return RedirectToAction("OrderTicket", "Tickets", new { id = ticket.ShowTime.MovieID });
                }

                // ✅ Build Stripe line items - Convert VND to USD (1 USD = 24,000 VND)
                var lineItems = new List<SessionLineItemOptions>();

                // Add movie tickets - Convert VND to USD
                var seatNames = ticket.TicketSeats.Select(ts => $"{ts.Seat.RowLabel}{ts.Seat.SeatNumber}").ToList();
                var seatDescription = $"Seats: {string.Join(", ", seatNames)}";

                // Convert VND to USD (1 USD = 24,000 VND)
                var ticketPriceUSD = ticket.ShowTime.Price / 24000;
                var ticketPriceCents = (long)(ticketPriceUSD * 100);

                lineItems.Add(new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        UnitAmount = ticketPriceCents, // USD in cents
                        Currency = "usd",
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = $"Movie Ticket - {ticket.ShowTime.Movie.Title}",
                            Description = $"{seatDescription}\nShowtime: {ticket.ShowTime.StartTime:dd/MM/yyyy HH:mm}\nTheater: {ticket.ShowTime.Auditorium.Theater.Name} - {ticket.ShowTime.Auditorium.Name}"
                        }
                    },
                    Quantity = ticket.TicketSeats.Count
                });

                // Add concession items - Convert VND to USD
                if (ticket.OrderItems?.Any() == true)
                {
                    var concessionGroups = ticket.OrderItems
                        .GroupBy(oi => oi.TheaterConcession.Concession.Name)
                        .ToList();

                    foreach (var group in concessionGroups)
                    {
                        var firstItem = group.First();
                        var totalQuantity = group.Sum(g => g.Quantity);

                        // Convert concession price VND to USD
                        var concessionPriceUSD = firstItem.PriceAtPurchase / 24000;
                        var concessionPriceCents = (long)(concessionPriceUSD * 100);

                        lineItems.Add(new SessionLineItemOptions
                        {
                            PriceData = new SessionLineItemPriceDataOptions
                            {
                                UnitAmount = concessionPriceCents, // USD in cents
                                Currency = "usd",
                                ProductData = new SessionLineItemPriceDataProductDataOptions
                                {
                                    Name = firstItem.TheaterConcession.Concession.Name,
                                    Description = firstItem.TheaterConcession.Concession.Description ?? "Food & Beverage"
                                }
                            },
                            Quantity = totalQuantity
                        });
                    }
                }

                // Create Stripe checkout session
                var options = new SessionCreateOptions
                {
                    PaymentMethodTypes = new List<string>
                    {
                        "card"
                    },
                    LineItems = lineItems,
                    Mode = "payment",
                    SuccessUrl = Url.Action("PaymentSuccess", "Payment", new { ticketId }, Request.Scheme),
                    CancelUrl = Url.Action("PaymentCancel", "Payment", new { ticketId }, Request.Scheme),
                    CustomerEmail = ticket.User.Email,
                    Metadata = new Dictionary<string, string>
                    {
                        { "ticket_id", ticketId.ToString() },
                        { "user_id", ticket.UserID.ToString() }
                    },
                    ExpiresAt = DateTime.UtcNow.AddMinutes(30), // Minimum 30 minutes required by Stripe
                    PaymentIntentData = new SessionPaymentIntentDataOptions
                    {
                        Description = $"Movie ticket payment #{ticketId} - {ticket.ShowTime.Movie.Title}"
                    }
                };

                var service = new SessionService();
                var session = await service.CreateAsync(options);

                // Store Stripe session ID
                ticket.StripeSessionId = session.Id;
                _context.Update(ticket);
                await _context.SaveChangesAsync();

                // Use simple redirect
                return Redirect(session.Url);
            }
            catch (StripeException ex)
            {
                Console.WriteLine($"❌ Stripe Error: {ex.Message}");
                TempData["ToastError"] = $"Payment error: {ex.Message}";
                return RedirectToAction("PaymentSelection", "Tickets", new { ticketId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ General Error: {ex.Message}");
                TempData["ToastError"] = "An error occurred while creating payment session. Please try again.";
                return RedirectToAction("PaymentSelection", "Tickets", new { ticketId });
            }
        }

        [HttpGet]
        public async Task<IActionResult> PaymentSuccess(int ticketId)
        {
            try
            {
                var ticket = await _context.Tickets
                    .Include(t => t.ShowTime)
                        .ThenInclude(st => st.Movie)
                    .Include(t => t.User)
                    .Include(t => t.TicketSeats)
                        .ThenInclude(ts => ts.Seat)
                    .Include(t => t.OrderItems)
                        .ThenInclude(oi => oi.TheaterConcession)
                            .ThenInclude(tc => tc.Concession)
                    .FirstOrDefaultAsync(t => t.ID == ticketId);

                if (ticket == null)
                {
                    TempData["ToastError"] = "Không tìm thấy vé.";
                    return RedirectToAction("Index", "MoviesList");
                }

                // Verify Stripe payment if session exists
                if (!string.IsNullOrEmpty(ticket.StripeSessionId))
                {
                    var sessionService = new SessionService();
                    var session = await sessionService.GetAsync(ticket.StripeSessionId);

                    if (session.PaymentStatus == "paid")
                    {
                        // Only update if not already paid
                        if (ticket.Status == TicketStatus.PENDING)
                        {
                            // Update ticket status
                            ticket.Status = TicketStatus.PAID;
                            ticket.PaymentTime = DateTime.Now;

                            // Create payment record
                            var ticketPayment = new TicketPayment
                            {
                                TicketID = ticket.ID,
                                MethodID = 1, // Stripe payment method ID
                                PaymentStatus = "Completed",
                                PaidAmount = ticket.TotalPrice,
                                PaidAt = DateTime.Now
                            };

                            _context.TicketPayments.Add(ticketPayment);
                            _context.Update(ticket);
                            await _context.SaveChangesAsync();

                            // Send confirmation email
                            await SendConfirmationEmail(ticket);

                            TempData["ToastSuccess"] = "Thanh toán thành công! Vé của bạn đã được xác nhận.";
                        }
                        else
                        {
                            TempData["ToastInfo"] = "Vé đã được thanh toán trước đó.";
                        }
                    }
                    else
                    {
                        TempData["ToastWarning"] = "Thanh toán chưa hoàn tất. Vui lòng kiểm tra lại.";
                    }
                }

                return RedirectToAction("OrderConfirmation", "Tickets", new { ticketId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Payment Success Error: {ex.Message}");
                TempData["ToastError"] = "Có lỗi xảy ra khi xác nhận thanh toán.";
                return RedirectToAction("OrderConfirmation", "Tickets", new { ticketId });
            }
        }

        [HttpGet]
        public async Task<IActionResult> PaymentCancel(int ticketId)
        {
            try
            {
                var ticket = await _context.Tickets
                    .Include(t => t.ShowTime)
                    .FirstOrDefaultAsync(t => t.ID == ticketId);

                if (ticket != null)
                {
                    TempData["ToastWarning"] = "Thanh toán đã bị hủy. Bạn vẫn có thể hoàn tất thanh toán trước khi hết hạn.";

                    // Check if still within payment window
                    if (ticket.PurchaseTime.AddMinutes(15) >= DateTime.Now)
                    {
                        return RedirectToAction("PaymentSelection", "Tickets", new { ticketId });
                    }
                    else
                    {
                        // Cancel expired ticket
                        await CancelExpiredTicket(ticket);
                        TempData["ToastError"] = "Vé đã hết hạn thanh toán và bị hủy.";
                        return RedirectToAction("OrderTicket", "Tickets", new { id = ticket.ShowTime.MovieID });
                    }
                }

                return RedirectToAction("Index", "MoviesList");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Payment Cancel Error: {ex.Message}");
                TempData["ToastError"] = "Có lỗi xảy ra.";
                return RedirectToAction("Index", "MoviesList");
            }
        }

        private async Task CancelExpiredTicket(Ticket ticket)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                Console.WriteLine($"🔄 Cancelling expired ticket {ticket.ID}");

                // Cancel ticket
                ticket.Status = TicketStatus.CANCELLED;
                _context.Update(ticket);

                // Restore concession stock
                if (ticket.OrderItems?.Any() == true)
                {
                    foreach (var orderItem in ticket.OrderItems)
                    {
                        var concession = await _context.TheaterConcessions
                            .FirstOrDefaultAsync(tc => tc.ID == orderItem.TheaterConcessionID);

                        if (concession != null)
                        {
                            concession.StockLeft += orderItem.Quantity;
                            _context.Update(concession);
                            Console.WriteLine($"📦 Restored {orderItem.Quantity} stock for concession {concession.ID}");
                        }
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                Console.WriteLine($"✅ Successfully cancelled ticket {ticket.ID}");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"❌ Failed to cancel ticket {ticket.ID}: {ex.Message}");
                throw;
            }
        }

        private async Task SendConfirmationEmail(Ticket ticket)
        {
            try
            {
                var smtpSettings = _configuration.GetSection("SmtpSettings");
                var fromEmail = smtpSettings["FromEmail"];
                var smtpHost = smtpSettings["Host"];
                var smtpPort = int.Parse(smtpSettings["Port"]);
                var smtpUser = smtpSettings["Username"];
                var smtpPass = smtpSettings["Password"];

                // Skip email if settings are not configured
                if (string.IsNullOrEmpty(fromEmail) || string.IsNullOrEmpty(smtpHost))
                {
                    Console.WriteLine("⚠️ Email settings not configured, skipping email");
                    return;
                }

                using var client = new SmtpClient(smtpHost, smtpPort)
                {
                    Credentials = new NetworkCredential(smtpUser, smtpPass),
                    EnableSsl = true
                };

                var seatNames = ticket.TicketSeats.Select(ts => $"{ts.Seat.RowLabel}{ts.Seat.SeatNumber}").ToList();

                var emailBody = new StringBuilder();
                emailBody.AppendLine($"Dear {ticket.User.FullName ?? ticket.User.Username},");
                emailBody.AppendLine();
                emailBody.AppendLine("Thank you for booking with DK Movies! Here is your ticket information:");
                emailBody.AppendLine();
                emailBody.AppendLine("=== TICKET INFORMATION ===");
                emailBody.AppendLine($"Ticket ID: #{ticket.ID}");
                emailBody.AppendLine($"Movie: {ticket.ShowTime.Movie.Title}");
                emailBody.AppendLine($"Theater: {ticket.ShowTime.Auditorium.Theater.Name}");
                emailBody.AppendLine($"Auditorium: {ticket.ShowTime.Auditorium.Name}");
                emailBody.AppendLine($"Showtime: {ticket.ShowTime.StartTime:dd/MM/yyyy HH:mm}");
                emailBody.AppendLine($"Seats: {string.Join(", ", seatNames)}");
                emailBody.AppendLine($"Status: {(ticket.Status == TicketStatus.PAID ? "Paid" : "Confirmed")}");
                emailBody.AppendLine();
                emailBody.AppendLine($"Total: {ticket.TotalPrice:N0} VND");
                emailBody.AppendLine();
                emailBody.AppendLine("Please arrive at the theater at least 15 minutes before showtime.");
                emailBody.AppendLine();
                emailBody.AppendLine("Best regards,");
                emailBody.AppendLine("DK Movies Team");

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(fromEmail, "DK Movies"),
                    Subject = $"Ticket Confirmation #{ticket.ID} - {ticket.ShowTime.Movie.Title}",
                    Body = emailBody.ToString(),
                    IsBodyHtml = false
                };

                mailMessage.To.Add(ticket.User.Email);
                await client.SendMailAsync(mailMessage);

                Console.WriteLine($"✅ Confirmation email sent to {ticket.User.Email}");
            }
            catch (Exception ex)
            {
                // Log email error but don't throw
                Console.WriteLine($"⚠️ Email sending failed: {ex.Message}");
            }
        }
    }
}

