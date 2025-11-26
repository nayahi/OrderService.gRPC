using FluentValidation;

namespace OrderService.gRPC.Validators
{
    /// <summary>
    /// Validator para CreateOrderRequest (compatible con proto existente)
    /// </summary>
    public class CreateOrderRequestValidator : AbstractValidator<ECommerceGRPC.OrderService.CreateOrderRequest>
    {
        public CreateOrderRequestValidator()
        {
            RuleFor(x => x.UserId)
                .GreaterThan(0)
                .WithMessage("UserId debe ser mayor a 0");

            RuleFor(x => x.ShippingAddress)
                .NotEmpty()
                .WithMessage("ShippingAddress es requerido")
                .MaximumLength(500)
                .WithMessage("ShippingAddress no puede exceder 500 caracteres");

            RuleFor(x => x.Items)
                .NotEmpty()
                .WithMessage("La orden debe tener al menos un item");

            RuleForEach(x => x.Items).ChildRules(item =>
            {
                item.RuleFor(i => i.ProductId)
                    .GreaterThan(0)
                    .WithMessage("ProductId debe ser mayor a 0");

                item.RuleFor(i => i.Quantity)
                    .GreaterThan(0)
                    .WithMessage("Quantity debe ser mayor a 0");

                item.RuleFor(i => i.UnitPrice)
                    .GreaterThan(0)
                    .WithMessage("UnitPrice debe ser mayor a 0");
            });
        }
    }

    /// <summary>
    /// Validator para GetOrderRequest
    /// </summary>
    public class GetOrderRequestValidator : AbstractValidator<ECommerceGRPC.OrderService.GetOrderRequest>
    {
        public GetOrderRequestValidator()
        {
            RuleFor(x => x.Id)
                .GreaterThan(0)
                .WithMessage("Id debe ser mayor a 0");
        }
    }

    /// <summary>
    /// Validator para UpdateOrderStatusRequest
    /// </summary>
    public class UpdateOrderStatusRequestValidator : AbstractValidator<ECommerceGRPC.OrderService.UpdateOrderStatusRequest>
    {
        public UpdateOrderStatusRequestValidator()
        {
            RuleFor(x => x.Id)
                .GreaterThan(0)
                .WithMessage("Id debe ser mayor a 0");

            RuleFor(x => x.Status)
                .NotEmpty()
                .WithMessage("Status no puede estar vacío")
                .MaximumLength(50)
                .WithMessage("Status no puede exceder 50 caracteres");
        }
    }

    /// <summary>
    /// Validator para CancelOrderRequest
    /// </summary>
    public class CancelOrderRequestValidator : AbstractValidator<ECommerceGRPC.OrderService.CancelOrderRequest>
    {
        public CancelOrderRequestValidator()
        {
            RuleFor(x => x.Id)
                .GreaterThan(0)
                .WithMessage("Id debe ser mayor a 0");

            RuleFor(x => x.Reason)
                .NotEmpty()
                .WithMessage("Reason es requerido para cancelar")
                .MaximumLength(500)
                .WithMessage("Reason no puede exceder 500 caracteres");
        }
    }

    /// <summary>
    /// Validator para GetOrdersRequest
    /// </summary>
    public class GetOrdersRequestValidator : AbstractValidator<ECommerceGRPC.OrderService.GetOrdersRequest>
    {
        public GetOrdersRequestValidator()
        {
            RuleFor(x => x.PageNumber)
                .GreaterThan(0)
                .WithMessage("PageNumber debe ser mayor a 0");

            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 100)
                .WithMessage("PageSize debe estar entre 1 y 100");
        }
    }

    /// <summary>
    /// Validator para GetOrdersByUserRequest
    /// </summary>
    public class GetOrdersByUserRequestValidator : AbstractValidator<ECommerceGRPC.OrderService.GetOrdersByUserRequest>
    {
        public GetOrdersByUserRequestValidator()
        {
            RuleFor(x => x.UserId)
                .GreaterThan(0)
                .WithMessage("UserId debe ser mayor a 0");

            RuleFor(x => x.PageNumber)
                .GreaterThan(0)
                .WithMessage("PageNumber debe ser mayor a 0");

            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 100)
                .WithMessage("PageSize debe estar entre 1 y 100");
        }
    }
}
