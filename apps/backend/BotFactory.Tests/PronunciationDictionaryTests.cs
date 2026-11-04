using BotFactory.Core.Entities;

namespace BotFactory.Tests;

public class PronunciationDictionaryTests
{
    private static List<PronunciationDictionary.Rule> Rules(params (string Written, string Spoken)[] rules) =>
        rules.Select(r => new PronunciationDictionary.Rule(r.Written, r.Spoken)).ToList();

    // ---- Parsing and storage ----

    [Fact]
    public void Parse_AcceptsEveryArrowSpelling()
    {
        var rules = PronunciationDictionary.Parse("ABC → a, be, ce\nXYZ -> equis\nQRS => cu\nTUV = te");

        Assert.Equal(
            [("ABC", "a, be, ce"), ("XYZ", "equis"), ("QRS", "cu"), ("TUV", "te")],
            rules.Select(r => (r.Written, r.Spoken)).ToArray());
    }

    [Fact]
    public void Parse_TrimsCollapsesSpacesAndSkipsBlankLinesAndWhatIsNotARule()
    {
        var rules = PronunciationDictionary.Parse("  Punto   ABC  →   punto   a,  be,  ce \n\n   \nsin flecha\n → vacío\nvacío → \nXYZ → equis");

        Assert.Equal(
            [("Punto ABC", "punto a, be, ce"), ("XYZ", "equis")],
            rules.Select(r => (r.Written, r.Spoken)).ToArray());
    }

    [Fact]
    public void Parse_KeepsTheFirstRuleOfARepeatedWrittenForm_WhateverTheCase()
    {
        var rules = PronunciationDictionary.Parse("ABC → primera\nabc → segunda\nXYZ → equis");

        Assert.Equal([("ABC", "primera"), ("XYZ", "equis")], rules.Select(r => (r.Written, r.Spoken)).ToArray());
    }

    [Fact]
    public void Normalize_IsCanonical_OneRulePerLine()
    {
        Assert.Equal("ABC → a, be, ce\nXYZ → equis", PronunciationDictionary.Normalize("ABC->a,  be, ce\r\n\r\nXYZ = equis"));
        Assert.Equal(string.Empty, PronunciationDictionary.Normalize(null));
        Assert.Equal(string.Empty, PronunciationDictionary.Normalize("  \n "));
    }

    // ---- Validation ----

    [Fact]
    public void Validate_AcceptsRulesAndNothing()
    {
        Assert.Null(PronunciationDictionary.Validate("ABC → a, be, ce\nPunto ABC → punto a, be, ce"));
        Assert.Null(PronunciationDictionary.Validate(null));
        Assert.Null(PronunciationDictionary.Validate(""));
        Assert.Null(PronunciationDictionary.Validate("  \n  "));
    }

    [Theory]
    [InlineData("sin flecha", "without")]
    [InlineData("→ solo lo dicho", "empty side")]
    [InlineData("solo lo escrito →", "empty side")]
    [InlineData("ABC → a\nabc → be", "repeated")]
    public void Validate_RejectsALineThatIsNotARule_SayingWhy(string raw, string reason)
    {
        var error = PronunciationDictionary.Validate(raw);

        Assert.NotNull(error);
        Assert.Contains(reason, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsAWrittenOrSpokenFormOverTheLimits()
    {
        var long50 = new string('x', PronunciationDictionary.MaxWrittenLength + 1);
        var long100 = new string('y', PronunciationDictionary.MaxSpokenLength + 1);

        Assert.Contains("written form", PronunciationDictionary.Validate($"{long50} → a")!, StringComparison.Ordinal);
        Assert.Contains("spoken form", PronunciationDictionary.Validate($"ABC → {long100}")!, StringComparison.Ordinal);
        // Exactly the limits are fine.
        Assert.Null(PronunciationDictionary.Validate(
            $"{new string('x', PronunciationDictionary.MaxWrittenLength)} → {new string('y', PronunciationDictionary.MaxSpokenLength)}"));
    }

    [Fact]
    public void Validate_RejectsMoreRulesThanTheLimit()
    {
        var raw = string.Join('\n', Enumerable.Range(0, PronunciationDictionary.MaxRules + 1).Select(i => $"sigla{i} → dicho{i}"));

        Assert.Contains("Too many", PronunciationDictionary.Validate(raw)!, StringComparison.Ordinal);
        Assert.Null(PronunciationDictionary.Validate(string.Join('\n', Enumerable.Range(0, PronunciationDictionary.MaxRules).Select(i => $"sigla{i} → dicho{i}"))));
    }

    // ---- Applying ----

    [Fact]
    public void Apply_ReplacesWholeWordsOnly_WhateverTheCase()
    {
        var rules = Rules(("ABC", "a, be, ce"));

        var result = PronunciationDictionary.Apply("El ABC, el abc y (ABC); ABCD, xABC y ABC.", rules);

        Assert.Equal("El a, be, ce, el a, be, ce y (a, be, ce); ABCD, xABC y a, be, ce.", result);
    }

    [Fact]
    public void Apply_DoesNotTouchWhatIsInsideAnotherWord()
    {
        var rules = Rules(("abc", "a, be, ce"));

        Assert.Equal("el abcdario y el abcesso", PronunciationDictionary.Apply("el abcdario y el abcesso", rules));
    }

    [Fact]
    public void Apply_TheLongestRuleWins()
    {
        var rules = Rules(("ABC", "a, be, ce"), ("Punto ABC", "punto a be ce"));

        Assert.Equal("Visite el punto a be ce o el a, be, ce.", PronunciationDictionary.Apply("Visite el Punto ABC o el ABC.", rules));
    }

    [Fact]
    public void Apply_WhatItPutsInIsNotLookedAtAgain()
    {
        var rules = Rules(("ABC", "ABC ABC"), ("XYZ", "ABC"));

        Assert.Equal("ABC ABC y ABC", PronunciationDictionary.Apply("ABC y XYZ", rules));
    }

    [Fact]
    public void Apply_ARuleOfSeveralWordsMatchesAnyRunOfSpaces()
    {
        var rules = Rules(("Punto ABC", "punto a be ce"));

        Assert.Equal("Vaya al punto a be ce.", PronunciationDictionary.Apply("Vaya al Punto   ABC.", rules));
    }

    [Fact]
    public void Apply_RulesWithRegexCharactersAreLiteral()
    {
        var rules = Rules(("C++", "ce más más"), ("A.B.", "a punto be punto"));

        Assert.Equal("Programo en ce más más y a punto be punto hoy", PronunciationDictionary.Apply("Programo en C++ y A.B. hoy", rules));
    }

    [Fact]
    public void Apply_AccentsAreKept()
    {
        var rules = Rules(("Supérate", "su, pe, ra, te"));

        Assert.Equal("En su, pe, ra, te, pero no en superate.", PronunciationDictionary.Apply("En supérate, pero no en superate.", rules));
    }

    [Fact]
    public void Apply_WithoutRulesOrText_ReturnsItUnchanged()
    {
        Assert.Equal("ABC", PronunciationDictionary.Apply("ABC", []));
        Assert.Equal(string.Empty, PronunciationDictionary.Apply(string.Empty, Rules(("ABC", "a"))));
        Assert.Null(PronunciationDictionary.Apply(null, Rules(("ABC", "a"))));
    }
}
