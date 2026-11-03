using BotFactory.Core.Services;

namespace BotFactory.Tests;

public class VoiceSpokenSanitizerTests
{
    [Fact]
    public void Sanitize_SingleUrl_ReplacesWithSingularPhrase()
    {
        var result = VoiceSpokenSanitizer.Sanitize(
            "Más info en https://ejemplo.gob.do/tramites por favor.");

        Assert.Equal("Más info en Puedes ver el enlace en el chat. por favor.", result);
        Assert.DoesNotContain("https://", result);
    }

    [Fact]
    public void Sanitize_MultipleUrls_UsesPluralPhraseOnce()
    {
        var result = VoiceSpokenSanitizer.Sanitize(
            "Mira https://a.example.com y también www.b.example.com/x.");

        Assert.Contains("Puedes ver los enlaces en el chat.", result);
        Assert.DoesNotContain("https://", result);
        Assert.DoesNotContain("www.", result);
        Assert.Equal(1, CountOccurrences(result, "Puedes ver"));
    }

    [Fact]
    public void Sanitize_SinglePhone_ReplacesWithSingularPhrase()
    {
        var result = VoiceSpokenSanitizer.Sanitize(
            "Llama al 809-555-1234 para ayuda.");

        Assert.Equal("Llama al Puedes ver el teléfono en el chat. para ayuda.", result);
        Assert.DoesNotContain("809", result);
    }

    [Fact]
    public void Sanitize_UrlAndPhone_CombinedPhrase()
    {
        var result = VoiceSpokenSanitizer.Sanitize(
            "Portal https://ejemplo.gob.do o llama 809-555-1234.");

        Assert.Contains("Puedes ver el enlace y el teléfono en el chat.", result);
        Assert.DoesNotContain("https://", result);
        Assert.DoesNotContain("809", result);
    }

    [Fact]
    public void Sanitize_NoContacts_Unchanged()
    {
        const string text = "Hola, ¿en qué puedo ayudarte?";
        Assert.Equal(text, VoiceSpokenSanitizer.Sanitize(text));
    }

    [Fact]
    public void Push_HoldsIncompleteUrlAcrossChunks()
    {
        var s = new VoiceSpokenSanitizer();
        var spoken = new List<string>();
        spoken.AddRange(s.Push("Visita http"));
        spoken.AddRange(s.Push("s://ejemplo.gob.do hoy."));
        spoken.AddRange(s.Flush());

        var joined = string.Concat(spoken);
        Assert.Contains("Puedes ver el enlace en el chat.", joined);
        Assert.DoesNotContain("https://", joined);
        Assert.StartsWith("Visita ", joined);
    }

    [Fact]
    public void Push_StreamsTextBeforeFirstContact()
    {
        var s = new VoiceSpokenSanitizer();
        var before = s.Push("Texto previo seguro. ").ToList();
        Assert.Equal(["Texto previo seguro. "], before);

        var after = s.Push("Link https://x.com fin.").ToList();
        Assert.Equal(["Link "], after);

        var flushed = string.Concat(s.Flush());
        Assert.Contains("Puedes ver el enlace en el chat.", flushed);
        Assert.Contains("fin.", flushed);
        Assert.DoesNotContain("https://", flushed);
    }

    [Fact]
    public void BuildPhrase_PluralizesBothSides()
    {
        Assert.Equal(
            "Puedes ver los enlaces y los teléfonos en el chat.",
            VoiceSpokenSanitizer.BuildPhrase(2, 3));
    }

    [Fact]
    public void BuildPhrase_IncludesEmail()
    {
        Assert.Equal(
            "Puedes ver el enlace, el correo y el teléfono en el chat.",
            VoiceSpokenSanitizer.BuildPhrase(1, 1, 1));
    }

    [Fact]
    public void Sanitize_Email_ReplacesWithPhrase()
    {
        var result = VoiceSpokenSanitizer.Sanitize(
            "Escriba a contacto@example.go.cr por favor.");

        Assert.Contains("Puedes ver el correo en el chat.", result);
        Assert.DoesNotContain("@", result);
    }

    [Fact]
    public void Sanitize_ShortServiceNumber_IsTreatedAsPhone()
    {
        var result = VoiceSpokenSanitizer.Sanitize(
            "El número para solicitar cita es el (1311).");

        Assert.Contains("Puedes ver el teléfono en el chat.", result);
        Assert.DoesNotContain("1311", result);
    }

    [Fact]
    public void Sanitize_DoesNotTreatYearAsPhone()
    {
        const string text = "La cita es en 2026 para el trámite.";
        Assert.Equal(text, VoiceSpokenSanitizer.Sanitize(text));
    }

    [Fact]
    public void Sanitize_TrimsTrailingPunctuationFromUrl()
    {
        var result = VoiceSpokenSanitizer.Sanitize("Ver https://ejemplo.gob.do.");

        Assert.Equal("Ver Puedes ver el enlace en el chat.", result);
    }

    [Fact]
    public void Sanitize_BareDomain_IsTreatedAsUrl()
    {
        var result = VoiceSpokenSanitizer.Sanitize(
            "El portal es puravidacr.go.cr para trámites.");

        Assert.Contains("Puedes ver el enlace en el chat.", result);
        Assert.DoesNotContain("puravidacr", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_DoesNotTreatAbbreviationsAsUrls()
    {
        const string text = "Vea al Dr. Juan o p.ej. el formulario.";
        Assert.Equal(text, VoiceSpokenSanitizer.Sanitize(text));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (string.CompareOrdinal(haystack, i, needle, 0, needle.Length) == 0)
                count++;
        }
        return count;
    }
}
