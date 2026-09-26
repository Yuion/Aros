using System.Text;
using System.Text.Json;
using Aros.Api.Strokes;
using Microsoft.AspNetCore.Mvc;

namespace Aros.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StrokesController(StrokeLibrary strokes) : ControllerBase
{
    /// <summary>
    /// The stroke order of a whole word, one entry per character, in reading order. A character
    /// the dataset does not cover comes back with a null body rather than being dropped, so the
    /// page can say which one it cannot show instead of quietly renumbering the rest.
    ///
    /// The answer is assembled as text because each character's strokes are already JSON in the
    /// pack; parsing 30 paths only to write them out again would be work done twice.
    /// </summary>
    [HttpGet]
    public IActionResult Word([FromQuery] string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return BadRequest(new { error = "No word given." });

        var json = new StringBuilder("[");

        foreach (var character in word.Where(c => !char.IsWhiteSpace(c)))
        {
            if (json.Length > 1) json.Append(',');

            json.Append("{\"character\":")
                .Append(JsonSerializer.Serialize(character.ToString()))
                .Append(",\"data\":")
                .Append(strokes.Find(character) ?? "null")
                .Append('}');
        }

        json.Append(']');

        // The strokes of 你 will be the strokes of 你 tomorrow as well. Letting the browser keep
        // them means a word opened twice costs one request, and the vocabulary page opens a lot
        // of the same words.
        Response.Headers.CacheControl = "public, max-age=2592000, immutable";

        return Content(json.ToString(), "application/json; charset=utf-8");
    }
}
