namespace Aros.Api.Data.Entities;

/// <summary>
/// A CC-CEDICT line. Kept in the database and queried on demand rather than held in memory —
/// ~125k entries is around 20 MB, which a 1 GB Raspberry Pi would rather not carry resident.
/// Pinyin is stored in the app's canonical form (tone numbers, space-separated, ü as v),
/// which is CC-CEDICT's own format bar the ü.
/// </summary>
public class DictionaryEntry
{
    public int Id { get; set; }
    public string Simplified { get; set; } = "";
    public string Traditional { get; set; } = "";
    public string Pinyin { get; set; } = "";
    public string English { get; set; } = "";      // senses separated by /
}
