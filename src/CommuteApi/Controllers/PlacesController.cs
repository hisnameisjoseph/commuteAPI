using CommuteApi.Data;
using CommuteApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CommuteApi.Controllers;

/// <summary>
/// Admin pages for saved places. Antiforgery is enforced globally in Program.cs.
/// Microsoft sign-in is added in milestone 3; until then this only runs locally.
/// </summary>
public class PlacesController(AppDbContext db) : Controller
{
    public const string DuplicateNameMessage = "A place with this name already exists.";

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var places = await db.Places
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        return View(places);
    }

    public IActionResult Create() => View(new PlaceForm());

    [HttpPost]
    public async Task<IActionResult> Create(PlaceForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        if (ModelState.IsValid && await NameTakenAsync(form.Name, exceptId: null, cancellationToken))
        {
            ModelState.AddModelError(nameof(PlaceForm.Name), DuplicateNameMessage);
        }

        if (!ModelState.IsValid)
        {
            return View(form);
        }

        var place = new Place { Name = "" };
        form.ApplyTo(place);
        db.Places.Add(place);

        if (!await TrySaveAsync(form, exceptId: null, cancellationToken))
        {
            return View(form);
        }

        TempData["Status"] = $"Added {place.Name}.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var place = await db.Places.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (place is null)
        {
            return NotFound();
        }

        ViewData["PlaceId"] = id;
        return View(PlaceForm.From(place));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, PlaceForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        var place = await db.Places.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (place is null)
        {
            return NotFound();
        }

        if (ModelState.IsValid && await NameTakenAsync(form.Name, exceptId: id, cancellationToken))
        {
            ModelState.AddModelError(nameof(PlaceForm.Name), DuplicateNameMessage);
        }

        ViewData["PlaceId"] = id;
        if (!ModelState.IsValid)
        {
            return View(form);
        }

        form.ApplyTo(place);

        if (!await TrySaveAsync(form, exceptId: id, cancellationToken))
        {
            return View(form);
        }

        TempData["Status"] = $"Saved {place.Name}.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var place = await db.Places.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return place is null ? NotFound() : View(place);
    }

    [HttpPost, ActionName(nameof(Delete))]
    public async Task<IActionResult> DeleteConfirmed(int id, CancellationToken cancellationToken)
    {
        var place = await db.Places.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (place is not null)
        {
            db.Places.Remove(place);
            await db.SaveChangesAsync(cancellationToken);
            TempData["Status"] = $"Deleted {place.Name}.";
        }

        return RedirectToAction(nameof(Index));
    }

    // Case-insensitive on every provider. SQL Server's default collation already is; SQLite
    // (used in tests) is not, so the comparison is made explicit.
    private Task<bool> NameTakenAsync(string name, int? exceptId, CancellationToken cancellationToken)
    {
        var normalised = name.Trim().ToUpperInvariant();

        // p.Name.ToUpper() is translated to SQL UPPER(); it never runs as .NET code, so the
        // culture and StringComparison analyzers do not apply. string.Equals with a
        // StringComparison cannot be translated by EF Core.
#pragma warning disable CA1304, CA1311, CA1862
        return db.Places.AnyAsync(
            p => p.Name.ToUpper() == normalised && p.Id != exceptId,
            cancellationToken);
#pragma warning restore CA1304, CA1311, CA1862
    }

    // The unique index is the real guarantee. If two saves race past NameTakenAsync, the
    // database rejects the second and the user sees the same message instead of a 500.
    private async Task<bool> TrySaveAsync(PlaceForm form, int? exceptId, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Drop the failed change so the duplicate check below queries the database, not
            // the change tracker, then decide whether this was the unique index.
            db.ChangeTracker.Clear();
            if (!await NameTakenAsync(form.Name, exceptId, cancellationToken))
            {
                throw;
            }

            ModelState.AddModelError(nameof(PlaceForm.Name), DuplicateNameMessage);
            return false;
        }
    }
}
