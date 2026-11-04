using BotFactory.Core.Entities;
using BotFactory.Core.Services;

namespace BotFactory.Tests;

public class SpokenPronunciationTests
{
    private static readonly List<PronunciationDictionary.Rule> Rules =
    [
        new("ABC", "a, be, ce"),
        new("Punto ABC", "punto a be ce"),
        new("XYZ", "equis"),
    ];

    private static string Stream(IEnumerable<string> chunks, List<PronunciationDictionary.Rule>? rules = null)
    {
        var pronunciation = new SpokenPronunciation(rules ?? Rules);
        var spoken = new List<string>();
        foreach (var chunk in chunks)
            spoken.AddRange(pronunciation.Push(chunk));
        spoken.AddRange(pronunciation.Flush());
        return string.Concat(spoken);
    }

    private const string Text =
        "Visite el Punto ABC hoy, o el ABC; y también XYZ. ABCD no, ni xABC. Punto ABC. Y el abc, XYZ";

    [Fact]
    public void TheResultIsTheSameWhateverWayTheTextArrives()
    {
        var expected = PronunciationDictionary.Apply(Text, Rules);

        // Cut in two at every position, and one character at a time.
        for (var cut = 0; cut <= Text.Length; cut++)
            Assert.Equal(expected, Stream([Text[..cut], Text[cut..]]));
        Assert.Equal(expected, Stream(Text.Select(c => c.ToString())));
        Assert.Equal(expected, Stream(Text.Split(' ').Select(w => w + " ")).TrimEnd());
    }

    [Fact]
    public void TheStartOfALongerRuleIsHeldBack_TheTextBeforeItIsNot()
    {
        var pronunciation = new SpokenPronunciation(Rules);

        Assert.Equal(["Hola. "], pronunciation.Push("Hola. Punto AB").ToList());
        // «Punto ABC» is complete, but a word at the end may still continue: it waits.
        Assert.Empty(pronunciation.Push("C").ToList());
        // The next word releases it, with the rule applied to the whole of it.
        Assert.Equal(["punto a be ce "], pronunciation.Push(" mañana").ToList());
        Assert.Equal(["mañana"], pronunciation.Flush().ToList());
    }

    [Fact]
    public void ARuleAtTheEndOfTheTurnIsAppliedAtTheFlush()
    {
        var pronunciation = new SpokenPronunciation(Rules);

        Assert.Equal(["Visite "], pronunciation.Push("Visite ABC").ToList());
        Assert.Equal(["a, be, ce"], pronunciation.Flush().ToList());
    }

    [Fact]
    public void AWordThatContinuesIsNotTakenForTheRule()
    {
        // «ABC» + «D»: the rule must not fire on the first chunk.
        Assert.Equal("ABCD", Stream(["ABC", "D"]));
    }

    [Fact]
    public void WithoutRules_TextPassesThroughAtOnce()
    {
        var pronunciation = new SpokenPronunciation([]);

        Assert.Equal(["hola ABC"], pronunciation.Push("hola ABC").ToList());
        Assert.Empty(pronunciation.Flush());
    }

    [Fact]
    public void CountsTheTimesARuleWasApplied()
    {
        var pronunciation = new SpokenPronunciation(Rules);
        _ = pronunciation.Push("El ABC y el XYZ, y Punto ABC. ").ToList();
        _ = pronunciation.Flush().ToList();

        Assert.Equal(3, pronunciation.Replacements);
    }
}
