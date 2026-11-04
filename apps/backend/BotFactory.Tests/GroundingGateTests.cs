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
