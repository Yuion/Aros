namespace Aros.Api.Data.Entities;

/// <summary>
/// The six ways a word can be tested. The two that ask for characters are multiple choice,
/// since typing them needs a Chinese IME; the rest are typed.
/// </summary>
public enum VocabDirection
{
    CharactersToPinyin,
    CharactersToEnglish,
    /// <summary>
    /// Retired: pinyin and English are both things the learner already reads, so asking one from
    /// the other tests a gloss rather than the language. The values stay for the answers already
    /// recorded under them - see VocabService.Asked, which is what the trainer offers now.
    /// </summary>
    PinyinToEnglish,
    EnglishToPinyin,
    PinyinToCharacters,
    EnglishToCharacters,
}
