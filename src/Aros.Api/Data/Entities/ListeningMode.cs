namespace Aros.Api.Data.Entities;

/// <summary>
/// What a listening question asks for once the clip has played. Characters is 0 so every score
/// recorded before the other two existed keeps counting as what it always was.
/// </summary>
public enum ListeningMode
{
    /// <summary>
    /// Pick the sentence you heard, out of three. Retired: building the sentence from tiles asks
    /// the same question without handing you the answer to recognise, so this one only ever tested
    /// whether the distractors were good. The value stays for the history already recorded under
    /// it — see <see cref="Aros.Api.Listening.ListeningService.Asked"/>, which is what the trainer
    /// offers now.
    /// </summary>
    Characters = 0,

    /// <summary>Write the pinyin of what you heard. Needs <see cref="TtsClip.Pinyin"/>.</summary>
    Pinyin = 1,

    /// <summary>Write the English of what you heard. Needs <see cref="TtsClip.English"/>.</summary>
    English = 2,

    /// <summary>
    /// Rebuild what you heard from character tiles. Needs nothing but the audio, and is the only
    /// question in either trainer that asks about word order.
    /// </summary>
    Ordering = 3,
}
