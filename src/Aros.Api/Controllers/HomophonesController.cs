using System.Text;
using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Aros.Api.Listening;
using Aros.Api.Tts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Controllers;

public record HomophoneRequest(string? Characters, string? Reading);

[ApiController]
[Route("api/[controller]")]
public class HomophonesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var groups = await db.HomophoneGroups
            .OrderBy(g => g.Id)
            .AsNoTracking()
            .Select(g => new { id = g.Id, characters = g.Characters, reading = g.Reading })
            .ToListAsync(ct);

        return Ok(groups);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] HomophoneRequest request, CancellationToken ct)
    {
        var characters = Distinct(ChineseText.Normalize(request.Characters));

        if (Homophones.Runes(characters).Count < 2)
            return BadRequest(new { message = "A group needs at least two different characters, e.g. 他她." });

        var existing = await db.HomophoneGroups.AsNoTracking().ToListAsync(ct);

        // Overlapping groups would make a character's representative ambiguous
        foreach (var rune in Homophones.Runes(characters))
        {
            var clash = existing.FirstOrDefault(g => g.Characters.EnumerateRunes().Contains(rune));
            if (clash is not null)
                return BadRequest(new { message = $"{rune} is already in the group {clash.Characters}. Edit that group instead." });
        }

        var group = new HomophoneGroup
        {
            Characters = characters,
            Reading = string.IsNullOrWhiteSpace(request.Reading) ? null : request.Reading.Trim(),
        };

        db.HomophoneGroups.Add(group);
        await db.SaveChangesAsync(ct);

        return Ok(new { id = group.Id, characters = group.Characters, reading = group.Reading });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var group = await db.HomophoneGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null) return NotFound();

        db.HomophoneGroups.Remove(group);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>Keeps first occurrences only — "他她他" is the same group as "他她".</summary>
    private static string Distinct(string characters)
    {
        var seen = new HashSet<Rune>();
        var sb = new StringBuilder();

        foreach (var rune in characters.EnumerateRunes())
            if (seen.Add(rune)) sb.Append(rune);

        return sb.ToString();
    }
}
