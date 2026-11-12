using BotFactory.Core.Services;

namespace BotFactory.Tests;

public class GroundingGateTests
{
    private const string Note = "No tengo esa información confirmada. Por favor, vea los datos de contacto: *462 · gob.do";

    private static GroundingGate Gate(params string[] evidence)
    {
        var gate = new GroundingGate(Note);
        foreach (var e in evidence)
            gate.AddEvidence(e);
        return gate;
    }

    private static string Run(GroundingGate gate, params string[] deltas)
    {
        var parts = new List<string>();
        foreach (var d in deltas)
            parts.AddRange(gate.Push(d));
        parts.AddRange(gate.Flush());
        return string.Concat(parts);
    }

    [Fact]
    public void ProseWithoutData_PassesUntouched()
    {
        var gate = Gate("{\"resultado\":\"El trámite es gratuito.\"}");
        Assert.Equal("El trámite es gratuito. ¿Le ayudo con algo más?", Run(gate, "El trámite es gratuito. ¿Le ayudo con algo más?"));
        Assert.Empty(gate.Blocked);
    }

    [Fact]
    public void SupportedPhone_Passes_WithOrWithoutCountryCode()
    {
        var gate = Gate("{\"telefono\":\"(305) 358-3220\"}");
        Assert.Equal("Llame al +1 305-358-3220.", Run(gate, "Llame al +1 305-358-3220."));
        Assert.Empty(gate.Blocked);
    }

    [Fact]
    public void InventedPhone_DropsTheSentence_AndClosesWithTheNote()
    {
        var gate = Gate("{\"institucion\":\"JCE\",\"telefono\":\"809-539-5419\"}");
        var output = Run(gate, "La JCE atiende en la sede central. Su línea directa es 809-555-0000. ¿Algo más?");

        Assert.Equal("La JCE atiende en la sede central. ¿Algo más?\n\n" + Note, output);
        var blocked = Assert.Single(gate.Blocked);
        Assert.Equal("phone", blocked.Kind);
        Assert.Equal("809-555-0000", blocked.Value);
    }

    [Fact]
    public void PhoneSaidDigitByDigit_IsCheckedLikeAnyPhone()
    {
        // Voice-friendly answers write the number in words; it is still a datum.
        var spoken = "El teléfono general es el ocho, cero, nueve; cinco, dos, tres; cero, cuatro, cero, cuatro.";

        var supported = Gate("{\"telefono\":\"809-523-0404\"}");
        Assert.Equal(spoken, Run(supported, spoken));
        Assert.Empty(supported.Blocked);

        var invented = Gate("{\"telefono\":\"809-539-5419\"}");
        Run(invented, spoken);
        Assert.Equal("phone", Assert.Single(invented.Blocked).Kind);
    }

    [Fact]
    public void FewDigitWords_AreNotAPhone()
    {
        // "la línea asterisco, cuatro, seis, dos" or "dos o tres días" are not phones.
        var gate = Gate("Línea *462.");
        Assert.Equal("Llame a la línea asterisco, cuatro, seis, dos en dos o tres días.",
            Run(gate, "Llame a la línea asterisco, cuatro, seis, dos en dos o tres días."));
        Assert.Empty(gate.Blocked);
    }

    [Fact]
    public void ContactLine_KeepsSupportedItems_DropsOnlyTheInventedOne()
    {
        var gate = Gate("Consulado en Miami: +1 305-358-3220, despacho@consulado.gob.do");
        var output = Run(gate, "Por favor, vea los datos de contacto: +1 305-358-3220 · +1 305-999-1111 · despacho@consulado.gob.do");

        Assert.StartsWith("Por favor, vea los datos de contacto: +1 305-358-3220 · despacho@consulado.gob.do", output);
        Assert.DoesNotContain("999-1111", output);
        Assert.Equal("+1 305-999-1111", Assert.Single(gate.Blocked).Value);
    }

    [Theory]
    [InlineData("{\"precio\":\"RD$600.00\"}", "La solicitud tiene un costo de seiscientos pesos.")]
    [InlineData("{\"precio\":\"6,700.00\"}", "El costo normal es de seis mil setecientos pesos.")]
    [InlineData("{\"precio_tableta\":0.20}", "Cuesta veinte centavos la tableta.")]
    [InlineData("{\"precio\":\"1.50\"}", "Cuesta un peso con cincuenta centavos la tableta.")]
    [InlineData("{\"precio\":\"RD$ 1,500\"}", "Cuesta RD$1,500.")]
    public void AmountInTheSources_Passes_InDigitsOrWords(string evidence, string sentence)
    {
        var gate = Gate(evidence);
        Assert.Equal(sentence, Run(gate, sentence));
        Assert.Empty(gate.Blocked);
    }

    [Fact]
    public void InventedAmount_IsBlocked()
    {
        // Observed on staging: "un peso con cincuenta" for enalapril, which no tool gave.
        var gate = Gate("{\"enalapril\":{\"precio\":\"0.20\"}}");
        Run(gate, "El enalapril cuesta trescientos pesos la caja.");
        Assert.Equal("amount", Assert.Single(gate.Blocked).Kind);
    }

    [Fact]
    public void Links_PassWhenTheSourceHasThem_DomainOnlyIsEnough()
    {
        var gate = Gate("{\"enlace\":\"https://www.dgii.gov.do/servicios/rnc\"}");
        Assert.Equal("Visite www.dgii.gov.do.", Run(gate, "Visite www.dgii.gov.do."));

        var other = Gate("{\"enlace\":\"https://www.dgii.gov.do/servicios/rnc\"}");
        Run(other, "La ficha está en https://www.dgii.gov.do/servicios/inventado.");
        Assert.Equal("url", Assert.Single(other.Blocked).Kind);
    }

    [Fact]
    public void InventedEmail_IsBlocked()
    {
        var gate = Gate("{\"correo\":\"info@pgr.gob.do\"}");
        Run(gate, "Escriba a contacto@pgr.gob.do para más información.");
        Assert.Equal("email", Assert.Single(gate.Blocked).Kind);
    }

    [Fact]
    public void ServiceCodeFromThePrompt_Passes()
    {
        var gate = Gate("Si no tienes el dato, ofrece la línea *462 o gob.do.");
        Assert.Equal("Puede llamar al *462.", Run(gate, "Puede llamar al *462."));
        Assert.Empty(gate.Blocked);
    }

    [Fact]
    public void AddressNumbers_MustComeFromTheSources()
    {
        var gate = Gate("{\"direccion\":\"1038 Brickell Avenue, Miami, FL 33131\"}");
        Assert.Equal("Está en la 1038 Brickell Avenue, Miami, Florida 33131.", Run(gate, "Está en la 1038 Brickell Avenue, Miami, Florida 33131."));

        var invented = Gate("{\"direccion\":\"Avenida Las Carreras\"}");
        Run(invented, "Está en la Calle 7777 de Santiago.");
        Assert.Equal("number", Assert.Single(invented.Blocked).Kind);
    }

    [Fact]
    public void YearFromADateInTheSources_Passes()
    {
        var gate = Gate("{\"fecha\":\"2026-11-09\",\"nombre\":\"Día de la Constitución\"}");
        Assert.Equal("Se descansa el lunes nueve de noviembre de 2026.", Run(gate, "Se descansa el lunes nueve de noviembre de 2026."));
    }

    [Fact]
    public void WhatTheUserSaid_CountsAsASource()
    {
        var gate = Gate("Mi pasaporte vence el 15 de marzo de 2027.");
        Assert.Equal("Su pasaporte vence en 2027.", Run(gate, "Su pasaporte vence en 2027."));
    }

    [Fact]
    public void StreamedInPieces_HoldsTheSentenceUntilItEnds()
    {
        var gate = Gate("{\"telefono\":\"809-689-3444\"}");
        Assert.Empty(gate.Push("El teléfono de la DGII es 809-"));
        Assert.Empty(gate.Push("689-34"));
        var ready = gate.Push("44. ");
        Assert.Empty(ready); // the boundary needs the next character to be decided
        ready = gate.Push("Atienden de lunes a viernes.");
        Assert.Equal("El teléfono de la DGII es 809-689-3444. ", Assert.Single(ready));
        Assert.Equal("Atienden de lunes a viernes.", string.Concat(gate.Flush()));
    }

    [Fact]
    public void LineBreaks_EndSentences_AndAListKeepsItsShape()
    {
        var gate = Gate("{\"requisitos\":[\"Cita impresa\",\"Cédula vigente\"],\"telefono\":\"809-532-4233\"}");
        var output = Run(gate, "Los requisitos son:\n- Cita impresa.\n- Llame al 809-000-0000.\n- Cédula vigente.");
        Assert.Equal("Los requisitos son:\n- Cita impresa.\n- Cédula vigente.\n\n" + Note, output);
    }

    [Fact]
    public void Discard_DropsWhatWasHeld()
    {
        var gate = Gate();
        gate.Push("Texto pendiente sin cerrar");
        gate.Discard();
        Assert.Empty(gate.Flush());
    }

    // ── False positives seen on TAINA (point 5 of OGTIC's 7-Nov feedback) ──────────────────

    [Fact]
    public void BonoMadre_PesosWithCentavosInWords_IsOneAmount()
    {
        // Conversations 1759, 2125, 2164, 2309, 2387, 2396, 2399: the answer itself was dropped
        // and the visitor read «No tengo esa información confirmada».
        var gate = Gate("El Bono Madre 2026 fue un pago único de RD$1,500 en mayo, para madres dominicanas con cédula, residentes en el país y con ingresos de hasta RD$38,158.33.");
        var sentence = "Para el Bono Madre 2026, las madres dominicanas residentes en el país podían tener ingresos de hasta treinta y ocho mil ciento cincuenta y ocho pesos con treinta y tres centavos.";

        Assert.Equal(sentence, Run(gate, sentence));
        Assert.Empty(gate.Blocked);
    }

    [Fact]
    public void AmountWithoutItsCentavos_Passes()
    {
        var gate = Gate("con ingresos de hasta RD$38,158.33");
        Assert.Equal("El tope era de treinta y ocho mil ciento cincuenta y ocho pesos.",
            Run(gate, "El tope era de treinta y ocho mil ciento cincuenta y ocho pesos."));
        Assert.Empty(gate.Blocked);
    }

    [Theory]
    [InlineData("{\"combustible\":\"Gasoil regular\",\"rd_por_galon\":270.8}", "- Gasoil regular: doscientos setenta pesos con ochenta centavos el galón.")]
    [InlineData("{\"compra\":60.59,\"venta\":60.9}", "El dólar se compra a sesenta pesos con cincuenta y nueve centavos y se vende a sesenta pesos con noventa centavos.")]
    public void PriceFromATool_InPesosAndCentavos_Passes(string evidence, string sentence)
    {
        // Conversations 1590, 1615 and 1619: the centavos alone were read as an amount of their own.
        var gate = Gate(evidence);
        Assert.Equal(sentence, Run(gate, sentence));
        Assert.Empty(gate.Blocked);
    }

    [Fact]
    public void WrongCentavos_AreStillBlocked()
    {
        var gate = Gate("{\"rd_por_galon\":270.8}");
        Run(gate, "El gasoil cuesta doscientos setenta pesos con noventa y cinco centavos el galón.");
        Assert.Equal("amount", Assert.Single(gate.Blocked).Kind);
    }

    [Theory]
    [InlineData("{\"precios\":{\"Mercado Nuevo\":39,\"Mercado Conaprope\":40}}", "El arroz súper selecto cuesta entre treinta y nueve y cuarenta pesos la libra.")]
    [InlineData("{\"precios\":{\"Mercado Nuevo\":60,\"Mercadom\":84}}", "El pollo procesado está entre sesenta y ochenta y cuatro pesos la libra.")]
    [InlineData("{\"precios\":{\"Mercado Nuevo\":58,\"Mercadom\":50}}", "El pollo vivo está entre cincuenta y cincuenta y ocho pesos la libra.")]
    public void RangeInWords_IsTwoAmounts(string evidence, string sentence)
    {
        // Conversations 1643 and 2413: «sesenta y ochenta y cuatro» was read as one number.
        var gate = Gate(evidence);
        Assert.Equal(sentence, Run(gate, sentence));
        Assert.Empty(gate.Blocked);
    }

    [Fact]
    public void RangeWithAnInventedEnd_IsBlocked()
    {
        var gate = Gate("{\"precios\":{\"Mercado Nuevo\":39,\"Mercado Conaprope\":40}}");
        Run(gate, "El arroz cuesta entre treinta y nueve y cincuenta pesos la libra.");
        Assert.Equal("amount", Assert.Single(gate.Blocked).Kind);
    }

    [Fact]
    public void ThousandsWrittenAsDigitsAndMil_CountAsTheWholeAmount()
    {
        // Conversation 2007: the person wrote «35 mil pesos» and the answer said it back.
        var gate = Gate("Gano como 35 mil pesos al mes, califico?");
        Assert.Equal("Con treinta y cinco mil pesos, usted cumpliría con ese requisito.",
            Run(gate, "Con treinta y cinco mil pesos, usted cumpliría con ese requisito."));
        Assert.Empty(gate.Blocked);
    }

    [Fact]
    public void PhoneSaidWithATwoDigitWord_IsReadWhole()
    {
        // Conversation 2228: «cero cero veinticinco» was cut at «cero cero».
        var sentence = "Las citas se piden a la línea asterisco siete cinco tres desde un celular o al ocho cero nueve, dos cero cero, cero cero veinticinco desde un teléfono fijo.";
        var supported = Gate("{\"citas_por_telefono\":\"*753 desde un celular o 809-200-0025 desde un teléfono fijo\"}");
        Assert.Equal(sentence, Run(supported, sentence));
        Assert.Empty(supported.Blocked);

        var invented = Gate("{\"citas_por_telefono\":\"*753 desde un celular o 809-200-0025 desde un teléfono fijo\"}");
        Run(invented, "Llame al ocho cero nueve, dos cero cero, cero cero treinta y uno.");
        Assert.Equal("phone", Assert.Single(invented.Blocked).Kind);
    }

    [Theory]
    [InlineData("(https://proconsumidor.gob.do/files/precios de productos de primera necesidad.csv)", "Por favor, vea los datos de contacto: https://proconsumidor.gob.do/files/precios%20de%20productos%20de%20primera%20necesidad.csv")]
    [InlineData("(https://proconsumidor.gob.do/files/precios%20de%20productos%20de%20primera%20necesidad.csv)", "Por favor, vea los datos de contacto: https://proconsumidor.gob.do/files/precios%20de%20productos%20de%20primera%20necesidad.csv")]
    public void LinkWithEscapedSpaces_MatchesTheSource(string evidence, string sentence)
    {
        // Conversation 1643: the source wrote the spaces, the answer «%20».
        var gate = Gate(evidence);
        Assert.Equal(sentence, Run(gate, sentence));
        Assert.Empty(gate.Blocked);
    }

    [Fact]
    public void ContactDataFromTheWholeKnowledgeBase_Passes()
    {
        // Conversations 2219, 2303 and 2392: right links and phones that the base has, but not
        // the fragments of that turn's lookup.
        var gate = Gate("{\"resultado\":\"sin fragmentos\"}");
        gate.AddContactEvidence(["https://edeeste.com.do/", "809-920-2081", "*462", "info@proconsumidor.gob.do"]);
        var line = "Por favor, vea los datos de contacto: https://www.edeeste.com.do · 809-920-2081 · *462 · info@proconsumidor.gob.do";

        Assert.Equal(line, Run(gate, line));
        Assert.Empty(gate.Blocked);
    }

    [Fact]
    public void ContactDataFromTheWholeBase_DoesNotBackAmounts()
    {
        // A phone of the base (809-200-0025) must not make «doscientos pesos» a sourced amount.
        var gate = Gate("{\"resultado\":\"sin fragmentos\"}");
        gate.AddContactEvidence(["809-200-0025"]);
        Run(gate, "El trámite cuesta doscientos pesos.");
        Assert.Equal("amount", Assert.Single(gate.Blocked).Kind);
    }

    [Theory]
    [InlineData("Por favor, vea los datos de contacto: 809-286-1009 · ogetic.gob.do", "ogetic.gob.do")]
    [InlineData("Mercado Conaprope: https://www.google.com/maps/search/?api=1&query=Mercado+Conaprope+Santo+Domingo", "https://www.google.com/maps/search/?api=1&query=Mercado+Conaprope+Santo+Domingo")]
    public void LinksTheModelMadeUp_AreStillBlocked(string sentence, string expected)
    {
        // Conversations 1973 (a misspelt domain) and 2412 (a map search the model built).
        var gate = Gate("Portal: https://ogtic.gob.do · Teléfono: 809-286-1009 · https://www.google.com/maps?q=18.4791,-69.9113");
        gate.AddContactEvidence(["https://ogtic.gob.do", "809-286-1009"]);
        Run(gate, sentence);
        var blocked = Assert.Single(gate.Blocked);
        Assert.Equal("url", blocked.Kind);
        Assert.Equal(expected, blocked.Value);
    }

    [Theory]
    [InlineData("treinta y nueve y cuarenta", new[] { "treinta y nueve", "cuarenta" })]
    [InlineData("sesenta y ochenta y cuatro", new[] { "sesenta", "ochenta y cuatro" })]
    [InlineData("treinta y ocho mil ciento cincuenta y ocho", new[] { "treinta y ocho mil ciento cincuenta y ocho" })]
    [InlineData("dos y tres", new[] { "dos", "tres" })]
    public void SpanishNumbers_SplitARunIntoItsNumbers(string words, string[] expected)
    {
        Assert.Equal(expected, SpanishNumbers.Split(words));
    }

    [Theory]
    [InlineData("veintidós", 22)]
    [InlineData("treinta y cinco", 35)]
    [InlineData("ciento cinco", 105)]
    [InlineData("seis mil setecientos", 6700)]
    [InlineData("un millón doscientos mil", 1_200_000)]
    [InlineData("dos mil veintiséis", 2026)]
    public void SpanishNumbers_Parse(string words, int expected)
    {
        Assert.True(SpanishNumbers.TryParse(words, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("1,500.00", new[] { 1500.0 })]
    [InlineData("1.500,00", new[] { 1500.0 })]
    [InlineData("1.500", new[] { 1500.0, 1.5 })]
    [InlineData("6,700", new[] { 6700.0, 6.7 })]
    [InlineData("0.20", new[] { 0.2 })]
    public void NumberCandidates_CoverBothReadings(string token, double[] expected)
    {
        var values = GroundingEvidence.NumberCandidates(token).Select(v => (double)v).ToArray();
        foreach (var e in expected)
            Assert.Contains(e, values);
    }
}
