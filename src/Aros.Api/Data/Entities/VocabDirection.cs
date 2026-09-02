namespace Aros.Api.Data.Entities;

/// <summary>
/// The six ways a word can be tested. The two that ask for characters are multiple choice,
/// since typing them needs a Chinese IME; the rest are typed.
/// </summary>
public enum VocabDirection
{
    CharactersToPinyin,
    CharactersToEnglish,
    PinyinToEnglish,
    EnglishToPinyin,
    PinyinToCharacters,
    EnglishToCharacters,
}
