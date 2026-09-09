# IT15 Final Project Documentation: Screenshots & Source Code

This document is formatted to perfectly match the three requirements of the IT15 Final Project Documentation rubric. For the **(Source Code)** requirements, you can take a screenshot of the provided C# code blocks using your Snipping Tool to paste directly into your final paper alongside the UI screenshots.

---

## 1. Prototype (Backend)

### 1.1 Home Page
**Label:** Public Landing Page (UI & Routing Code)
**Description:** This is the main public-facing landing page of the HomeServe IT platform. New visitors interact with this screen to learn about the services offered.
**UI Screenshot:** 
![Home Page](/Users/kylechristiancabanig/MyMvcProjects/HomeServe IT/ScreenshotBot/Screenshots/1_HomePage.png)
**Source Code (Routing API):**
```csharp
// HomeController.cs
public class HomeController : Controller
{
    public IActionResult Index()
    {
        // Renders the main landing page
        return View();
    }
}
```

### 1.2 Login Page
**Label:** User Authentication Login (UI & Identity API)
**Description:** This screen handles secure access for all users. The system uses ASP.NET Identity to verify credentials and redirect the user to their role-based dashboard.
**UI Screenshot:** 
![Login Page](/Users/kylechristiancabanig/MyMvcProjects/HomeServe IT/ScreenshotBot/Screenshots/2_LoginPage.png)
**Source Code (Identity Authentication API):**
```csharp
// Login.cshtml.cs
var result = await _signInManager.PasswordSignInAsync(
    Input.Email, 
    Input.Password, 
    Input.RememberMe, 
    lockoutOnFailure: false
);

if (result.Succeeded)
{
    _logger.LogInformation("User logged in.");
    return LocalRedirect(returnUrl);
}
```

### 1.3 Register Page
**Label:** New Account Registration (UI & Entity Framework)
**Description:** This form allows new customers to create an account on the platform securely.
**UI Screenshot:** 
![Register Page](/Users/kylechristiancabanig/MyMvcProjects/HomeServe IT/ScreenshotBot/Screenshots/3_RegisterPage.png)
**Source Code (User Creation API):**
```csharp
// Register.cshtml.cs
var user = new ApplicationUser { 
    UserName = Input.Email, 
    Email = Input.Email, 
    FullName = Input.FullName,
    DateCreated = DateTime.UtcNow 
};
var result = await _userManager.CreateAsync(user, Input.Password);
if (result.Succeeded)
{
    await _userManager.AddToRoleAsync(user, Roles.Customer);
}
```

### 1.4 Admin Dashboard
**Label:** Admin Control Panel (UI & Aggregation)
**Description:** Centralized hub for System Administrators to view total revenue, active technicians, and pending service requests through interactive charts.
**UI Screenshot:** 
![Admin Dashboard](/Users/kylechristiancabanig/MyMvcProjects/HomeServe IT/ScreenshotBot/Screenshots/4_AdminDashboard.png)
**Source Code (LINQ Aggregation API):**
```csharp
// DashboardController.cs
var dashboardData = new DashboardViewModel
{
    TotalRevenue = await _context.Invoices.SumAsync(i => i.TotalAmount),
    PendingRequests = await _context.ServiceRequests.CountAsync(s => s.Status == "Pending"),
    LowStockItemsCount = await _context.InventoryItems.CountAsync(i => i.StockQuantity <= i.ReorderLevel)
};
```

### 1.5 Service Booking Module
**Label:** Customer Service Request Form (UI & Parameterized Input)
**Description:** Allows logged-in customers to book an IT service or home repair, saving it securely to the database.
**UI Screenshot:** 
![Service Booking Module](/Users/kylechristiancabanig/MyMvcProjects/HomeServe IT/ScreenshotBot/Screenshots/5_ServiceBooking.png)
**Source Code (Database INSERT API):**
```csharp
// OperationsController.cs
var request = new ServiceRequest
{
    CustomerID = customerId,
    IssueDescription = issueDescription,
    ScheduledDate = scheduledDate,
    Status = "Pending"
};
_context.ServiceRequests.Add(request);
await _context.SaveChangesAsync();
```

---

## 2. API FUNCTIONS/FEATURES

### 2.1 File Upload & Processing API
**Label:** Secure Image Upload API
**Description:** Discuss the process on how it works: When a customer uploads an image of their broken device, the API generates a unique GUID filename to prevent overwriting, validates the file extension against an allowed list to prevent malicious uploads, and streams the file asynchronously to the local server storage.
**Source Code Screenshot Target:**
```csharp
// OperationsController.cs - AddServiceRequest Method
if (image != null && image.Length > 0)
{
    var ext = Path.GetExtension(image.FileName).ToLowerInvariant();
    var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
    if (allowed.Contains(ext))
    {
        var fileName = $"{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(uploadsDir, fileName);
        using var stream = new FileStream(filePath, FileMode.Create);
        await image.CopyToAsync(stream);
        imagePath = $"/uploads/service-requests/{fileName}";
    }
}
```

### 2.2 Inventory Threshold & Restock Algorithm
**Label:** Automated Stock Management Algorithm
**Description:** Discuss the process on how it works: The system fetches the specific inventory item via Entity Framework Core's `FindAsync`. It then executes a state-change algorithm that safely increments the `StockQuantity` property by the incoming restock value, executing an atomic `UPDATE` query when `SaveChangesAsync` is called.
**Source Code Screenshot Target:**
```csharp
// FinanceController.cs
[HttpPost]
public async Task<IActionResult> RestockInventory(int itemId, int quantity)
{
    var item = await _context.InventoryItems.FindAsync(itemId);
    if (item != null)
    {
        item.StockQuantity += quantity;
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Restocked {quantity} pcs of '{item.ItemName}'.";
    }
    return RedirectToAction(nameof(Inventory));
}
```

---

## 3. SECURITY FEATURES

### 3.1 Role-Based Access Control (RBAC)
**Label:** Secure Controller Authorization
**Description:** Discuss the process on how it works: ASP.NET Core Middleware intercepts HTTP requests directed at sensitive controllers (like `OperationsController`). It reads the authentication cookie, parses the user's claims, and verifies if the user holds the "Administrator" role. If unauthorized, the user is safely redirected or blocked with a 403 Forbidden response.
**Source Code Screenshot Target:**
```csharp
// OperationsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HomeServeIT.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = Roles.Administrator)] // SECURITY FEATURE: Role Restriction
    public class OperationsController : Controller
    {
        // Secure endpoints inside...
    }
}
```

### 3.2 Anti-Forgery & CSRF Protection
**Label:** Cross-Site Request Forgery Validation
**Description:** Discuss the process on how it works: Security is enforced on data-mutating forms via the `[ValidateAntiForgeryToken]` attribute and the `@Html.AntiForgeryToken()` tag helper. This generates a cryptographically secure, session-unique hidden token inside the HTML form that must perfectly match the cookie token sent by the browser upon submission.
**Source Code Screenshot Target:**
```csharp
// Login.cshtml & C# Controller equivalent
<form id="account" method="post">
    <!-- SECURITY FEATURE: Anti-Forgery Token generation (auto-injected by ASP.NET Core) -->
    <div asp-validation-summary="ModelOnly" class="text-danger" role="alert"></div>
    
    <!-- Input Fields... -->
    
    <button id="login-submit" type="submit" class="btn btn-primary">Sign in</button>
</form>
```
