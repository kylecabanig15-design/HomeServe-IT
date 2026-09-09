using HomeServeIT.Web.Areas.Admin.Controllers;
using HomeServeIT.Web.Areas.Admin.Models;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace HomeServeIT.Web.Tests;

public sealed class TechnicianDirectoryTests
{
    [Fact]
    public async Task TechniciansSeparatesActiveSuspendedAndArchivedAccounts()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var accounts = await TestDataBuilder.SeedRoleAccountsAsync(context);
        var suspended = await TestDataBuilder.AddTechnicianAsync(context);
        var archived = await TestDataBuilder.AddTechnicianAsync(context);
        suspended.User.LockoutEnd = DateTimeOffset.MaxValue;
        archived.User.IsArchived = true;
        archived.User.LockoutEnd = DateTimeOffset.MaxValue;
        await context.SaveChangesAsync();

        var controller = new OperationsController(context, null!, null!, null!);
        var result = Assert.IsType<ViewResult>(await controller.Technicians());
        var model = Assert.IsType<TechnicianDirectoryViewModel>(result.Model);

        Assert.Contains(model.ActiveTechnicians, technician => technician.TechID == accounts.Technician.TechID);
        Assert.DoesNotContain(model.ActiveTechnicians, technician => technician.TechID == suspended.TechID || technician.TechID == archived.TechID);
        Assert.Contains(model.SuspendedTechnicians, technician => technician.TechID == suspended.TechID);
        Assert.Contains(model.SuspendedTechnicians, technician => technician.TechID == archived.TechID);
    }
}
