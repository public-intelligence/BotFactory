using BotFactory.Core.Services;

namespace BotFactory.Tests;

/// <summary>
/// What the voice says and what it points to in the chat. The only announcement is the
/// phrase that takes the place of the contact line «Por favor, vea los datos de contacto:»;
/// what a voice cannot say (links, addresses, phone numbers) is dropped silently anywhere
/// else, and numbers a voice reads fine (years, decrees) stay in the audio.
/// </summary>
public class VoiceSpokenSanitizerTests
{
    // ---- The contact line: one phrase, in «usted», in the place of the whole line. ----

    [Fact]
    public void Sanitize_AnswerWithAContactLine_KeepsTheSpokenPart_AndPointsToTheChatOnce()
    {
        // The shape of a real answer: phone numbers said in words,
        // then the line with the data for the chat.
        var result = VoiceSpokenSanitizer.Sanitize(
            "La institución tiene varias líneas de contacto. Para servicios a usuarios, "
            + "puede llamar al ocho, cero, nueve; cinco, tres, nueve; cinco, cuatro, uno, nueve. "
            + "Por favor, vea los datos de contacto: 809-555-0101 · 809-555-0102 · 809-555-0103 · "
            + "809-555-0104 · https://ejemplo.gob.do/");

        Assert.Equal(
            "La institución tiene varias líneas de contacto. Para servicios a usuarios, "
            + "puede llamar al ocho, cero, nueve; cinco, tres, nueve; cinco, cuatro, uno, nueve. "
            + "Puede ver el enlace y los teléfonos en el chat.",
            result);
        Assert.DoesNotContain("809-", result);
        Assert.DoesNotContain("https://", result);
        Assert.DoesNotContain("Por favor", result);
        Assert.DoesNotContain("Puedes", result);
    }

    [Theory]
    [InlineData("Por favor vea los datos de contacto: 809-555-0101")]
    [InlineData("POR FAVOR, VEA LOS DATOS DE CONTACTO: 809-555-0101")]
    [InlineData("Por favor,  vea los datos de contacto :809-555-0101")]
    public void Sanitize_ContactLine_IsRecognisedWhateverItsCaseAndSpacing(string line)
    {
        Assert.Equal("Puede ver el teléfono en el chat.", VoiceSpokenSanitizer.Sanitize(line));
    }

    [Fact]
    public void Sanitize_InsideTheContactLine_ShortNumbersAndServiceCodesCountAsContacts()
    {
        var result = VoiceSpokenSanitizer.Sanitize("Por favor, vea los datos de contacto: *462 · 1311");

        Assert.Equal("Puede ver los teléfonos en el chat.", result);
    }

    [Fact]
    public void Sanitize_InsideTheContactLine_YearsAreNotContacts()
    {
        var result = VoiceSpokenSanitizer.Sanitize("Por favor, vea los datos de contacto: 809-555-0101 · vigente en 2026");

        Assert.Equal("Puede ver el teléfono en el chat. vigente en 2026", result);
    }

    [Fact]
    public void Sanitize_ContactLineWithNothingRecognisable_NamesTheContactDetails()
    {
        var result = VoiceSpokenSanitizer.Sanitize("Gracias. Por favor, vea los datos de contacto:");

        Assert.Equal("Gracias. Puede ver los datos de contacto en el chat.", result);
    }

    // ---- Without the contact line nothing is announced. ----

    [Fact]
    public void Sanitize_YearsAndDecrees_AreLeftInTheAudio_AndNothingIsOffered()
    {
        // «¿En qué año se creó la oficina?» must not offer numbers.
        const string text = "La oficina se creó mediante el decreto 1090-04 y la Constitución de 1844 sigue vigente en 2026.";

        Assert.Equal(text, VoiceSpokenSanitizer.Sanitize(text));
    }

    [Theory]
    [InlineData("El decreto 1234 de 2004 lo creó.")]
    [InlineData("Son 250 pesos por trámite.")]
    [InlineData("Hay 12000 beneficiarios y 345 oficinas.")]
    public void Sanitize_NumbersOfThreeToSixDigits_AreNotContactsOutsideTheContactLine(string text)
    {
        Assert.Equal(text, VoiceSpokenSanitizer.Sanitize(text));
    }

    [Fact]
    public void Sanitize_Url_WithoutTheLine_IsDroppedSilently()
    {
        var result = VoiceSpokenSanitizer.Sanitize("Más info en https://ejemplo.gob.do/tramites por favor.");

        Assert.Equal("Más info en por favor.", result);
        Assert.DoesNotContain("Puede ver", result);
    }

    [Fact]
    public void Sanitize_MultipleUrls_WithoutTheLine_AreDroppedWithoutAWord()
    {
        var result = VoiceSpokenSanitizer.Sanitize("Mira https://a.example.com y también www.b.example.com/x.");

        Assert.Equal("Mira y también.", result);
    }

    [Fact]
    public void Sanitize_PhoneNumber_WithoutTheLine_IsDroppedSilently()
    {
        var result = VoiceSpokenSanitizer.Sanitize("Llame al 809-555-0101 para ayuda.");

        Assert.Equal("Llame al para ayuda.", result);
        Assert.DoesNotContain("Puede ver", result);
    }

    [Fact]
    public void Sanitize_Email_WithoutTheLine_IsDroppedSilently()
    {
        var result = VoiceSpokenSanitizer.Sanitize("Escriba a contacto@example.go.cr hoy.");

        Assert.Equal("Escriba a hoy.", result);
        Assert.DoesNotContain("@", result);
    }

    [Fact]
    public void Sanitize_TrimsTrailingPunctuationFromUrl()
    {
        Assert.Equal("Ver.", VoiceSpokenSanitizer.Sanitize("Ver https://ejemplo.gob.do."));
    }

    [Fact]
    public void Sanitize_BareDomain_IsTreatedAsUrl()
    {
        var result = VoiceSpokenSanitizer.Sanitize("El portal es puravidacr.go.cr para trámites.");

        Assert.Equal("El portal es para trámites.", result);
    }

    [Fact]
    public void Sanitize_ServiceCodeCopiedInParentheses_IsDropped_TheWordsRemain()
    {
        // The bot says the code in words and writes it again in parentheses: the voice would
        // read «cuatrocientos sesenta y dos» right after «cuatro, seis, dos».
        var result = VoiceSpokenSanitizer.Sanitize("El asterisco cuatro, seis, dos (*462) es la línea del centro.");

        Assert.Equal("El asterisco cuatro, seis, dos es la línea del centro.", result);
    }

    [Fact]
    public void Sanitize_EmergencyNumberCopiedInParentheses_IsDropped()
    {
        var result = VoiceSpokenSanitizer.Sanitize("Marque nueve, uno, uno (911) en una emergencia.");

        Assert.Equal("Marque nueve, uno, uno en una emergencia.", result);
    }

    [Fact]
    public void Sanitize_NoContacts_Unchanged()
    {
        const string text = "Hola, ¿en qué puedo ayudarle hoy?";
        Assert.Equal(text, VoiceSpokenSanitizer.Sanitize(text));
    }

    [Fact]
    public void Sanitize_DoesNotTreatYearAsPhone()
    {
        const string text = "La cita es en 2026 para el trámite.";
        Assert.Equal(text, VoiceSpokenSanitizer.Sanitize(text));
    }

    [Fact]
    public void Sanitize_DoesNotTreatAbbreviationsAsUrls()
    {
        const string text = "Vea al Dr. Juan o p.ej. el formulario.";
        Assert.Equal(text, VoiceSpokenSanitizer.Sanitize(text));
    }

    // ---- Streaming: the same result, whatever way the text arrives. ----

    [Fact]
    public void Push_HoldsAContactLineMarkerThatArrivesInPieces()
    {
        var s = new VoiceSpokenSanitizer();

        // «Por favor, ve» could still become the marker: it is held back, the text before it is not.
        Assert.Equal(["Hola. "], s.Push("Hola. Por favor, ve").ToList());
        Assert.Empty(s.Push("a los datos de contacto: 809-555-0101"));

        Assert.Equal(["Puede ver el teléfono en el chat."], s.Flush().ToList());
    }

    [Fact]
    public void Push_AnUnrelatedPorFavor_IsSpokenOnceItIsClearItIsNotTheMarker()
    {
        var s = new VoiceSpokenSanitizer();
        var spoken = new List<string>();
        spoken.AddRange(s.Push("Espere un momento, por favor"));
        spoken.AddRange(s.Push(", y gracias."));
        spoken.AddRange(s.Flush());

        Assert.Equal("Espere un momento, por favor, y gracias.", string.Concat(spoken));
    }

    [Fact]
    public void Push_TheEndOfAWordThatLooksLikeTheStartOfTheMarker_IsNotHeldBack()
    {
        // «FALLBACK_CAP» ends in a «p», and «por favor…» starts with one: only a word that
        // begins there can be the marker.
        var s = new VoiceSpokenSanitizer();

        Assert.Equal(["FALLBACK_CAP"], s.Push("FALLBACK_CAP").ToList());
    }

    [Fact]
    public void Push_AYearAtTheEndOfAChunk_IsNotLost()
    {
        var s = new VoiceSpokenSanitizer();
        var spoken = new List<string>();
        spoken.AddRange(s.Push("El año fue 2027"));
        spoken.AddRange(s.Push(" y sigue vigente."));
        spoken.AddRange(s.Flush());

        Assert.Equal("El año fue 2027 y sigue vigente.", string.Concat(spoken));
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
        Assert.Equal("Visita hoy.", joined);
        Assert.DoesNotContain("https://", joined);
    }

    [Fact]
    public void Push_StreamsTextBeforeFirstContact()
    {
        var s = new VoiceSpokenSanitizer();
        var before = s.Push("Texto previo seguro. ").ToList();
        Assert.Equal(["Texto previo seguro. "], before);

        // The space before the link is held with it: it stays only if what follows needs it.
        var after = s.Push("Link https://x.com fin.").ToList();
        Assert.Equal(["Link"], after);

        Assert.Equal(" fin.", string.Concat(s.Flush()));
    }

    [Fact]
    public void Push_AnAnswerWithAContactLine_ArrivingWordByWord_GivesTheSameAsAllAtOnce()
    {
        const string answer =
            "La institución tiene varias líneas. Puede llamar al ocho, cero, nueve. "
            + "Por favor, vea los datos de contacto: 809-555-0101 · 809-555-0102 · https://ejemplo.gob.do/";

        var s = new VoiceSpokenSanitizer();
        var spoken = new List<string>();
        foreach (var piece in answer.Split(' ').Select(w => w + " "))
            spoken.AddRange(s.Push(piece));
        spoken.AddRange(s.Flush());

        Assert.Equal(VoiceSpokenSanitizer.Sanitize(answer), string.Concat(spoken).Trim());
        Assert.EndsWith("Puede ver el enlace y los teléfonos en el chat.", string.Concat(spoken).Trim());
    }

    // ---- The phrase. ----

    [Fact]
    public void BuildPhrase_PluralizesBothSides()
    {
        Assert.Equal(
            "Puede ver los enlaces y los teléfonos en el chat.",
            VoiceSpokenSanitizer.BuildPhrase(2, 3));
    }

    [Fact]
    public void BuildPhrase_IncludesEmail()
    {
        Assert.Equal(
            "Puede ver el enlace, el correo y el teléfono en el chat.",
            VoiceSpokenSanitizer.BuildPhrase(1, 1, 1));
    }

    [Fact]
    public void BuildPhrase_WithNothingToName_PointsToTheContactDetails()
    {
        Assert.Equal("Puede ver los datos de contacto en el chat.", VoiceSpokenSanitizer.BuildPhrase(0, 0, 0));
    }
}
