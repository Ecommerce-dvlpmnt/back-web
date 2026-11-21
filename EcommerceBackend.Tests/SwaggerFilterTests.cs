using System.Reflection;
using EcommerceBackend;
using EcommerceBackend.Controllers;
using EcommerceBackend.Controllers.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Xunit;

namespace EcommerceBackend.Tests;

public class SwaggerFilterTests
{
    private readonly AuthorizeCheckOperationFilter _filter = new();
    private readonly OpenApiDocument _document;

    public SwaggerFilterTests()
    {
        _document = new OpenApiDocument
        {
            Components = new OpenApiComponents
            {
                SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
                {
                    ["Bearer"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,
                        Scheme = "Bearer"
                    }
                }
            }
        };
    }

    [Fact]
    public void Admin_Endpoint_Should_Have_Lock()
    {
        var operation = new OpenApiOperation();
        var method = typeof(AdminProductsController).GetMethod(nameof(AdminProductsController.GetAll))!;
        var context = CreateContext(typeof(AdminProductsController), method, "api/admin/products");

        _filter.Apply(operation, context);

        Assert.NotNull(operation.Security);
        Assert.Single(operation.Security);
    }

    [Fact]
    public void Admin_Suppliers_Endpoint_Should_Have_Lock()
    {
        var operation = new OpenApiOperation();
        var method = typeof(AdminSuppliersController).GetMethod(nameof(AdminSuppliersController.GetAll))!;
        var context = CreateContext(typeof(AdminSuppliersController), method, "api/admin/suppliers");

        _filter.Apply(operation, context);

        Assert.NotNull(operation.Security);
        Assert.Single(operation.Security);
    }

    [Fact]
    public void Admin_Reviews_Endpoint_Should_Have_Lock()
    {
        var operation = new OpenApiOperation();
        var method = typeof(AdminReviewsController).GetMethod(nameof(AdminReviewsController.GetAll))!;
        var context = CreateContext(typeof(AdminReviewsController), method, "api/admin/reviews");

        _filter.Apply(operation, context);

        Assert.NotNull(operation.Security);
        Assert.Single(operation.Security);
    }

    [Fact]
    public void Public_Client_Endpoint_Should_NOT_Have_Lock()
    {
        var operation = new OpenApiOperation();
        var method = typeof(ProductsController).GetMethod(nameof(ProductsController.GetAll))!;
        var context = CreateContext(typeof(ProductsController), method, "api/products");

        _filter.Apply(operation, context);

        Assert.Null(operation.Security);
    }

    [Fact]
    public void Public_Reviews_Endpoint_Should_NOT_Have_Lock()
    {
        var operation = new OpenApiOperation();
        var method = typeof(ReviewsController).GetMethod(nameof(ReviewsController.Create))!;
        var context = CreateContext(typeof(ReviewsController), method, "api/reviews");

        _filter.Apply(operation, context);

        Assert.Null(operation.Security);
    }

    [Fact]
    public void Admin_Login_AllowAnonymous_Should_NOT_Have_Lock()
    {
        var operation = new OpenApiOperation();
        var method = typeof(AdminAuthController).GetMethod(nameof(AdminAuthController.Login))!;
        var context = CreateContext(typeof(AdminAuthController), method, "api/admin/auth/login");

        _filter.Apply(operation, context);

        Assert.Null(operation.Security);
    }

    [Fact]
    public void Admin_Orders_ConfirmPayment_Should_Have_Lock()
    {
        var operation = new OpenApiOperation();
        var method = typeof(AdminOrdersController).GetMethod(nameof(AdminOrdersController.ConfirmPayment))!;
        var context = CreateContext(typeof(AdminOrdersController), method, "api/admin/orders/{id}/confirm-payment");

        _filter.Apply(operation, context);

        Assert.NotNull(operation.Security);
        Assert.Single(operation.Security);
    }

    [Fact]
    public void Admin_Referrals_Partners_Should_Have_Lock()
    {
        var operation = new OpenApiOperation();
        var method = typeof(AdminReferralsController).GetMethod(nameof(AdminReferralsController.CreatePartner))!;
        var context = CreateContext(typeof(AdminReferralsController), method, "api/admin/referrals/partners");

        _filter.Apply(operation, context);

        Assert.NotNull(operation.Security);
        Assert.Single(operation.Security);
    }

    [Fact]
    public void Public_Referrals_Validate_Should_NOT_Have_Lock()
    {
        var operation = new OpenApiOperation();
        var method = typeof(ReferralsController).GetMethod(nameof(ReferralsController.ValidateCode))!;
        var context = CreateContext(typeof(ReferralsController), method, "api/referrals/validate/{referralCode}");

        _filter.Apply(operation, context);

        Assert.Null(operation.Security);
    }

    [Fact]
    public void Payments_Webhook_Should_NOT_Have_Lock()
    {
        var operation = new OpenApiOperation();
        var method = typeof(PaymentsController).GetMethod(nameof(PaymentsController.HandleWebhook))!;
        var context = CreateContext(typeof(PaymentsController), method, "api/payments/webhook");

        _filter.Apply(operation, context);

        Assert.Null(operation.Security);
    }

    private OperationFilterContext CreateContext(Type controllerType, MethodInfo method, string path)
    {
        var actionDescriptor = new ControllerActionDescriptor
        {
            ControllerTypeInfo = controllerType.GetTypeInfo(),
            MethodInfo = method
        };

        var apiDesc = new ApiDescription
        {
            ActionDescriptor = actionDescriptor,
            RelativePath = path
        };

        return new OperationFilterContext(
            apiDesc,
            new SchemaGenerator(new SchemaGeneratorOptions(), new JsonSerializerDataContractResolver(new System.Text.Json.JsonSerializerOptions())),
            new SchemaRepository(),
            _document,
            method);
    }
}
