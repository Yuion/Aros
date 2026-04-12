namespace Aros.Api.Data.Entities;

public enum TestType
{
    KanjiToRomaji,            // Show Kanji → write Romaji + translation
    EnglishToKanjiMultiple,   // Show English → pick correct Kanji from 6
    EnglishToRomaji,          // Show English → write Romaji
}
