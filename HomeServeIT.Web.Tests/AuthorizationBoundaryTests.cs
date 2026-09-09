using HomeServeIT.Web.Constants;
using HomeServeIT.Web.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using AdminOperationsController = HomeServeIT.Web.Areas.Admin.Controllers.OperationsController;
using CustomerServiceRequestsController = HomeServeIT.Web.Areas.Customer.Controllers.ServiceRequestsController;
using TechnicianAssignedJobsController = HomeServeIT.Web.Areas.Technician.Controllers.AssignedJobsController;

namespace HomeServeIT.Web.Tests;

public sealed class AuthorizationBoundaryTests
{
    [Theory]
    [InlineData(typeof(AdminOperationsController), Roles.Administrator)]
    [InlineData(typeof(CustomerServiceRequestsController), Roles.Customer)]
    [InlineData(typeof(TechnicianAssignedJobsController), Roles.Technician)]
    [InlineData(typeof(HomeServeApiController), Roles.Administrator)]
    public void SensitiveController_RequiresItsExpectedIdentityRole(Type controllerType, string expectedRole)
    {
        var roles = controllerType
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .SelectMany(attribute => (attribute.Roles ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        Assert.Contains(expectedRole, roles);
    }
}
