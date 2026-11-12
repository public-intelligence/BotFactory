using BotFactory.Core.Services;

namespace BotFactory.Tests;

/// <summary>The closing contact block of TAINA's answers, as stored on staging, read into buttons.</summary>
public class ContactLineTests
{
    [Fact]
    public void OgticRainAlert_OneLine_BecomesButtons_AndLeavesTheText()
    {
        // OGTIC's test (U20), conversation 2184: the app showed this line in full and buttons under it.
        const string answer = "Hay dos alertas vigentes por inundaciones urbanas. Afectan a San Pedro de Macorís y La Romana.\n\n"
            + "Se recomienda desplazarse a zonas altas.\n\n"
            + "Por favor, vea los datos de contacto: 9-1-1 · 809-472-0909 · https://www.coe.gob.do/index.php/noticias/item/1237-el-coe-descontinua · https://www.coe.gob.do/";

        var split = ContactLine.Split(answer);

        Assert.Equal("Hay dos alertas vigentes por inundaciones urbanas. Afectan a San Pedro de Macorís y La Romana.\n\nSe recomienda desplazarse a zonas altas.", split.DisplayContent);
        Assert.Collection(split.Contacts,
            c => Assert.Equal(("phone", "9-1-1", "tel:911"), (c.Kind, c.Label, c.Href)),
            c => Assert.Equal(("phone", "809-472-0909", "tel:8094720909"), (c.Kind, c.Label, c.Href)),
            c => Assert.Equal(("url", "coe.gob.do", "https://www.coe.gob.do/index.php/noticias/item/1237-el-coe-descontinua"), (c.Kind, c.Label, c.Href)),
            c => Assert.Equal(("url", "coe.gob.do", "https://www.coe.gob.do/"), (c.Kind, c.Label, c.Href)));
    }

    [Fact]
    public void ServiceCodeAndBarePortal_AreButtons()
    {
        // Conversation 2431; «gob.do» alone does not resolve, the portal is www.gob.do.
        var split = ContactLine.Split("El Bono Madre de 2026 ya terminó.\n\nPor favor, vea los datos de contacto: *462 · 809-362-2000 · gob.do");

        Assert.Equal("El Bono Madre de 2026 ya terminó.", split.DisplayContent);
        Assert.Collection(split.Contacts,
            c => Assert.Equal(("phone", "*462", "tel:*462"), (c.Kind, c.Label, c.Href)),
            c => Assert.Equal(("phone", "809-362-2000", "tel:8093622000"), (c.Kind, c.Label, c.Href)),
            c => Assert.Equal(("url", "www.gob.do", "https://www.gob.do"), (c.Kind, c.Label, c.Href)));
    }

    [Fact]
    public void HeadingInCreole_WithTheContactsOnTheNextLine()
    {
        // Conversation 2422: the bot writes the label in the visitor's language.
        const string answer = "Ou ka jwenn plizyè lopital piblik nan Santiago.\n\nTanpri, gade detay kontak yo:\n"
            + "809-583-4047 · https://www.google.com/maps?q=19.4673,-70.7112 · https://www.google.com/maps?q=19.4561,-70.69939";

        var split = ContactLine.Split(answer);

        Assert.Equal("Ou ka jwenn plizyè lopital piblik nan Santiago.", split.DisplayContent);
        Assert.Equal(["809-583-4047", "Ver en el mapa", "Ver en el mapa"], split.Contacts.Select(c => c.Label));
        Assert.Equal("https://www.google.com/maps?q=19.4561,-70.69939", split.Contacts[2].Href);
    }

    [Fact]
    public void HeadingOverAnInstitutionLine_KeepsItsNote()
    {
        // Conversation 2435.
        const string answer = "Si no le resuelven, puede acudir a PROTECOM.\n\nPor favor, vea los datos de contacto:\n"
            + "PROTECOM: 809-683-3100 · 809-683-4122 (Centro de contacto y WhatsApp) · https://protecom.gob.do";

        var split = ContactLine.Split(answer);

        Assert.Equal("Si no le resuelven, puede acudir a PROTECOM.", split.DisplayContent);
        Assert.Equal(["809-683-3100", "809-683-4122", "protecom.gob.do"], split.Contacts.Select(c => c.Label));
        Assert.Equal("Centro de contacto y WhatsApp", split.Contacts[1].Detail);
    }

    [Fact]
    public void ContactsWithANameBeforeThem_KeepTheName()
    {
        // Conversation 2424.
        const string answer = "Gen de lopital piblik nan Puerto Plata.\n\nSilvouplè, gade enfòmasyon kontak yo:\n"
            + "9-1-1 · *753 · 809-200-0025 · Ricardo Limardo: https://www.google.com/maps?q=19.786,-70.6885 · Dr. Francisco G Hardy: https://www.google.com/maps?q=19.79869,-70.69038";

        var split = ContactLine.Split(answer);

        Assert.Equal("Gen de lopital piblik nan Puerto Plata.", split.DisplayContent);
        Assert.Equal(5, split.Contacts.Count);
        Assert.Equal("Ricardo Limardo", split.Contacts[3].Detail);
        Assert.Equal("Dr. Francisco G Hardy", split.Contacts[4].Detail);
    }

    [Fact]
    public void LineClosingASentence_KeepsTheSentences()
    {
        // TAINA's «sin respaldo» note: the contacts close a line that says more.
        const string answer = "El pago ya terminó.\n\nNo tengo esa información confirmada. Puede llamar a la línea asterisco cuatro seis dos o visitar el portal gob.do. Por favor, vea los datos de contacto: *462 · gob.do";

        var split = ContactLine.Split(answer);

        Assert.Equal("El pago ya terminó.\n\nNo tengo esa información confirmada. Puede llamar a la línea asterisco cuatro seis dos o visitar el portal gob.do.", split.DisplayContent);
        Assert.Equal(["*462", "www.gob.do"], split.Contacts.Select(c => c.Label));
    }

    [Fact]
    public void ShortLineAndAPdf_AreButtons()
    {
        var split = ContactLine.Split("Lo primero es su seguridad.\n\nPor favor, vea los datos de contacto: 700");
        var phone = Assert.Single(split.Contacts);
        Assert.Equal(("phone", "700", "tel:700"), (phone.Kind, phone.Label, phone.Href));

        var pdf = ContactLine.Split("Estos son los pasos.\n\nPor favor, vea los datos de contacto:\nhttps://www.gob.do/servicios/renovacion-licencia · https://taina-ficha-servicio.luis-c4a.workers.dev/pdf/renovacion-licencia.pdf");
        Assert.Equal(["www.gob.do", "Ver el PDF"], pdf.Contacts.Select(c => c.Label));
    }

    [Fact]
    public void RepeatedContact_IsOneButton()
    {
        var split = ContactLine.Split("Llame.\n\nPor favor, vea los datos de contacto: 809-472-0909 · (809) 472-0909");
        Assert.Single(split.Contacts);
    }

    [Fact]
    public void NumbersSeparatedBySlashes_AreButtons()
    {
        // Conversation with the consulate in New York (message 12161).
        var split = ContactLine.Split("Su teléfono es el uno, dos, uno, dos.\n\nPor favor, vea los datos de contacto: +1 212-768-2480 / +1 212-768-2481 · infony@mirex.gob.do");
        Assert.Equal(["+1 212-768-2480", "+1 212-768-2481", "infony@mirex.gob.do"], split.Contacts.Select(c => c.Label));
        Assert.Equal("tel:+12127682480", split.Contacts[0].Href);
    }

    [Fact]
    public void ClosingQuestionAfterTheBlock_StaysAfterTheText()
    {
        // Message 14675: the block, then a question.
        var split = ContactLine.Split("Estos son los pasos.\n\nPor favor, vea los datos de contacto: https://taina-ficha-servicio.luis-c4a.workers.dev/plan.pdf?evento=negocio\n\n¿Quiere que le dé más detalles sobre alguno de estos pasos?");
        Assert.Equal("Estos son los pasos.\n\n¿Quiere que le dé más detalles sobre alguno de estos pasos?", split.DisplayContent);
        Assert.Equal("Ver el PDF", Assert.Single(split.Contacts).Label);
    }

    [Theory]
    [InlineData("La oficina queda en la Av. Imbert 50.\n\nPor favor, vea los datos de contacto: 809-583-4047 · Av. Imbert 50, Santiago")] // an address is not a button
    [InlineData("Llame al 809-555-0000 para más información.")] // a sentence, not a block
    [InlineData("Horario: 8:00 a. m. a 4:00 p. m.")]
    [InlineData("Puede escribir a: soporte del portal.")]
    [InlineData("Ve a la página.\n\n809-583-4047 · gob.do")] // contacts alone with no heading
    [InlineData("Por favor, vea los datos de contacto: 809-583-4047\n\nEl horario es de lunes a viernes.")] // the block is not the end
    [InlineData("Por favor, vea los datos de contacto: https://www.superate.gob.do/ · @SuperateRDO")] // a social account is not a button: the line stays whole
    public void AnythingElse_StaysText(string answer)
    {
        var split = ContactLine.Split(answer);
        Assert.Empty(split.Contacts);
        Assert.Equal(answer, split.DisplayContent);
    }

    [Fact]
    public void ForMessage_OnlyAnswersWithABlock()
    {
        Assert.Equal((null, null), ContactLine.ForMessage("User", "Por favor, vea los datos de contacto: 809-583-4047"));
        Assert.Equal((null, null), ContactLine.ForMessage("Assistant", "El trámite es gratuito."));

        var (contacts, display) = ContactLine.ForMessage("Assistant", "Gratis.\n\nPor favor, vea los datos de contacto: 809-583-4047");
        Assert.Single(contacts!);
        Assert.Equal("Gratis.", display);
    }
}
