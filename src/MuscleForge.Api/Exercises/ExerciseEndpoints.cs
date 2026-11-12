using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MuscleForge.Api.Contracts;
using MuscleForge.Api.Standards;
using MuscleForge.Core.Exercises;
using MuscleForge.Infrastructure.Identity;
using MuscleForge.Infrastructure.Persistence;

namespace MuscleForge.Api.Exercises;

public static class ExerciseEndpoints
{
    public static void MapExerciseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/exercises").WithTags("Exercises").RequireAuthorization();
        group.MapGet("/", List).WithName("ListExercises")
            .Produces<ExercisePageResponse>().Produces<ApiValidationProblem>(400, "application/problem+json")
            .Produces<ApiProblem>(401, "application/problem+json");
        group.MapGet("/{id:guid}", Get).WithName("GetExercise")
            .Produces<ExerciseResponse>().Produces<ApiProblem>(401, "application/problem+json")
            .Produces<ApiProblem>(404, "application/problem+json");
        group.MapPost("/", Create).WithName("CreateExercise").WithApiValidation<CreateExerciseRequest>()
            .Produces<ExerciseResponse>(201).Produces<ApiProblem>(401, "application/problem+json");
        group.MapPut("/{id:guid}", Update).WithName("UpdateExercise").WithApiValidation<UpdateExerciseRequest>()
            .Produces<ExerciseResponse>().Produces<ApiProblem>(401, "application/problem+json")
            .Produces<ApiProblem>(404, "application/problem+json").Produces<ApiProblem>(409, "application/problem+json");
    }

    private static async Task<IResult> List(HttpContext context, UserManager<ApplicationUser> users, MuscleForgeDbContext db,
        string? search, bool? includeArchived, int? offset, int? limit, CancellationToken ct)
    {
        var user = await users.GetUserAsync(context.User);
        if (user is null) return ApiErrors.ForStatus(401);
        var errors = new Dictionary<string, string[]>();
        var term = search?.Trim();
        if (term?.Length > 100) errors["search"] = ["Search must be at most 100 characters after trimming."];
        if (offset < 0) errors["offset"] = ["Offset must be nonnegative."];
        if (limit is < 1 or > 100) errors["limit"] = ["Limit must be between 1 and 100."];
        if (errors.Count > 0) return ApiErrors.Validation(errors);
        var query = db.Exercises.AsNoTracking().Where(x => x.OwnerId == user.Id);
        if (includeArchived != true) query = query.Where(x => x.ArchivedAt == null);
        if (!string.IsNullOrEmpty(term))
        {
            var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(x => EF.Functions.ILike(x.Name, pattern, "\\") || EF.Functions.ILike(x.Equipment, pattern, "\\"));
        }
        var count = await query.CountAsync(ct);
        var skip = offset ?? 0;
        var take = limit ?? 50;
        var items = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip(skip).Take(take).ToListAsync(ct);
        return Results.Ok(new ExercisePageResponse(items.Select(ToResponse).ToArray(), skip, take, count));
    }

    private static async Task<IResult> Get(Guid id, HttpContext context, UserManager<ApplicationUser> users, MuscleForgeDbContext db, CancellationToken ct)
    {
        var user = await users.GetUserAsync(context.User);
        if (user is null) return ApiErrors.ForStatus(401);
        var exercise = await db.Exercises.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == user.Id, ct);
        return exercise is null ? ApiErrors.ForStatus(404) : Results.Ok(ToResponse(exercise));
    }

    private static async Task<IResult> Create(CreateExerciseRequest request, HttpContext context, UserManager<ApplicationUser> users, MuscleForgeDbContext db, CancellationToken ct)
    {
        var user = await users.GetUserAsync(context.User);
        if (user is null) return ApiErrors.ForStatus(401);
        var exercise = new Exercise(user.Id, request.Name!, request.Equipment!, ExerciseTrackingType.ExternalWeightAndReps,
            Enum.Parse<ExerciseLoadConvention>(request.LoadConvention!));
        db.Exercises.Add(exercise);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/exercises/{exercise.Id}", ToResponse(exercise));
    }

    private static async Task<IResult> Update(Guid id, UpdateExerciseRequest request, HttpContext context, UserManager<ApplicationUser> users, MuscleForgeDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var user = await users.GetUserAsync(context.User);
        if (user is null) return ApiErrors.ForStatus(401);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Lock the owner-scoped row so archive transitions and their timestamps are serialized.
        var rows = await db.Exercises.FromSqlInterpolated($"SELECT * FROM \"Exercises\" WHERE \"Id\" = {id} AND \"OwnerId\" = {user.Id} FOR UPDATE").ToListAsync(ct);
        var exercise = rows.SingleOrDefault();
        if (exercise is null) return ApiErrors.ForStatus(404);
        if (exercise.ArchivedAt is not null && request.IsArchived == false) return ApiErrors.StateConflict();
        exercise.Update(request.Name!, request.Equipment!, Enum.Parse<ExerciseLoadConvention>(request.LoadConvention!));
        if (request.IsArchived == true) exercise.Archive(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        // Acknowledge the persisted timestamp precision, including the first archive.
        await db.Entry(exercise).ReloadAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(ToResponse(exercise));
    }

    private static ExerciseResponse ToResponse(Exercise exercise) => new(exercise.Id, exercise.Name, exercise.Equipment,
        exercise.TrackingType.ToString(), exercise.LoadConvention.ToString(), exercise.ArchivedAt is not null, exercise.ArchivedAt);
}
