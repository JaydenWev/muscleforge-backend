using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MuscleForge.Api.Contracts;
using MuscleForge.Api.Standards;
using MuscleForge.Infrastructure.Identity;
using LoginRequest = MuscleForge.Api.Contracts.LoginRequest;
using RefreshRequest = MuscleForge.Api.Contracts.RefreshRequest;

namespace MuscleForge.Api.Authentication;

public static class AuthEndpoints
{
    public static IServiceCollection AddAuthRateLimits(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.AddPolicy("auth-login", context => Partition(context, 10));
            options.AddPolicy("auth-refresh", context => Partition(context, 30));
            options.OnRejected = async (rejection, cancellationToken) =>
            {
                var seconds = rejection.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
                    ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)) : 60;
                rejection.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                await ApiErrors.WriteAsync(rejection.HttpContext, StatusCodes.Status429TooManyRequests);
            };
        });
        return services;
    }

    private static RateLimitPartition<string> Partition(HttpContext context, int limit) =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true });

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Authentication");
        group.MapPost("/login", async (LoginRequest request, SignInManager<ApplicationUser> signIn) =>
        {
            signIn.AuthenticationScheme = IdentityConstants.BearerScheme;
            var result = await signIn.PasswordSignInAsync(request.Email!, request.Password!, false, lockoutOnFailure: true);
            return result.Succeeded ? Results.Empty : ApiErrors.ForStatus(401);
        }).WithName("Login").WithApiValidation<LoginRequest>().RequireRateLimiting("auth-login")
          .Produces<AccessTokenResponse>(200).Produces<ApiProblem>(401, "application/problem+json")
          .Produces<ApiProblem>(429, "application/problem+json");

        group.MapPost("/refresh", async (RefreshRequest request, SignInManager<ApplicationUser> signIn,
            IOptionsMonitor<BearerTokenOptions> options, TimeProvider timeProvider) =>
        {
            var ticket = options.Get(IdentityConstants.BearerScheme).RefreshTokenProtector.Unprotect(request.RefreshToken!);
            if (ticket?.Properties?.ExpiresUtc is not { } expires || timeProvider.GetUtcNow() >= expires ||
                await signIn.ValidateSecurityStampAsync(ticket.Principal) is not ApplicationUser user)
                return ApiErrors.ForStatus(401);
            var principal = await signIn.CreateUserPrincipalAsync(user);
            return Results.SignIn(principal, authenticationScheme: IdentityConstants.BearerScheme);
        }).WithName("Refresh").WithApiValidation<RefreshRequest>().RequireRateLimiting("auth-refresh")
          .Produces<AccessTokenResponse>(200).Produces<ApiProblem>(401, "application/problem+json")
          .Produces<ApiProblem>(429, "application/problem+json");

        group.MapGet("/me", async (HttpContext context, UserManager<ApplicationUser> users) =>
        {
            var user = await users.GetUserAsync(context.User);
            return user?.Email is { } email ? Results.Ok(new CurrentUserResponse(user.Id, email)) : ApiErrors.ForStatus(401);
        }).WithName("GetCurrentUser").RequireAuthorization()
          .Produces<CurrentUserResponse>(200).Produces<ApiProblem>(401, "application/problem+json");
    }
}
