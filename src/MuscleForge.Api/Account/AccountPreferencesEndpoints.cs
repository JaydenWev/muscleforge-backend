using Microsoft.EntityFrameworkCore;
using MuscleForge.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using MuscleForge.Api.Contracts;
using MuscleForge.Api.Standards;
using MuscleForge.Infrastructure.Identity;

namespace MuscleForge.Api.Account;

public static class AccountPreferencesEndpoints
{
    public static void MapAccountPreferencesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/account/preferences").WithTags("Account").RequireAuthorization();
        group.MapGet("", async (HttpContext context, UserManager<ApplicationUser> users) =>
        {
            var user = await users.GetUserAsync(context.User);
            return user is null ? ApiErrors.ForStatus(401) : Results.Ok(new AccountPreferencesResponse(user.WeightUnit, user.TimeZoneId));
        }).WithName("GetAccountPreferences").Produces<AccountPreferencesResponse>(200).Produces<ApiProblem>(401, "application/problem+json");
        group.MapPut("", async (UpdateAccountPreferencesRequest request, HttpContext context, UserManager<ApplicationUser> users, MuscleForgeDbContext db) =>
        {
            var user = await users.GetUserAsync(context.User);
            if (user is null) return ApiErrors.ForStatus(401);
            var updated = await db.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.WeightUnit, request.WeightUnit!)
                .SetProperty(x => x.TimeZoneId, request.TimeZoneId!), context.RequestAborted);
            return updated == 0 ? ApiErrors.ForStatus(401) : Results.Ok(new AccountPreferencesResponse(request.WeightUnit!, request.TimeZoneId!));
        }).WithName("UpdateAccountPreferences").WithApiValidation<UpdateAccountPreferencesRequest>()
          .Produces<AccountPreferencesResponse>(200).Produces<ApiProblem>(401, "application/problem+json");
    }
}
