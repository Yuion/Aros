namespace Aros.Api.Data.Entities;

/// <summary>
/// What a listening question asks for once the clip has played. Characters is 0 so every score
/// recorded before the other two existed keeps counting as what it always was.
/// </summary>
public enum ListeningMode
{
    /// <summary>Pick the sentence you heard, out of three. Needs nothing but the audio.</summary>
    Characters = 0,

    /// <summary>Write the pinyin of what you heard. Needs <see cref="TtsClip.Pinyin"/>.</summary>
    Pinyin = 1,

    /// <summary>Write the English of what you heard. Needs <see cref="TtsClip.English"/>.</summary>
    English = 2,
}
